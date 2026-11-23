namespace MicroinvestConvert.Core.Exchange;

/// <param name="Done">Pages that were taken.</param>
/// <param name="Total">Pages that have to be taken.</param>
public readonly record struct RateProgress(int Done, int Total);

/// <param name="Added">Days that became known.</param>
/// <param name="Missing">Wanted days that are still not known: not published yet, or not taken.</param>
/// <param name="Failure">What stopped it; null when nothing did.</param>
/// <param name="Cancelled">The user stopped it.</param>
public sealed record RateUpdate(int Added, int Missing, RateException? Failure = null, bool Cancelled = false);

/// <summary>
/// Takes the tables of some days and keeps them. The newest day is asked for first: it says
/// whether the source answers and which table is its last. Whatever was taken before a failure
/// is kept, so the next time goes on from there.
/// </summary>
/// <param name="source">Where the tables are taken from.</param>
/// <param name="store">Where they are kept.</param>
/// <param name="pause">How long to wait before asking again for a page that failed; longer every time.</param>
public sealed class RateUpdater(IRateSource source, IRateStore store, TimeSpan? pause = null)
{
    private const int AtOnce = 4;   // polite to the site
    private const int Attempts = 3;
    private const int GiveUpAfter = 4;

    private readonly TimeSpan _pause = pause ?? TimeSpan.FromSeconds(1.5);

    /// <param name="days">The days whose rates are wanted.</param>
    /// <param name="today">No day after it is asked for.</param>
    /// <param name="progress">Told after every page.</param>
    /// <param name="cancel">Stops it; what was taken stays.</param>
    public async Task<RateUpdate> RunAsync(IEnumerable<DateTime> days, DateTime today,
        IProgress<RateProgress>? progress = null, CancellationToken cancel = default)
    {
        var wanted = days.Select(d => d.Date).Where(d => d >= RateCalendar.Earliest && d <= today.Date).ToHashSet();
        var run = new Run(store, wanted);
        var ask = RateCalendar.ToAsk(wanted, run.Known);

        RateException? failure = null;
        bool cancelled = false;
        try
        {
            if (ask.Count > 0)
            {
                progress?.Report(new RateProgress(0, ask.Count));
                run.Take(ask[^1], await GetAsync(ask[^1], cancel).ConfigureAwait(false));
                progress?.Report(new RateProgress(1, ask.Count));

                failure = await TakeRestAsync(ask.Take(ask.Count - 1), ask.Count, run, progress, cancel).ConfigureAwait(false);

                // A holiday, and no later table among the answers: the page of today says whether there is one.
                if (failure == null && run.IsUnsettled && ask[^1] != today.Date)
                    run.Settle((await GetAsync(today.Date, cancel).ConfigureAwait(false)).Date);
            }
        }
        catch (RateException ex)
        {
            failure = ex;
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            cancelled = true;
        }

        run.Finish();
        return new RateUpdate(run.Added, wanted.Count(d => !run.IsKnown(d)), failure, cancelled);
    }

    private async Task<RateException?> TakeRestAsync(IEnumerable<DateTime> days, int total, Run run,
        IProgress<RateProgress>? progress, CancellationToken cancel)
    {
        RateException? failure = null;
        int done = 1, failuresInARow = 0;
        using var giveUp = CancellationTokenSource.CreateLinkedTokenSource(cancel);

        try
        {
            var options = new ParallelOptions { MaxDegreeOfParallelism = AtOnce, CancellationToken = giveUp.Token };
            await Parallel.ForEachAsync(days, options, async (day, token) =>
            {
                giveUp.Token.ThrowIfCancellationRequested();   // the loop itself learns it a little later
                try
                {
                    run.Take(day, await GetAsync(day, token).ConfigureAwait(false));
                    Interlocked.Exchange(ref failuresInARow, 0);
                    progress?.Report(new RateProgress(Interlocked.Increment(ref done), total));
                }
                catch (RateException ex)
                {
                    // One page is missed and asked for the next time; several in a row are the end.
                    failure = ex;
                    if (ex.Failure == RateFailure.NoInternet || Interlocked.Increment(ref failuresInARow) >= GiveUpAfter)
                        await giveUp.CancelAsync().ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            // Given up.
        }

        return failure;
    }

    private async Task<RateTable> GetAsync(DateTime day, CancellationToken cancel)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await source.GetAsync(day, cancel).ConfigureAwait(false);
            }
            catch (RateException ex) when (ex.Failure != RateFailure.NoInternet && attempt < Attempts)
            {
                await Task.Delay(_pause * attempt, cancel).ConfigureAwait(false);
            }
        }
    }

    /// <summary>What one update has taken so far.</summary>
    private sealed class Run(IRateStore store, HashSet<DateTime> wanted)
    {
        private const int SaveEvery = 12;

        private readonly Lock _lock = new();
        private readonly Dictionary<DateTime, RateTable> _tables = [];       // not saved yet, by their own date
        private readonly Dictionary<DateTime, DateTime> _days = [];          // not saved yet: day → its table
        private readonly Dictionary<DateTime, DateTime> _unsettled = [];     // day → a table that may not be the last word
        private readonly Dictionary<DateTime, DateTime> _added = [];         // everything that became known: day → its table
        private DateTime? _latest = store.Latest();
        private bool _broken;

        public IReadOnlySet<DateTime> Known { get; } = store.Days();
        public int Added => _added.Count;
        public bool IsKnown(DateTime day) => Known.Contains(day) || _added.ContainsKey(day);

        public bool IsUnsettled
        {
            get { lock (_lock) return _unsettled.Count > 0; }
        }

        public void Take(DateTime day, RateTable table)
        {
            lock (_lock)
            {
                if (_broken) return;
                _tables[table.Date] = table;
                _unsettled[day] = table.Date;
                Add(table.Date, table.Date);
                SettleUpTo(table.Date);
                if (_tables.Count >= SaveEvery) Save();
            }
        }

        /// <summary>There is a table of that day, so the days before it have got theirs.</summary>
        public void Settle(DateTime latest)
        {
            lock (_lock) SettleUpTo(latest);
        }

        /// <summary>The weekends get the table of their Friday, and everything is kept.</summary>
        public void Finish()
        {
            lock (_lock)
            {
                foreach (var day in wanted.Where(RateCalendar.IsWeekend).Order().ToList())
                    if (!IsKnown(day) && (_added.TryGetValue(day.AddDays(-1), out var table) ? table : store.TableOf(day.AddDays(-1))) is { } before)
                        Add(day, before);
                Save();
            }
        }

        private void SettleUpTo(DateTime latest)
        {
            if (_latest is null || latest > _latest) _latest = latest;
            foreach (var (day, table) in _unsettled.ToList())
            {
                if (!RateCalendar.IsFinal(day, table, _latest.Value)) continue;
                _unsettled.Remove(day);
                foreach (var between in RateCalendar.Days(table, day)) Add(between, table);   // no other table since then
            }
        }

        private void Add(DateTime day, DateTime table)
        {
            if (IsKnown(day)) return;
            _days[day] = _added[day] = table;
        }

        private void Save()
        {
            if (_tables.Count == 0 && _days.Count == 0) return;
            _broken = true;   // until it succeeds: what cannot be kept once is not tried by every page after it
            store.Save([.. _tables.Values], _days);
            _broken = false;
            _tables.Clear();
            _days.Clear();
        }
    }
}
