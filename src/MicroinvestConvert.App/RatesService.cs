using System.Globalization;
using MicroinvestConvert.App.Ui;
using MicroinvestConvert.Core.Exchange;
using MicroinvestConvert.Data;

namespace MicroinvestConvert.App;

/// <summary>
/// The exchange rates: which days there are, the starting date, and the download. By itself – at
/// the start, or when a document needs a day – it says nothing, whatever happens; a failure is
/// told only to the one who pressed a button. Without a starting date it does nothing by itself.
/// </summary>
internal sealed class RatesService : IDisposable
{
    private const string FromKey = "rates.from", AutoKey = "rates.auto", LastKey = "rates.last";

    /// <summary>A day that was asked for without success is not asked for again at once.</summary>
    private static readonly TimeSpan AskAgainAfter = TimeSpan.FromMinutes(5);

    private readonly SettingStore? _settings;
    private readonly IRateSource _source;
    private readonly TimeSpan? _pause;
    private readonly CancellationTokenSource _closing = new();
    private readonly Dictionary<DateTime, DateTime> _asked = [];
    private readonly HashSet<DateTime> _needed = [];
    private CancellationTokenSource? _running;
    private DateTime? _from;
    private bool _auto = true;

    /// <summary>The days, a setting, or how far the download is.</summary>
    public event Action? Changed;

    /// <summary>There are new rates: what is open is calculated again.</summary>
    public event Action? Arrived;

    /// <summary>The database; null when it cannot be opened.</summary>
    public RateStore? Store { get; }

    /// <summary>What the rules look rates up in.</summary>
    public IExchangeRates Table => (IExchangeRates?)Store ?? NoRates.Instance;

    public string SourceName => _source.Name;
    public bool IsRunning => _running != null;
    public RateProgress Progress { get; private set; }

    /// <summary>When the last download was, and what came of it.</summary>
    public string LastResult { get; private set; } = "";

    public RatesService(RateStore? store, SettingStore? settings, IRateSource source, TimeSpan? pause = null)
    {
        Store = store;
        _settings = settings;
        _source = source;
        _pause = pause;
        Load();
    }

    // ----- settings -----

    /// <summary>The rates are kept from this day on. Null = nothing is downloaded.</summary>
    public DateTime? From
    {
        get => _from;
        set
        {
            if (_from == value) return;
            _settings?.Set(FromKey, value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            _from = value;
            Changed?.Invoke();
        }
    }

    /// <summary>Downloaded by itself; otherwise only with the button.</summary>
    public bool Auto
    {
        get => _auto;
        set
        {
            if (_auto == value) return;
            _settings?.Set(AutoKey, value ? "1" : "0");
            _auto = value;
            Changed?.Invoke();
        }
    }

    private void Load()
    {
        try
        {
            _from = DateTime.TryParseExact(_settings?.Get(FromKey), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from) ? from : null;
            _auto = _settings?.Get(AutoKey) != "0";
            LastResult = _settings?.Get(LastKey) ?? "";
        }
        catch (DatabaseException)
        {
            // As if nothing was set.
        }
    }

    // ----- what there is -----

    /// <summary>For the start window.</summary>
    public string Summary => "Валутни курсове: " + (IsRunning && Progress.Total > 1
        ? $"изтеглят се – {Progress.Done} от {Progress.Total}"
        : Store == null ? "базата данни не е достъпна" : DaysText);

    /// <summary>The days there are rates for.</summary>
    public string DaysText
    {
        get
        {
            var ranges = Ranges;
            return ranges.Count switch
            {
                0 => "няма",
                <= 3 => string.Join("; ", ranges.Select(Text)),
                _ => $"{Fmt.Date(ranges[0].From)} – {Fmt.Date(ranges[^1].To)}, с прекъсвания",
            };
        }
    }

    public IReadOnlyList<DayRange> Ranges
    {
        get
        {
            try { return Store == null ? [] : RateCalendar.Ranges(Store.Days()); }
            catch (DatabaseException) { return []; }
        }
    }

    private static string Text(DayRange range) =>
        range.Days == 1 ? Fmt.Date(range.From) : $"{Fmt.Date(range.From)} – {Fmt.Date(range.To)}";

    // ----- by itself -----

    /// <summary>At the start: everything from the starting date until now that is not there.</summary>
    public async Task UpdateQuietlyAsync()
    {
        if (!Auto || From is not { } from || Store == null) return;
        _needed.UnionWith(RateCalendar.Days(from, DateTime.Today));
        await QuietlyAsync(RunNeededAsync);
    }

    /// <summary>Days that open documents need. They come by themselves, when that is allowed.</summary>
    public async void Need(IEnumerable<DateTime> days) => await QuietlyAsync(() => NeedAsync(days));

    private async Task NeedAsync(IEnumerable<DateTime> days)
    {
        if (!Auto || From == null || Store == null || _closing.IsCancellationRequested) return;

        var now = DateTime.UtcNow;
        foreach (var day in days.Select(d => d.Date).Distinct().Where(d => d >= RateCalendar.Earliest && d <= DateTime.Today))
        {
            if (Store.Knows(day) || (_asked.TryGetValue(day, out var when) && now - when < AskAgainAfter)) continue;
            _asked[day] = now;
            _needed.Add(day);
        }

        await RunNeededAsync();
    }

    // One download at a time: what is needed meanwhile is asked for when that one ends.
    private async Task RunNeededAsync()
    {
        while (!IsRunning && _needed.Count > 0 && !_closing.IsCancellationRequested)
        {
            var ask = _needed.ToList();
            _needed.Clear();
            await RunAsync(ask);
        }
    }

    // Nobody asked, so nobody is told. The window says when it was and what came of it.
    private static async Task QuietlyAsync(Func<Task> work)
    {
        try { await work(); }
        catch (DatabaseException) { }
    }

    // ----- with the button -----

    /// <summary>Everything from the starting date until now.</summary>
    /// <exception cref="DatabaseException">What was taken cannot be kept.</exception>
    public Task<RateUpdate?> PullAsync() => From is { } from ? RunAsync(RateCalendar.Days(from, DateTime.Today)) : Task.FromResult<RateUpdate?>(null);

    /// <summary>Only some days, whatever the starting date.</summary>
    /// <exception cref="DatabaseException">What was taken cannot be kept.</exception>
    public Task<RateUpdate?> PullAsync(IEnumerable<DateTime> days) => RunAsync(days);

    /// <summary>What has been taken so far stays; what waits its turn is dropped too.</summary>
    public void Stop()
    {
        _needed.Clear();
        _running?.Cancel();
    }

    private async Task<RateUpdate?> RunAsync(IEnumerable<DateTime> days)
    {
        if (Store == null || _running != null || _closing.IsCancellationRequested) return null;

        RateUpdate? result = null;
        using (var stop = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token))
        {
            _running = stop;
            Progress = default;
            Changed?.Invoke();
            try
            {
                var progress = new Progress<RateProgress>(p =>
                {
                    if (!IsRunning) return;
                    Progress = p;
                    Changed?.Invoke();
                });
                var updater = new RateUpdater(_source, Store, _pause);
                result = await Task.Run(() => updater.RunAsync(days, DateTime.Today, progress, stop.Token));
            }
            finally
            {
                if (!_closing.IsCancellationRequested) Remember(result);
                _running = null;   // it is over when the database is let go
                if (!_closing.IsCancellationRequested)
                {
                    Changed?.Invoke();
                    if (result is not { Added: 0 }) Arrived?.Invoke();
                }
            }
        }

        Need([]);   // what has been waiting
        return result;
    }

    /// <param name="result">Null when the database failed: what was kept before that is there all the same.</param>
    private void Remember(RateUpdate? result)
    {
        LastResult = DateTime.Now.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) + " – " +
                     (result == null ? "курсовете не могат да бъдат записани" : Text(result));
        try
        {
            if (result != null) _settings?.Set(LastKey, LastResult);
        }
        catch (DatabaseException)
        {
            // Only a note. The rates themselves are kept.
        }
    }

    /// <summary>What a download brought, in a few words.</summary>
    public static string Text(RateUpdate result)
    {
        string added = result.Added switch { 0 => "няма нови дни", 1 => "1 нов ден", var n => $"{n} нови дни" };
        return result.Failure != null ? (result.Added > 0 ? added + "; после: " : "") + result.Failure.Title.ToLower(Fmt.Bulgarian)
            : result.Cancelled ? added + ", спряно"
            : added;
    }

    public void Dispose()
    {
        _closing.Cancel();
        (_source as IDisposable)?.Dispose();
    }
}
