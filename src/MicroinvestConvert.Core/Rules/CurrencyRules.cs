using MicroinvestConvert.Core.Exchange;
using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Nomenclature;
using MicroinvestConvert.Core.Settings;
using static MicroinvestConvert.Core.Rules.RuleText;

namespace MicroinvestConvert.Core.Rules;

/// <summary>
/// Amounts in other currencies become euro: divided by the units of the currency for one euro on
/// the date of the document, or on the day the user chose. When the file gives the euro itself
/// (the foreign export), that is what is written until another day or rate is chosen.
/// </summary>
internal static class CurrencyRules
{
    /// <summary>How far an amount of the file may be from the official rate – its rounding.</summary>
    internal const decimal FileTolerance = 0.02m;

    /// <summary>How far a typed rate may be from the official one, as a part of it.</summary>
    private const decimal TypedTolerance = 0.05m;

    public static void Resolve(OperationDocument doc, IExchangeRates rates)
    {
        doc.UnknownRateDay = null;
        var conversions = new List<Conversion>();
        foreach (string currency in doc.ForeignCurrencies)
        {
            if (Find(doc, currency, rates) is { } conversion) conversions.Add(conversion);
            else doc.Issues.Add(new Issue(IssueCode.MissingRate, WhatIsMissing(doc, currency, rates)));

            // Also when the file has the euro: the rate is needed to compare with.
            if (currency != Currencies.Lev && Currencies.IsCode(currency) && doc.RateDate is { } day && !rates.Knows(day))
                doc.UnknownRateDay = day.Date;
        }

        doc.Conversions = conversions;
        foreach (var conversion in conversions) AddIssues(doc, conversion);
    }

    private static Conversion? Find(OperationDocument doc, string currency, IExchangeRates rates)
    {
        var official = doc.RateDate is { } day ? rates.Find(currency, day) : null;
        decimal? officialRate = currency == Currencies.Lev ? Currencies.LevaPerEuro : official?.PerEuro;

        if (doc.RateOverride is { } typed)
            return doc.ForeignCurrencies.Count == 1
                ? new Conversion(currency, RateOrigin.ByHand, typed, doc.RateDate, null, officialRate)
                : null;   // one rate for two currencies

        var lines = RowsIn(doc, currency);
        if (doc.RateDateOverride == null && lines.TrueForAll(l => l.Euro != null))
        {
            // The rate is only to be shown: the official one, when the amounts are by it.
            decimal euro = lines.Sum(l => l.Euro!.Value);
            decimal shown = officialRate is { } o && lines.TrueForAll(l => IsByRate(l, o)) ? o
                : euro != 0m ? Math.Round(lines.Sum(l => l.Gross) / euro, 4) : 0m;
            return new Conversion(currency, RateOrigin.File, shown, doc.RateDate, official?.Published, officialRate);
        }

        if (currency == Currencies.Lev) return new Conversion(currency, RateOrigin.Fixed, Currencies.LevaPerEuro, null, null, officialRate);
        return official == null ? null : new Conversion(currency, RateOrigin.Official, official.PerEuro, official.Day, official.Published, officialRate);
    }

    private static List<LedgerLine> RowsIn(OperationDocument doc, string currency) =>
        doc.Lines.Where(l => doc.CurrencyOf(l) == currency).ToList();

    private static string WhatIsMissing(OperationDocument doc, string currency, IExchangeRates rates)
    {
        if (doc.RateOverride != null)
            return $"Документът е в няколко валути ({string.Join(", ", doc.ForeignCurrencies)}), а е въведен един курс. Изчистете го.";
        if (!Currencies.IsCode(currency))
            return $"„{currency}“ не е код на валута. Поправете го, например USD.";
        if (doc.RateDate is not { } day)
            return $"Сумите са в {currency}, а документът няма дата, за която да се вземе курс.";
        if (rates.Knows(day))
            return $"За {day:dd.MM.yyyy} няма официален курс на {currency}. Въведете курс или поправете валутата.";
        return day.Date < DateTime.Today
            ? $"Няма курс на {currency} за {day:dd.MM.yyyy}. Изтеглете го от „Валутни курсове“, изберете друга дата на курса или въведете курс."
            : $"Курсът на {currency} за {day:dd.MM.yyyy} още не е публикуван. Изберете друга дата на курса или въведете курс.";
    }

    // ----- amounts -----

    /// <summary>
    /// Amounts of some rows together, in euro. Added up in their currency and divided once, so that
    /// what comes to zero there is zero here. Unchanged while there is no conversion – then the
    /// document waits.
    /// </summary>
    public static (decimal Base, decimal Vat) ToEuro(OperationDocument doc, IReadOnlyCollection<LedgerLine> lines,
        Func<LedgerLine, (decimal Base, decimal Vat)> amounts)
    {
        decimal allBase = 0m, allVat = 0m;
        foreach (var group in lines.GroupBy(l => (Conversion: doc.ConversionOf(l), OwnEuro: HasOwnEuro(doc, l))))
        {
            if (group.Key.OwnEuro)
            {
                // The euro of the file, or typed over it, are for the whole row.
                foreach (var line in group)
                {
                    var (@base, vat) = amounts(line);
                    decimal euro = line.Euro!.Value, vatPart = @base + vat == 0m ? 0m : euro * vat / (@base + vat);
                    allBase += euro - vatPart;
                    allVat += vatPart;
                }

                continue;
            }

            decimal sumBase = group.Sum(l => amounts(l).Base), sumVat = group.Sum(l => amounts(l).Vat);
            decimal perEuro = group.Key.Conversion?.PerEuro ?? 1m;
            if (perEuro <= 0m) continue;
            allBase += sumBase / perEuro;
            allVat += sumVat / perEuro;
        }

        return (allBase, allVat);
    }

    /// <summary>The euro of the row are from the file, or typed over it – not by the rate of the document.</summary>
    internal static bool HasOwnEuro(OperationDocument doc, LedgerLine line) =>
        line.Euro != null && doc.ConversionOf(line) is { } conversion && (conversion.Origin == RateOrigin.File || line.IsEdited(LineField.Euro));

    /// <summary>The whole amount of some rows in euro, without the noise of the division.</summary>
    public static decimal GrossInEuro(OperationDocument doc, IReadOnlyCollection<LedgerLine> lines)
    {
        var (@base, vat) = ToEuro(doc, lines, l => (l.Base, l.Vat));
        return Math.Round(@base + vat, 8);
    }

    // ----- what the user is told -----

    private static void AddIssues(OperationDocument doc, Conversion c)
    {
        var lines = RowsIn(doc, c.Currency);
        decimal total = lines.Sum(l => l.Gross), euro = GrossInEuro(doc, lines);
        bool typed = lines.TrueForAll(l => l.Euro != null && l.IsEdited(LineField.Euro));
        doc.Issues.Add(new Issue(IssueCode.ConvertedToEuro,
            $"{Money(total)} {c.Currency} = {Money(DocumentProcessor.Cents(euro))} EUR" +
            (typed ? ", въведена сума." : (c.PerEuro > 0m ? $" (1 EUR = {Rate(c.PerEuro)} {c.Currency})" : "") + ", " + HowText(c) + ".")));
        if (typed) return;

        switch (c.Origin)
        {
            case RateOrigin.File when c.Official is { } official:
                CompareWithFile(doc, c, lines, official);
                break;

            case RateOrigin.ByHand when c.Official is { } official && Math.Abs(c.PerEuro / official - 1m) > TypedTolerance:
                doc.Issues.Add(new Issue(IssueCode.RateDiffers,
                    $"Въведеният курс е 1 EUR = {Rate(c.PerEuro)} {c.Currency}, а официалният{OnDay(c)} е {Rate(official)}."));
                break;

            case RateOrigin.Official when doc.RateDateOverride == null && DocumentTypes.IsCreditNote(doc.DocType):
                doc.Issues.Add(new Issue(IssueCode.CreditNoteRate,
                    $"Кредитното известие е по курса за своята дата ({c.Day:dd.MM.yyyy}). Ако трябва курсът на фактурата, сменете „Дата на курса“."));
                break;
        }
    }

    private static string HowText(Conversion c) => c.Origin switch
    {
        RateOrigin.File => "от файла (кол. AI)" + (c.Official == c.PerEuro ? $", по курса{OnDay(c)}" : ""),
        RateOrigin.ByHand => "по въведен курс",
        RateOrigin.Fixed => "по фиксирания курс",
        _ => $"по курса за {c.Day:dd.MM.yyyy}" + (c.Published != c.Day ? $" – от {c.Published:dd.MM.yyyy}" : ""),
    };

    private static string OnDay(Conversion c) => c.Day is { } day ? $" за {day:dd.MM.yyyy}" : "";

    private static void CompareWithFile(OperationDocument doc, Conversion c, List<LedgerLine> lines, decimal official)
    {
        var off = lines.Where(l => !l.IsEdited(LineField.Euro) && !IsByRate(l, official)).ToList();
        if (off.Count == 0) return;

        var first = off[0];
        doc.Issues.Add(new Issue(IssueCode.RateDiffers,
            $"Ред {first.SourceRow}: по официалния курс{OnDay(c)} (1 EUR = {Rate(official)} {c.Currency}) {Money(first.Gross)} {c.Currency} " +
            $"са {Money(DocumentProcessor.Cents(first.Gross / official))} EUR, а във файла са {Money(first.Euro!.Value)}" +
            (off.Count > 1 ? $" – и още {off.Count - 1} реда." : ".")));
    }

    private static bool IsByRate(LedgerLine line, decimal rate) =>
        Math.Abs(line.Euro!.Value - DocumentProcessor.Cents(line.Gross / rate)) <= FileTolerance;

    // ----- with their currency in column A -----

    /// <summary>
    /// The currency the amounts on an account are written with, beside the euro; empty = euro only.
    /// The account of the partner takes the currency of the document, any other one only when it is
    /// marked with it. An account is kept in one currency, so a document in several has none, and
    /// the lev, whose rate is fixed, is only another way to say euro.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="mark">What the account is marked with; the euro = an ordinary account, empty = nothing.</param>
    /// <param name="isPartner">It is the account of the supplier or of the customer.</param>
    /// <param name="options">The settings in force.</param>
    public static string WrittenWith(OperationDocument doc, string mark, bool isPartner, ConverterOptions options)
    {
        string currency = doc.OnlyCurrency;
        bool with = mark.Length > 0
            ? mark == currency
            : isPartner && currency != Currencies.Lev && options.General.CurrencyAmounts == CurrencyAmounts.WithCurrency;
        return with ? currency : "";
    }

    /// <summary>What has to be written in euro only, although one would expect its currency too.</summary>
    public static void CheckAccounts(OperationDocument doc, ConverterOptions options, IEnumerable<(string Account, string Mark)> accounts)
    {
        var own = doc.Lines.Select(doc.CurrencyOf).Distinct().ToList();
        if (own.Count > 1 && own.Exists(c => c is not (Currencies.Euro or Currencies.Lev)) && options.General.CurrencyAmounts == CurrencyAmounts.WithCurrency)
            doc.Issues.Add(new Issue(IssueCode.AccountCurrency,
                $"Документът е в няколко валути ({string.Join(", ", own)}), а сметка се води само в една – сумите се записват само в евро."));

        foreach (var (account, mark) in accounts.Where(a => a.Mark.Length > 0 && a.Mark != Currencies.Euro).Distinct())
        {
            if (own.TrueForAll(c => c == mark)) continue;
            doc.Issues.Add(new Issue(IssueCode.AccountCurrency,
                $"Сметка {account} е във валута {mark}, а документът е в {string.Join(", ", own)} – сумата по нея се записва само в евро."));
        }
    }
}
