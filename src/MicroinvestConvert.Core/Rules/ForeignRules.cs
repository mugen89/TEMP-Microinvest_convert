using MicroinvestConvert.Core.Exchange;
using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Nomenclature;
using static MicroinvestConvert.Core.Rules.RuleText;

namespace MicroinvestConvert.Core.Rules;

/// <summary>
/// The invoices of the foreign export: the whole amount, deal type 1, no VAT. The VAT is
/// charged by a protocol, which is entered in Делта Pro. See Deal_Type.md.
/// </summary>
internal static class ForeignRules
{
    private const decimal Cent = 0.01m;

    /// <summary>How far an exchange rate may be from the others of the same currency.</summary>
    private const decimal RateTolerance = 0.05m;

    public static List<VatGroup> Classify(OperationDocument doc)
    {
        foreach (var line in doc.Lines)
        {
            line.AutoDealType = DealTypes.Internal;
            line.Why = "чуждестранна фактура – ДДС се начислява с протокол";
        }

        AddIssues(doc);
        return [];
    }

    private static void AddIssues(OperationDocument doc)
    {
        var partner = new PartnerIdentity(doc.Eik, doc.VatNumber);
        if (partner.IsBulgarian && doc.Lines.Any(l => l.DealTypeOverride == null))
            doc.Issues.Add(new Issue(IssueCode.NotForeign,
                $"Документът е в „Чуждестранни“, а номерът на доставчика е български ({(doc.VatNumber.Length > 0 ? doc.VatNumber : doc.Eik)}). " +
                "Проверете номера и вида сделка."));

        foreach (var line in doc.Lines.Where(l => doc.CurrencyOf(l) == Currencies.Euro && Math.Abs(l.Base - l.SellerTotal) > Cent))
            doc.Issues.Add(new Issue(IssueCode.EuroTotalDiffers,
                $"Ред {line.SourceRow}: фактурата е в евро за {Money(line.SellerTotal)}, а се записват {Money(line.Base)}."));
    }

    /// <summary>
    /// The exchange rates the amounts in euro come to, against each other: on the rows of a
    /// document, and over the documents of the file. A document total repeated on every row shows
    /// here. Only where the official rate is not known – otherwise they are compared with it.
    /// </summary>
    public static void CheckRates(OperationBatch batch)
    {
        var lines = batch.Documents
            .SelectMany(d => d.Lines.Where(l => IsCompared(d, l)).Select(l => (Doc: d, Line: l, Rate: l.Gross / l.Euro!.Value)))
            .ToList();

        foreach (var currency in lines.GroupBy(x => x.Doc.CurrencyOf(x.Line)))
        {
            // The documents of a file are a month or two apart; a median needs a few of them.
            var rates = currency.Select(x => x.Rate).OrderBy(r => r).ToList();
            if (rates.Count < 3) continue;
            decimal usual = rates[rates.Count / 2];

            foreach (var x in currency.Where(x => Math.Abs(x.Rate / usual - 1m) > RateTolerance))
                x.Doc.Issues.Add(new Issue(IssueCode.EuroRate,
                    $"Ред {x.Line.SourceRow}: {Money(x.Line.Gross)} {currency.Key} = {Money(x.Line.Euro!.Value)} EUR прави курс {Rate(x.Rate)}, " +
                    $"а обичайният във файла е {Rate(usual)}. Проверете сумата в евро (кол. AI)."));
        }
    }

    private static bool IsCompared(OperationDocument doc, LedgerLine line) =>
        doc.ConversionOf(line) is { Origin: RateOrigin.File, Official: null } && line.Gross != 0m && line.Euro is { } euro && euro != 0m;
}
