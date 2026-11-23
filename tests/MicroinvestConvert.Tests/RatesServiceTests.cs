using Microsoft.Data.Sqlite;
using MicroinvestConvert.App;
using MicroinvestConvert.Core.Exchange;
using MicroinvestConvert.Data;

namespace MicroinvestConvert.Tests;

/// <summary>When rates are downloaded, when they are not, and to whom a failure is told.</summary>
public sealed class RatesServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "mic-svc-" + Guid.NewGuid());
    private readonly FakeSource _source = new();

    // Every test is the same on any day: the source has tables up to today.
    private static readonly DateTime Today = DateTime.Today;
    private static readonly DateTime LongAgo = Today.AddDays(-30);

    private string DatabasePath => Path.Combine(_folder, Database.FileName);

    public RatesServiceTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, true);

    private RatesService Service()
    {
        var database = Database.Open(DatabasePath);
        return new RatesService(new RateStore(database), new SettingStore(database), _source, TimeSpan.Zero);
    }

    private static async Task Until(Func<bool> done)
    {
        for (int i = 0; i < 400 && !done(); i++) await Task.Delay(10);
        Assert.True(done());
    }

    private static Task Settled(RatesService rates) => Until(() => !rates.IsRunning);

    // ----- no starting date: nothing happens -----

    [Fact]
    public async Task Without_a_starting_date_nothing_is_ever_asked_for()
    {
        using var rates = Service();
        Assert.Equal((null, true), (rates.From, rates.Auto));   // as installed
        await rates.UpdateQuietlyAsync();
        rates.Need([Today.AddDays(-3)]);
        Assert.Null(await rates.PullAsync());
        Assert.Empty(_source.Asked);
        Assert.Equal("Валутни курсове: няма", rates.Summary);
        Assert.Equal("", rates.LastResult);
    }

    [Fact]
    public async Task A_cleared_date_stops_the_download_and_keeps_what_there_is()
    {
        using var rates = Service();
        rates.From = LongAgo;
        await rates.UpdateQuietlyAsync();
        int asked = _source.Asked.Count, days = rates.Store!.Days().Count;
        Assert.True(days > 25);

        rates.From = null;
        await rates.UpdateQuietlyAsync();
        Assert.Equal((asked, days), (_source.Asked.Count, rates.Store.Days().Count));
    }

    // ----- by itself -----

    [Fact]
    public async Task At_the_start_everything_from_the_starting_date_is_brought()
    {
        using var rates = Service();
        rates.From = LongAgo;
        int arrived = 0;
        rates.Arrived += () => arrived++;
        await rates.UpdateQuietlyAsync();
        Assert.Contains(LongAgo, rates.Store!.Days());
        Assert.Equal(1, arrived);
        Assert.StartsWith("Валутни курсове: ", rates.Summary);
        Assert.Contains(rates.Ranges, r => r.From <= LongAgo && r.To >= Today.AddDays(-3));
        Assert.Matches(@"^\d\d\.\d\d\.\d{4} \d\d:\d\d – \d+ нови дни$", rates.LastResult);
    }

    [Fact]
    public async Task With_the_automatic_download_off_only_the_button_brings_rates()
    {
        using var rates = Service();
        rates.From = LongAgo;
        rates.Auto = false;
        await rates.UpdateQuietlyAsync();
        rates.Need([LongAgo]);
        Assert.Empty(_source.Asked);

        var result = await rates.PullAsync();
        Assert.True(result is { Failure: null, Added: > 25 });
    }

    [Fact]
    public async Task By_itself_no_failure_comes_out()
    {
        using var rates = Service();
        rates.From = LongAgo;
        foreach (var failure in new[] { FakeSource.NoInternet, FakeSource.SiteDown, new RateException(RateFailure.BadPage, "друга страница") })
        {
            _source.Fails = _ => failure;
            await rates.UpdateQuietlyAsync();
            Assert.False(rates.IsRunning);
            Assert.EndsWith(failure.Title.ToLowerInvariant(), rates.LastResult);   // only written in the window
        }

        Assert.Equal("Валутни курсове: няма", rates.Summary);

        using var other = Locked();
        _source.Fails = null;
        await rates.UpdateQuietlyAsync();   // the database cannot be written either
    }

    // Another program writes: until this is closed nothing can be kept.
    private SqliteConnection Locked()
    {
        var other = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        other.Open();
        other.BeginTransaction(deferred: false);
        return other;
    }

    // ----- with the button -----

    [Fact]
    public async Task The_button_answers_with_the_failure()
    {
        using var rates = Service();
        rates.From = LongAgo;
        _source.Fails = _ => FakeSource.NoInternet;
        Assert.Equal(RateFailure.NoInternet, (await rates.PullAsync())!.Failure!.Failure);
        _source.Fails = _ => FakeSource.SiteDown;
        Assert.Equal(RateFailure.SiteDown, (await rates.PullAsync())!.Failure!.Failure);

        _source.Fails = null;
        using var other = Locked();
        await Assert.ThrowsAsync<DatabaseException>(() => rates.PullAsync());
        Assert.False(rates.IsRunning);
    }

    [Fact]
    public async Task Chosen_days_are_brought_without_a_starting_date()
    {
        using var rates = Service();
        await rates.PullAsync([LongAgo]);
        Assert.Contains(LongAgo, rates.Store!.Days());
        Assert.True(rates.Store.Days().Count < 4);
    }

    [Fact]
    public async Task It_can_be_stopped_and_one_at_a_time_is_enough()
    {
        using var rates = Service();
        rates.From = Today.AddDays(-300);
        var gate = new TaskCompletionSource();
        _source.Before = () => gate.Task;

        var pull = rates.PullAsync();
        await Until(() => _source.Asked.Count > 0);
        Assert.True(rates.IsRunning);
        Assert.Null(await rates.PullAsync());   // already
        rates.Stop();
        gate.SetResult();
        Assert.True((await pull)!.Cancelled);
        Assert.Contains("спряно", rates.LastResult);
        Assert.False(rates.IsRunning);
    }

    [Fact]
    public async Task What_was_kept_before_the_database_failed_is_used()
    {
        using var rates = Service();
        rates.From = Today.AddDays(-90);
        int arrived = 0;
        rates.Arrived += () => arrived++;
        SqliteConnection? other = null;
        _source.Before = () =>
        {
            lock (_source)
                if (other == null && rates.Store!.Days().Count > 0) other = Locked();
            return Task.CompletedTask;
        };

        await Assert.ThrowsAsync<DatabaseException>(() => rates.PullAsync());
        other!.Dispose();
        Assert.Equal((1, false), (arrived, rates.IsRunning));
        Assert.InRange(rates.Store!.Days().Count, 12, 60);
        Assert.EndsWith("курсовете не могат да бъдат записани", rates.LastResult);

        _source.Before = null;
        Assert.True(await rates.PullAsync() is { Failure: null, Added: > 25 });   // the next time goes on from there
        Assert.EndsWith("нови дни", rates.LastResult);
    }

    [Fact]
    public async Task A_note_that_cannot_be_kept_does_not_hide_the_rates()
    {
        using var rates = Service();
        rates.From = LongAgo;
        int arrived = 0;
        rates.Arrived += () => arrived++;
        using (var other = new SqliteConnection($"Data Source={DatabasePath};Pooling=False"))
        {
            other.Open();
            using var drop = other.CreateCommand();
            drop.CommandText = "DROP TABLE Settings";
            drop.ExecuteNonQuery();
        }

        Assert.True(await rates.PullAsync() is { Failure: null, Added: > 25 });
        Assert.Equal(1, arrived);
        Assert.EndsWith("нови дни", rates.LastResult);
    }

    [Fact]
    public async Task It_is_over_when_its_note_is_kept()
    {
        using var rates = Service();
        rates.From = LongAgo;
        _source.Fails = _ => FakeSource.NoInternet;
        Task update;
        using (Locked())   // nothing to keep but the note, and it has to wait
        {
            update = rates.UpdateQuietlyAsync();
            await Until(() => _source.Asked.Count == 1);
            await Task.Delay(300);
            Assert.True(rates.IsRunning);
        }

        await update;
        Assert.False(rates.IsRunning);
        Assert.Equal(rates.LastResult, new SettingStore(Database.Open(DatabasePath)).Get("rates.last"));
    }

    // ----- what documents need -----

    [Fact]
    public async Task A_day_that_a_document_needs_comes_by_itself()
    {
        using var rates = Service();
        rates.From = Today.AddDays(-3);
        int arrived = 0;
        rates.Arrived += () => arrived++;
        rates.Need([LongAgo, LongAgo]);
        await Until(() => arrived == 1);
        Assert.NotNull(rates.Table.Find("USD", LongAgo));

        // It is there: not asked for again.
        int asked = _source.Asked.Count;
        rates.Need([LongAgo]);
        await Settled(rates);
        Assert.Equal(asked, _source.Asked.Count);
    }

    [Fact]
    public async Task A_day_that_could_not_be_brought_is_not_asked_for_over_and_over()
    {
        using var rates = Service();
        rates.From = Today.AddDays(-3);
        _source.Fails = _ => FakeSource.NoInternet;
        for (int i = 0; i < 5; i++)
        {
            rates.Need([LongAgo]);
            await Settled(rates);
        }

        Assert.Single(_source.Asked);
    }

    [Fact]
    public async Task Days_to_come_and_days_before_the_tables_are_not_asked_for()
    {
        using var rates = Service();
        rates.From = LongAgo;
        rates.Need([Today.AddDays(1), Today.AddYears(1), new DateTime(1999, 12, 31)]);
        await Settled(rates);
        Assert.Empty(_source.Asked);
    }

    [Fact]
    public async Task What_is_needed_during_a_download_comes_after_it()
    {
        using var rates = Service();
        rates.From = Today.AddDays(-3);
        var gate = new TaskCompletionSource();
        _source.Before = () => gate.Task;
        var update = rates.UpdateQuietlyAsync();
        await Until(() => _source.Asked.Count > 0);
        rates.Need([LongAgo]);
        Assert.DoesNotContain(LongAgo, _source.Asked);

        gate.SetResult();
        await update;
        await Until(() => rates.Table.Find("USD", LongAgo) != null && !rates.IsRunning);
    }

    [Fact]
    public async Task The_update_at_the_start_waits_for_a_download_that_came_before_it()
    {
        using var rates = Service();
        rates.From = LongAgo;
        var gate = new TaskCompletionSource();
        _source.Before = () => gate.Task;
        rates.Need([Today.AddDays(-60)]);   // a file was opened with the program
        await Until(() => _source.Asked.Count == 1);
        await rates.UpdateQuietlyAsync();
        Assert.Single(_source.Asked);

        gate.SetResult();
        await Until(() => !rates.IsRunning && rates.Store!.Knows(LongAgo) && rates.Store.Days().Count > 25);
        Assert.NotNull(rates.Table.Find("USD", Today.AddDays(-60)));
    }

    [Fact]
    public async Task Stopping_stops_what_waits_too()
    {
        using var rates = Service();
        rates.From = LongAgo;
        var gate = new TaskCompletionSource();
        _source.Before = () => gate.Task;
        rates.Need([Today.AddDays(-60)]);
        await Until(() => _source.Asked.Count == 1);
        await rates.UpdateQuietlyAsync();
        rates.Stop();
        gate.SetResult();
        await Settled(rates);
        await Task.Delay(50);
        Assert.Single(_source.Asked);
        Assert.Empty(rates.Store!.Days());
    }

    // ----- what is kept -----

    [Fact]
    public async Task The_settings_and_the_rates_are_there_the_next_time()
    {
        using (var rates = Service())
        {
            rates.From = LongAgo;
            rates.Auto = false;
            await rates.PullAsync();
        }

        using var again = Service();
        Assert.Equal((LongAgo, false), (again.From, again.Auto));
        Assert.NotNull(again.Table.Find("USD", LongAgo));
        Assert.Contains("нови дни", again.LastResult);
        again.From = null;
        again.Auto = true;
        using var third = Service();
        Assert.Equal((null, true), (third.From, third.Auto));
    }

    [Fact]
    public async Task Closing_the_program_stops_the_download_and_nobody_is_told()
    {
        var rates = Service();
        rates.From = Today.AddDays(-300);
        var gate = new TaskCompletionSource();
        _source.Before = () => gate.Task;
        var update = rates.UpdateQuietlyAsync();
        await Until(() => _source.Asked.Count > 0);

        bool told = false;
        rates.Changed += () => told = true;
        rates.Arrived += () => told = true;
        rates.Dispose();
        gate.SetResult();
        await update;
        Assert.False(told);
    }

    [Fact]
    public void Days_are_shown_shortly()
    {
        using var rates = Service();
        void Keep(params string[] days) => rates.Store!.Save([KikInfoPage.Parse(RatePages.InEuro("02.01.2026"))],
            days.ToDictionary(SomeRates.Day, _ => SomeRates.Day("02.01.2026")));

        Keep("05.01.2026");
        Assert.Equal("Валутни курсове: 05.01.2026", rates.Summary);
        Keep("06.01.2026", "07.01.2026");
        Assert.Equal("Валутни курсове: 05.01.2026 – 07.01.2026", rates.Summary);
        Keep("01.02.2026");
        Assert.Equal("Валутни курсове: 05.01.2026 – 07.01.2026; 01.02.2026", rates.Summary);
        Keep("01.03.2026", "01.04.2026");
        Assert.Equal("Валутни курсове: 05.01.2026 – 01.04.2026, с прекъсвания", rates.Summary);
    }
}
