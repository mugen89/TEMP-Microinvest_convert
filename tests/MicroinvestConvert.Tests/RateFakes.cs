using System.Globalization;
using System.Net;
using System.Text;
using MicroinvestConvert.Core.Exchange;

namespace MicroinvestConvert.Tests;

/// <summary>Rates that are simply there.</summary>
internal sealed class SomeRates : IExchangeRates
{
    private readonly Dictionary<(string Currency, DateTime Day), ExchangeRate> _rates = [];
    private readonly HashSet<DateTime> _days = [];

    public SomeRates Add(string currency, string day, decimal perEuro, string? published = null)
    {
        _rates[(currency, Day(day))] = new ExchangeRate(currency, Day(day), Day(published ?? day), perEuro);
        _days.Add(Day(day));
        return this;
    }

    public ExchangeRate? Find(string currency, DateTime day) => _rates.GetValueOrDefault((currency, day.Date));
    public bool Knows(DateTime day) => _days.Contains(day.Date);

    public static DateTime Day(string text) => DateTime.ParseExact(text, "dd.MM.yyyy", CultureInfo.InvariantCulture);
}

/// <summary>
/// A source with a table on every working day up to <see cref="Latest"/> that is not a holiday –
/// like the real one it gives the last table before the day that is asked for.
/// </summary>
internal sealed class FakeSource : IRateSource
{
    private readonly Lock _lock = new();

    public string Name => "проба";
    public DateTime Latest { get; set; } = DateTime.Today;
    public HashSet<DateTime> Holidays { get; } = [];
    public List<DateTime> Asked { get; } = [];

    /// <summary>What is thrown instead of an answer; null = it answers.</summary>
    public Func<DateTime, RateException?>? Fails { get; set; }

    /// <summary>Waited for before every answer.</summary>
    public Func<Task>? Before { get; set; }

    public async Task<RateTable> GetAsync(DateTime day, CancellationToken cancel)
    {
        lock (_lock) Asked.Add(day);
        if (Before is { } before) await before().WaitAsync(cancel);
        cancel.ThrowIfCancellationRequested();
        if (Fails?.Invoke(day) is { } failure) throw failure;

        var date = day < Latest ? day : Latest;
        while (RateCalendar.IsWeekend(date) || Holidays.Contains(date)) date = date.AddDays(-1);
        return Table(date);
    }

    /// <summary>The dollar of a day: 1.1 and a digit for every day since 2000.</summary>
    public static decimal Dollar(DateTime date) => 1.1m + (date - new DateTime(2000, 1, 1)).Days / 100000m;

    public static RateTable Table(DateTime date) => new(date, Currencies.Euro,
    [
        new RateRow("DKK", "Датска крона", 1m, 7.46m, 0.134m),
        new RateRow("GBP", "Британска лира", 1m, 0.85m, 1.1765m),
        new RateRow("USD", "Щатски долар", 1m, Dollar(date), Math.Round(1m / Dollar(date), 4)),
    ]);

    public static RateException NoInternet => new(RateFailure.NoInternet, "няма връзка");
    public static RateException SiteDown => new(RateFailure.SiteDown, "не отговаря");
}

/// <summary>Pages like those of the real site, with invented rates.</summary>
internal static class RatePages
{
    private static readonly (string Code, string Name, string Rate, string Reverse, string Units)[] Euro =
    [
        ("AUD", "Австралийски долар", "1.648700", "0.606500", "1"), ("CZK", "Чешка крона", "24.500000", "0.040816", "1"),
        ("DKK", "Датска крона", "7.460000", "0.134048", "1"), ("GBP", "Британска лира", "0.850000", "1.176500", "1"),
        ("IDR", "Индонезийска рупия", "20000.000000", "0.000050", "1"), ("JPY", "Японска йена", "160.000000", "0.006250", "1"),
        ("USD", "Щатски долар", "1.161600", "0.860900", "1"),
    ];

    private static readonly (string Code, string Name, string Rate, string Reverse, string Units)[] Leva =
    [
        ("EUR", "Евро", "1.955830", "0.511292", "1"), ("BRL", "Бразилски реал", "3.000000", "3.333330", "10"),
        ("JPY", "Японска йена", "1.200000", "83.333300", "100"), ("RUB", "Руска рубла", "0.000000", "0.000000", "0"),
        ("USD", "Щатски долар", "1.700000", "0.588235", "1"), ("XAU", "Злато (1 трой унция)", "7000.000000", "0.000000", "1"),
        ("ZAR", "Южноафрикански ранд", "1.000000", "10.000000", "10"),
    ];

    public static string InEuro(string title = "20.11.2026") => Page(title, "Евро (EUR)", Euro);
    public static string InLeva(string title = "30.12.2025") => Page(title, "Лева (BGN)", Leva);

    /// <summary>The columns of the rate and of the reverse rate in each other's place.</summary>
    public static string TurnedRound() => Page("20.11.2026", "Евро (EUR)", Euro.Select(r => r with { Rate = r.Reverse, Reverse = r.Rate }));

    public static string Page(string title, string header, IEnumerable<(string Code, string Name, string Rate, string Reverse, string Units)> rows)
    {
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"bg\"><head><title>Валутни курсове за ").Append(title).Append("</title></head><body><table><tr><td>меню</td></tr></table>")
            .Append("<h1 class=\"page-title\">Валутни курсове за ").Append(title).Append("</h1><div class=\"table-responsive\"><table class=\"table table-hover\">")
            .Append("<thead><tr class=\"text-small\"><th colspan=\"2\">Валута</th><th class=\"text-center\">КОД</th><th>").Append(header)
            .Append("</th><th>Обратен курс</th><th>За единици валута</th><th>Разлика</th></tr></thead>");
        foreach (var r in rows)
            html.Append("<tr><td><img src=\"x.gif\" alt=\"").Append(r.Code).Append("\"/></td><td class=\"text-small\">").Append(r.Name)
                .Append("</td><td class=\"text-center text-bold\">").Append(r.Code).Append("</td><td class=\"text-right\">").Append(r.Rate)
                .Append("</td><td class=\"text-right\">").Append(r.Reverse).Append("</td><td class=\"text-right\">").Append(r.Units)
                .Append("</td><td class=\"text-nowrap\">0.010900 <i class=\"fa fa-angle-up\"></i></td></tr>");
        return html.Append("</table></div></body></html>").ToString();
    }
}

/// <summary>Answers requests without a network.</summary>
internal sealed class FakeSite(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
{
    public List<string> Asked { get; } = [];

    public FakeSite(Func<HttpRequestMessage, HttpResponseMessage> answer) : this((r, _) => Task.FromResult(answer(r)))
    {
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
    {
        Asked.Add(request.RequestUri!.ToString());
        return answer(request, cancel);
    }

    public static HttpResponseMessage Html(string html, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(html, Encoding.UTF8, "text/html") };
}
