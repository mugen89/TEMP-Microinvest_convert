using System.Globalization;
using System.Net;
using System.Net.Sockets;
using MicroinvestConvert.Core.Exchange;
using MicroinvestConvert.Core.Output;
using MicroinvestConvert.Core.Source;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using MicroinvestConvert.Data;
using static MicroinvestConvert.Tests.SomeRates;

namespace MicroinvestConvert.Tests;

public class KikInfoPageTests
{
    [Fact]
    public void A_table_in_euro_is_read()
    {
        var table = KikInfoPage.Parse(RatePages.InEuro());
        Assert.Equal((Day("20.11.2026"), "EUR", 7), (table.Date, table.Currency, table.Rows.Count));
        var usd = table.Rows.Single(r => r.Code == "USD");
        Assert.Equal(("Щатски долар", 1m, 1.1616m, 0.8609m, 1.1616m), (usd.Name, usd.Units, usd.Rate, usd.Reverse, usd.PerEuro(table.Currency)));
    }

    [Fact]
    public void A_table_in_leva_is_read_and_becomes_units_for_one_euro()
    {
        var table = KikInfoPage.Parse(RatePages.InLeva());
        Assert.Equal((Day("30.12.2025"), "BGN"), (table.Date, table.Currency));
        Assert.Equal(1.95583m / 1.7m, table.Rows.Single(r => r.Code == "USD").PerEuro("BGN"));
        Assert.Equal(1.95583m * 100m / 1.2m, table.Rows.Single(r => r.Code == "JPY").PerEuro("BGN"));   // a rate for 100 units
        Assert.Equal(1m, table.Rows.Single(r => r.Code == "EUR").PerEuro("BGN"));
    }

    [Fact]
    public void A_currency_that_is_not_quoted_any_more_is_left_out() =>
        Assert.DoesNotContain(KikInfoPage.Parse(RatePages.InLeva()).Rows, r => r.Code == "RUB");

    [Fact]
    public void Gold_has_no_reverse_rate_and_is_read() =>
        Assert.Equal(7000m, KikInfoPage.Parse(RatePages.InLeva()).Rows.Single(r => r.Code == "XAU").Rate);

    [Fact]
    public void The_date_is_the_one_of_the_table_whatever_was_asked() =>
        Assert.Equal(Day("20.11.2026"), KikInfoPage.Parse(RatePages.InEuro("20.11.2026")).Date);

    [Fact]
    public void Marks_and_spaces_in_the_cells_do_no_harm()
    {
        string html = RatePages.InEuro().Replace(">USD<", "> <b>USD</b>&nbsp;<").Replace("Щатски долар", "Щатски&nbsp;долар\r\n")
            .Replace("1.161600", "\t1,161600 ").Replace("<td", "<TD").Replace("</td>", "</TD>");
        var usd = KikInfoPage.Parse(html).Rows.Single(r => r.Code == "USD");
        Assert.Equal(("Щатски долар", 1.1616m), (usd.Name, usd.Rate));
    }

    public static TheoryData<string, string> Refused => new()
    {
        { "", "няма дата" },
        { "<html><body>Поддръжка на сайта</body></html>", "няма дата" },
        { RatePages.InEuro("").Replace("Валутни курсове за </title>", "</title>"), "няма дата" },
        { RatePages.InEuro("31.02.2026"), "няма дата" },
        { "<h1>Валутни курсове за 20.11.2026</h1><table><tr><td>реклама</td></tr></table>", "няма таблица" },
        { RatePages.InEuro().Replace("Обратен курс", "Нещо друго"), "няма таблица" },
        { RatePages.InEuro().Replace("(EUR)", "(USD)"), "няма таблица" },
        { RatePages.Page("20.11.2026", "Евро (EUR)", []), "празна" },
        { RatePages.TurnedRound(), "не са за едно евро" },
        { RatePages.InEuro().Replace("0.860900", "0.760900"), "USD не си съответстват" },
        { RatePages.InLeva().Replace("1.955830", "1.855830").Replace("0.511292", "0.538842"), "курсът на еврото не е" },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void A_page_that_is_not_certainly_understood_is_refused(string html, string why)
    {
        var ex = Assert.Throws<RateException>(() => KikInfoPage.Parse(html));
        Assert.Equal((RateFailure.BadPage, "Страницата с курсовете не е разпозната"), (ex.Failure, ex.Title));
        Assert.Contains(why, ex.Message);
    }
}

public class KikInfoSourceTests
{
    private static readonly DateTime Asked = Day("20.11.2026");

    private static KikInfoSource Source(Func<HttpRequestMessage, HttpResponseMessage> answer, bool online = true) =>
        new(new FakeSite(answer), _ => Task.FromResult(online));

    private static async Task<RateException> FailureOf(KikInfoSource source) =>
        await Assert.ThrowsAsync<RateException>(() => source.GetAsync(Asked, CancellationToken.None));

    [Fact]
    public async Task The_page_of_the_day_is_asked_for_and_nothing_else_is_sent()
    {
        var site = new FakeSite(r =>
        {
            Assert.Equal(HttpMethod.Get, r.Method);
            Assert.Null(r.Content);
            Assert.DoesNotContain("Microinvest", r.Headers.UserAgent.ToString(), StringComparison.OrdinalIgnoreCase);
            return FakeSite.Html(RatePages.InEuro());
        });
        using var source = new KikInfoSource(site, _ => Task.FromResult(true));
        Assert.Equal(Asked, (await source.GetAsync(Asked, CancellationToken.None)).Date);
        Assert.Equal(["https://kik-info.com/spravochnik/currency-rates.php?date=20.11.2026"], site.Asked);
    }

    [Fact]
    public async Task Without_internet_it_says_so()
    {
        using var source = Source(_ => throw new HttpRequestException("no such host", new SocketException((int)SocketError.HostNotFound)), online: false);
        var failure = await FailureOf(source);
        Assert.Equal((RateFailure.NoInternet, "Няма връзка с интернет"), (failure.Failure, failure.Title));
    }

    [Fact]
    public async Task With_internet_and_no_answer_the_site_is_not_found()
    {
        using var source = Source(_ => throw new HttpRequestException("no such host", new SocketException((int)SocketError.HostNotFound)));
        var failure = await FailureOf(source);
        Assert.Equal((RateFailure.SiteDown, "Сайтът с курсовете не е намерен"), (failure.Failure, failure.Title));
        Assert.Contains("kik-info.com", failure.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task A_site_that_answers_with_an_error_is_down(HttpStatusCode status)
    {
        using var source = Source(_ => FakeSite.Html("", status), online: false);   // an answer is proof enough of the internet
        var failure = await FailureOf(source);
        Assert.Equal(RateFailure.SiteDown, failure.Failure);
        Assert.Contains(((int)status).ToString(), failure.Message);
    }

    [Fact]
    public async Task A_site_that_takes_too_long_is_down()
    {
        var site = new FakeSite((_, _) => throw new TaskCanceledException("timeout", new TimeoutException()));
        using var source = new KikInfoSource(site, _ => Task.FromResult(true));
        Assert.Equal(RateFailure.SiteDown, (await FailureOf(source)).Failure);
    }

    [Fact]
    public async Task Another_page_is_not_taken_for_the_table()
    {
        using var source = Source(_ => FakeSite.Html("<html>Влезте, за да ползвате мрежата</html>"));
        Assert.Equal(RateFailure.BadPage, (await FailureOf(source)).Failure);
    }

    [Fact]
    public async Task An_earlier_table_is_an_answer_and_a_later_one_is_not()
    {
        using (var earlier = Source(_ => FakeSite.Html(RatePages.InEuro("18.11.2026"))))
            Assert.Equal(Day("18.11.2026"), (await earlier.GetAsync(Asked, CancellationToken.None)).Date);
        using var later = Source(_ => FakeSite.Html(RatePages.InEuro("23.11.2026")));
        Assert.Equal(RateFailure.BadPage, (await FailureOf(later)).Failure);
    }

    [Fact]
    public async Task Stopping_is_not_a_failure()
    {
        using var stop = new CancellationTokenSource();
        var site = new FakeSite(async (_, cancel) =>
        {
            await stop.CancelAsync();
            await Task.Delay(Timeout.Infinite, cancel);
            throw new InvalidOperationException();
        });
        using var source = new KikInfoSource(site, _ => Task.FromResult(true));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.GetAsync(Asked, stop.Token));
    }
}

public class RateCalendarTests
{
    private static HashSet<DateTime> Set(params string[] days) => days.Select(Day).ToHashSet();

    [Fact]
    public void Weekends_are_not_asked_for_when_their_friday_is()
    {
        var wanted = RateCalendar.Days(Day("16.11.2026"), Day("23.11.2026")).ToHashSet();   // Monday to Monday
        Assert.Equal(["16.11", "17.11", "18.11", "19.11", "20.11", "23.11"], RateCalendar.ToAsk(wanted, Set()).Select(d => d.ToString("dd.MM")));
    }

    [Fact]
    public void A_weekend_at_the_start_is_asked_for_once()
    {
        var wanted = RateCalendar.Days(Day("03.01.2026"), Day("06.01.2026")).ToHashSet();   // Saturday to Tuesday
        Assert.Equal(["03.01", "05.01", "06.01"], RateCalendar.ToAsk(wanted, Set()).Select(d => d.ToString("dd.MM")));
        Assert.Empty(RateCalendar.ToAsk(Set("04.01.2026"), Set("03.01.2026")));
    }

    [Fact]
    public void What_is_known_is_not_asked_for_again() =>
        Assert.Equal([Day("17.11.2026")], RateCalendar.ToAsk(Set("16.11.2026", "17.11.2026"), Set("16.11.2026")));

    [Theory]
    [InlineData("20.11.2026", "20.11.2026", "20.11.2026", true)]    // its own table
    [InlineData("23.11.2026", "20.11.2026", "20.11.2026", false)]   // a Monday whose table is still to come
    [InlineData("23.11.2026", "20.11.2026", "24.11.2026", true)]    // a holiday: there is a later one already
    [InlineData("21.11.2026", "20.11.2026", "20.11.2026", true)]    // the weekend after a Friday
    [InlineData("22.11.2026", "20.11.2026", "20.11.2026", true)]
    [InlineData("22.11.2026", "19.11.2026", "19.11.2026", false)]   // the table of Friday is still to come
    public void An_earlier_table_is_the_last_word_only_when_no_other_can_come(string day, string published, string latest, bool final) =>
        Assert.Equal(final, RateCalendar.IsFinal(Day(day), Day(published), Day(latest)));

    [Fact]
    public void Days_are_shown_as_periods()
    {
        var ranges = RateCalendar.Ranges([Day("02.01.2026"), Day("01.01.2026"), Day("03.01.2026"), Day("07.01.2026"), Day("01.01.2026")]);
        Assert.Equal([new DayRange(Day("01.01.2026"), Day("03.01.2026")), new DayRange(Day("07.01.2026"), Day("07.01.2026"))], ranges);
        Assert.Equal((3, 1), (ranges[0].Days, ranges[1].Days));
        Assert.Empty(RateCalendar.Ranges([]));
    }
}

public sealed class RateUpdaterTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "mic-rates-" + Guid.NewGuid());
    private readonly RateStore _store;
    private readonly FakeSource _source = new() { Latest = Day("20.11.2026") };

    private static readonly DateTime Monday = Day("23.11.2026");   // „today": its table is not published yet

    public RateUpdaterTests() => _store = new RateStore(Database.Open(Path.Combine(_folder, Database.FileName)));

    public void Dispose() => Directory.Delete(_folder, true);

    private Task<RateUpdate> Run(string from, string to, DateTime? today = null, CancellationToken cancel = default) =>
        new RateUpdater(_source, _store, TimeSpan.Zero).RunAsync(RateCalendar.Days(Day(from), Day(to)), today ?? Monday, cancel: cancel);

    private decimal? Dollar(string day) => _store.Find("USD", Day(day))?.PerEuro;

    private string Days() => string.Join("; ", RateCalendar.Ranges(_store.Days()).Select(r => $"{r.From:dd.MM.yyyy}-{r.To:dd.MM.yyyy}"));

    [Fact]
    public async Task Every_day_until_the_last_table_becomes_known()
    {
        var result = await Run("09.11.2026", "23.11.2026");
        Assert.Equal(new RateUpdate(14, 1), result);                 // today is still to come
        Assert.Equal("09.11.2026-22.11.2026", Days());
        Assert.Equal(FakeSource.Dollar(Day("13.11.2026")), Dollar("15.11.2026"));   // a Sunday has the rate of Friday
        Assert.Equal(Day("13.11.2026"), _store.Find("USD", Day("15.11.2026"))!.Published);
        Assert.Null(Dollar("23.11.2026"));
        Assert.Equal(11, _source.Asked.Count);                       // working days only
    }

    [Fact]
    public async Task The_next_time_only_what_is_missing_is_asked_for()
    {
        await Run("09.11.2026", "23.11.2026");
        _source.Asked.Clear();
        Assert.Equal(new RateUpdate(0, 1), await Run("09.11.2026", "23.11.2026"));
        Assert.Equal([Monday], _source.Asked);

        // In the evening its table is there.
        _source.Latest = Monday;
        Assert.Equal(new RateUpdate(1, 0), await Run("09.11.2026", "23.11.2026"));
        Assert.Equal(FakeSource.Dollar(Monday), Dollar("23.11.2026"));
    }

    /// <summary>What the script this replaces could keep for good: Friday's rate as the one of Monday.</summary>
    [Fact]
    public async Task A_table_that_is_late_is_never_kept_as_the_one_of_the_day()
    {
        _source.Latest = Day("18.11.2026");   // the site is two days behind
        await Run("16.11.2026", "20.11.2026", Day("21.11.2026"));
        Assert.Equal("16.11.2026-18.11.2026", Days());

        _source.Latest = Day("20.11.2026");
        await Run("16.11.2026", "20.11.2026", Day("21.11.2026"));
        Assert.Equal("16.11.2026-20.11.2026", Days());
        Assert.Equal(FakeSource.Dollar(Day("19.11.2026")), Dollar("19.11.2026"));
    }

    [Fact]
    public async Task A_holiday_takes_the_table_before_it()
    {
        _source.Holidays.Add(Day("11.11.2026"));
        await Run("09.11.2026", "13.11.2026");
        Assert.Equal("09.11.2026-13.11.2026", Days());
        Assert.Equal(FakeSource.Dollar(Day("10.11.2026")), Dollar("11.11.2026"));
    }

    [Fact]
    public async Task A_holiday_as_the_last_day_waits_for_a_later_table()
    {
        _source.Holidays.UnionWith([Day("24.12.2026"), Day("25.12.2026")]);
        _source.Latest = Day("23.12.2026");
        await Run("22.12.2026", "25.12.2026", Day("25.12.2026"));
        Assert.Equal("22.12.2026-23.12.2026", Days());

        _source.Latest = Day("28.12.2026");
        await Run("22.12.2026", "28.12.2026", Day("28.12.2026"));
        Assert.Equal("22.12.2026-28.12.2026", Days());
        Assert.Equal(FakeSource.Dollar(Day("23.12.2026")), Dollar("27.12.2026"));   // the Sunday after it, too
    }

    [Fact]
    public async Task One_day_of_the_past_is_enough_by_itself()
    {
        _source.Holidays.Add(Day("03.03.2026"));
        Assert.Equal(new RateUpdate(2, 0), await Run("03.03.2026", "03.03.2026"));   // with the day of its table
        Assert.Equal(FakeSource.Dollar(Day("02.03.2026")), Dollar("03.03.2026"));
        Assert.Equal([Day("03.03.2026"), Monday], _source.Asked);                    // today says whether a later table exists

        _source.Asked.Clear();
        await Run("15.11.2026", "15.11.2026");                                       // a Sunday
        Assert.Equal(FakeSource.Dollar(Day("13.11.2026")), Dollar("15.11.2026"));
        Assert.Equal([Day("15.11.2026")], _source.Asked);
    }

    [Fact]
    public async Task An_earlier_starting_date_fills_what_is_before()
    {
        await Run("16.11.2026", "20.11.2026");
        _source.Asked.Clear();
        await Run("09.11.2026", "20.11.2026");
        Assert.Equal("09.11.2026-20.11.2026", Days());
        Assert.Equal(5, _source.Asked.Count);
    }

    [Fact]
    public async Task Nothing_after_today_is_asked_for()
    {
        Assert.Equal(new RateUpdate(0, 0), await Run("01.12.2026", "31.12.2026"));
        Assert.Empty(_source.Asked);
    }

    [Fact]
    public async Task Without_internet_one_question_is_enough()
    {
        _source.Fails = _ => FakeSource.NoInternet;
        var result = await Run("01.01.2026", "23.11.2026");
        Assert.Equal((RateFailure.NoInternet, 0, 327), (result.Failure!.Failure, result.Added, result.Missing));
        Assert.Single(_source.Asked);
        Assert.Empty(_store.Days());
    }

    [Fact]
    public async Task A_page_that_fails_is_asked_for_again_and_then_missed()
    {
        int failures = 0;
        _source.Fails = day => day == Day("18.11.2026") && failures++ < 2 ? FakeSource.SiteDown : null;
        Assert.Equal(new RateUpdate(5, 0), await Run("16.11.2026", "20.11.2026"));

        _source.Fails = day => day == Day("11.11.2026") ? FakeSource.SiteDown : null;
        var result = await Run("09.11.2026", "13.11.2026");
        Assert.Equal((4, 1, RateFailure.SiteDown), (result.Added, result.Missing, result.Failure!.Failure));
        Assert.Equal("09.11.2026-10.11.2026; 12.11.2026-13.11.2026; 16.11.2026-20.11.2026", Days());

        _source.Fails = null;
        Assert.Equal(new RateUpdate(1, 0), await Run("09.11.2026", "13.11.2026"));
    }

    [Fact]
    public async Task A_site_that_goes_down_is_left_alone_and_what_was_taken_stays()
    {
        _source.Fails = day => day < Day("16.11.2026") ? FakeSource.SiteDown : null;
        var result = await Run("01.01.2026", "20.11.2026");
        Assert.Equal(RateFailure.SiteDown, result.Failure!.Failure);
        Assert.True(_source.Asked.Count < 40, _source.Asked.Count.ToString());
        Assert.Contains(Day("20.11.2026"), _store.Days());
    }

    [Fact]
    public async Task Stopped_by_the_user_it_keeps_what_it_has()
    {
        using var stop = new CancellationTokenSource();
        _source.Before = () =>
        {
            if (_source.Asked.Count == 20) stop.Cancel();
            return Task.CompletedTask;
        };
        var result = await Run("01.01.2026", "20.11.2026", cancel: stop.Token);
        Assert.True(result is { Cancelled: true, Failure: null, Added: > 0 });
        Assert.Equal(result.Added, _store.Days().Count);

        _source.Before = null;
        await Run("01.01.2026", "20.11.2026");
        Assert.Equal("01.01.2026-20.11.2026", Days());
    }

    [Fact]
    public async Task Progress_goes_from_nothing_to_all()
    {
        var seen = new List<RateProgress>();
        await new RateUpdater(_source, _store, TimeSpan.Zero).RunAsync(RateCalendar.Days(Day("02.11.2026"), Day("20.11.2026")), Monday, new Reported(seen));
        Assert.Equal(new RateProgress(0, 15), seen[0]);
        Assert.Equal(15, seen.Max(p => p.Done));
        Assert.All(seen, p => Assert.Equal(15, p.Total));
    }

    private sealed class Reported(List<RateProgress> seen) : IProgress<RateProgress>
    {
        public void Report(RateProgress value)
        {
            lock (seen) seen.Add(value);
        }
    }
}

/// <summary>What „Валутни курсове" shows of a currency, and the workbook it is exported to.</summary>
public class RateViewTests
{
    private static DateTime Day(string text) => SomeRates.Day(text);

    [Theory]
    [InlineData(RateView.ThreeCalendarMonths, "23.11.2026", "01.09.2026")]   // the owner's example
    [InlineData(RateView.ThreeCalendarMonths, "01.12.2026", "01.10.2026")]
    [InlineData(RateView.ThreeCalendarMonths, "30.11.2026", "01.09.2026")]
    [InlineData(RateView.ThreeCalendarMonths, "15.01.2026", "01.11.2025")]
    [InlineData(RateView.ThreeCalendarMonths, "28.02.2026", "01.12.2025")]
    [InlineData(RateView.ThreeMonths, "23.11.2026", "23.08.2026")]
    [InlineData(RateView.ThreeMonths, "31.05.2026", "28.02.2026")]
    [InlineData(RateView.ThreeMonths, "29.02.2028", "29.11.2027")]
    [InlineData(RateView.ThreeMonths, "10.01.2026", "10.10.2025")]
    [InlineData(RateView.Currency, "23.11.2026", "01.01.2026")]
    public void A_period_ends_with_the_last_day_there_is(RateView view, string last, string from) =>
        Assert.Equal(new DayRange(Day(from), Day(last)), RateCalendar.PeriodOf(view, Day("01.01.2026"), Day(last)));

    private static readonly RateLine[] Lines =
    [
        new(Day("23.11.2026"), "GBP", "Британска лира", 0.8437m, Day("23.11.2026")),
        new(Day("22.11.2026"), "GBP", "Британска лира", 0.8401m, Day("20.11.2026")),
        new(Day("21.11.2026"), "IDR", "Индонезийска рупия", 20071.37m, Day("20.11.2026")),
    ];

    [Fact]
    public void The_workbook_has_dates_and_numbers_under_a_title()
    {
        using var workbook = new XSSFWorkbook(new MemoryStream(RatesWorkbook.Build("Валутни курсове – GBP", ["01.09.2026 – 23.11.2026", "Източник: проба"], Lines)));
        var sheet = workbook.GetSheet(RatesWorkbook.SheetName);
        Assert.Equal(["Валутни курсове – GBP", "01.09.2026 – 23.11.2026", "Източник: проба"], Enumerable.Range(0, 3).Select(r => sheet.GetRow(r).GetCell(0).StringCellValue));
        Assert.Null(sheet.GetRow(3));
        Assert.Equal(["Дата", "Код", "Валута", "За 1 евро", "Евро за единица", "Курс от"], sheet.GetRow(4).Cells.Select(c => c.StringCellValue));
        Assert.Equal(4 + Lines.Length, sheet.LastRowNum);

        for (int i = 0; i < Lines.Length; i++)
        {
            var row = sheet.GetRow(5 + i);
            Assert.True(DateUtil.IsCellDateFormatted(row.GetCell(0)) && DateUtil.IsCellDateFormatted(row.GetCell(5)));
            Assert.Equal((Lines[i].Day, Lines[i].Published), (row.GetCell(0).DateCellValue, row.GetCell(5).DateCellValue));
            Assert.Equal((Lines[i].Code, Lines[i].Name), (row.GetCell(1).StringCellValue, row.GetCell(2).StringCellValue));
            Assert.Equal((CellType.Numeric, (double)Lines[i].PerEuro), (row.GetCell(3).CellType, row.GetCell(3).NumericCellValue));
            Assert.Equal(1 / (double)Lines[i].PerEuro, row.GetCell(4).NumericCellValue, 12);
        }

        // As Excel shows them: the date, every digit of the rate, and enough of the reverse one.
        string Shown(int column) => new DataFormatter(CultureInfo.InvariantCulture).FormatCellValue(sheet.GetRow(5).GetCell(column));
        Assert.Equal(("23.11.2026", "0.8437"), (Shown(0), Shown(3)));
        Assert.StartsWith("1.185255", Shown(4));
    }

    [Fact]
    public void It_is_written_whole_or_not_at_all()
    {
        string path = Path.Combine(Path.GetTempPath(), "mic-rates-" + Guid.NewGuid() + ".xlsx");
        try
        {
            RatesWorkbook.Write(path, "Курсове", [], Lines);
            byte[] before = File.ReadAllBytes(path);
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))   // open in Excel
                Assert.Contains("затворете го", Assert.Throws<ConvertException>(() => RatesWorkbook.Write(path, "Други", [], Lines)).Message);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.False(File.Exists(path + ".tmp"));

            var sheet = WorkbookLoader.Load(path).Single();   // also by the reader of the program
            Assert.Equal(("Курсове", 2 + Lines.Length), (sheet.Name, sheet.Rows.Count(r => r.Any(c => c != null))));
        }
        finally
        {
            File.Delete(path);
        }

        Assert.Throws<ConvertException>(() => RatesWorkbook.Build("Курсове", [], []));
    }
}
