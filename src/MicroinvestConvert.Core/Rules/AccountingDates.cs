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
