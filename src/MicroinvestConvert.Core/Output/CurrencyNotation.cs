using System.Globalization;
using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Rules;

namespace MicroinvestConvert.Core.Output;

/// <summary>
/// An amount in another currency, as Model.xls has it: „лв (валута|курс|фиксинг)" – here in euro.
/// The sample multiplies: 2000 (1000|2.00|1.95583) is 1000 × 2.00. So its rates are euro for one
/// unit of the currency, the reverse of the published ones, which are units for one euro.
/// </summary>
internal static class CurrencyNotation
{
    /// <summary>The decimals of the official rates in the sample.</summary>
    private const int Usual = 5;
    private const int Most = 12;

    /// <param name="doc">The document.</param>
    /// <param name="euro">The amount in euro.</param>
    /// <param name="amount">The same in the currency.</param>
    /// <param name="currency">The currency it is written with; empty = euro only.</param>
    /// <param name="lines">The rows of the file it is made of.</param>
    public static string Text(OperationDocument doc, decimal euro, decimal? amount, string currency, IReadOnlyCollection<LedgerLine> lines)
    {
        string text = OperationRows.Amount(euro);
        if (currency.Length == 0 || amount is not { } inCurrency || inCurrency == 0m ||
            doc.Conversions is not [var conversion] || conversion.Currency != currency)
            return text;

        decimal? official = conversion.Official is { } perEuro ? 1m / perEuro : null;
        bool own = lines.Any(l => CurrencyRules.HasOwnEuro(doc, l)) && !IsBy(official, euro, inCurrency, lines.Count);
        decimal? rate = own ? euro / inCurrency
            : conversion.Origin == RateOrigin.File ? official
            : conversion.PerEuro > 0m ? 1m / conversion.PerEuro : null;
        if (rate is not { } course || course <= 0m) return text;   // the file contradicts itself

        // The amounts of a document have one rate, in the same words; one of its own stands apart.
        string courseText = Digits(course, own ? [inCurrency] : AmountsOf(doc));
        string fixingText = official is { } fixing ? Digits(fixing, AmountsOf(doc)) : courseText;
        return $"{text} ({OperationRows.Amount(inCurrency)}|{courseText}|{fixingText})";
    }

    /// <summary>The euro are the currency by that rate, as far as the rounding of the file goes.</summary>
    private static bool IsBy(decimal? rate, decimal euro, decimal inCurrency, int rows) =>
        rate is { } r && Math.Abs(euro - DocumentProcessor.Cents(inCurrency * r)) <= CurrencyRules.FileTolerance * rows;

    private static List<decimal> AmountsOf(OperationDocument doc) =>
        doc.Segments.Select(s => s.InCurrency ?? 0m).Append(doc.TotalInCurrency ?? 0m).ToList();

    /// <summary>
    /// Five decimals – more when they would not give the amounts back to the cent, and for a
    /// currency of small value.
    /// </summary>
    private static string Digits(decimal exact, List<decimal> amounts)
    {
        int decimals = Usual;
        for (decimal value = exact; value < 0.1m && decimals < Most; value *= 10m) decimals++;
        while (decimals < Most && !amounts.TrueForAll(a => DocumentProcessor.Cents(a * To(decimals)) == DocumentProcessor.Cents(a * exact))) decimals++;
        return To(decimals).ToString("0.############", CultureInfo.InvariantCulture);

        decimal To(int digits) => Math.Round(exact, digits, MidpointRounding.AwayFromZero);
    }
}
