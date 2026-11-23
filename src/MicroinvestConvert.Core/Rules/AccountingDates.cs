namespace MicroinvestConvert.Core.Rules;

/// <param name="Date">The posting date (column C).</param>
/// <param name="AfterPeriod">
/// The document is from a month after its tax period: the date cannot be both in the period
/// and not before the document.
/// </param>
public readonly record struct AccountingDate(DateTime Date, bool AfterPeriod);

/// <summary>
/// The rule of Счетоводство Плюс for the accounting date: the first day of the tax period,
/// but never before the document.
/// </summary>
public static class AccountingDates
{
    public static AccountingDate Resolve(DateTime documentDate, DateTime? period)
    {
        if (period is not { } first) return new AccountingDate(documentDate, false);

        if (documentDate <= first) return new AccountingDate(first, false);

        bool sameMonth = documentDate.Year == first.Year && documentDate.Month == first.Month;
        return new AccountingDate(documentDate, !sameMonth);
    }
}

/// <summary>The currency the books are kept in.</summary>
public static class BookCurrency
{
    public const string Euro = "EUR";

    private static readonly DateTime EuroAdoption = new(2026, 1, 1);

    /// <summary>BGN until 31.12.2025, EUR from 01.01.2026.</summary>
    public static string On(DateTime date) => date < EuroAdoption ? "BGN" : Euro;

    public static string Normalise(string? currency) => (currency ?? "").Trim().ToUpperInvariant() switch
    {
        "" => "",
        "EUR" or "EURO" or "ЕВРО" or "€" => Euro,
        "BGN" or "ЛВ" or "ЛВ." or "ЛЕВ" or "ЛЕВА" => "BGN",
        var other => other,
    };
}
