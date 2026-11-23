using System.Globalization;
using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Nomenclature;
using static MicroinvestConvert.Core.Rules.RuleText;

namespace MicroinvestConvert.Core.Rules;

/// <summary>
/// Column N of a sale. The file has one column per case of дневник продажби, so the column
/// decides; the VAT of 20% and 9% is only checked. See Deal_Type.md.
/// </summary>
internal static class SaleRules
{
    public static int DealTypeOf(LedgerBucket bucket) => bucket switch
    {
        LedgerBucket.Sale20 => DealTypes.Sale20,
        LedgerBucket.Sale9 => DealTypes.Sale9,
        LedgerBucket.SaleIntraCommunity => DealTypes.SaleIntraCommunitySupply,
        LedgerBucket.SaleExport => DealTypes.Sale0,
        LedgerBucket.SaleArticles140 => DealTypes.SaleArticles140,
        LedgerBucket.SaleUntaxedEu => DealTypes.SaleServicesArticle21,
        LedgerBucket.SaleUntaxedNonEu => DealTypes.SaleDistance,
        LedgerBucket.SaleExempt => DealTypes.SaleExempt,
        _ => DealTypes.Internal,
    };

    public static List<VatGroup> Classify(OperationDocument doc, VatRates rates)
    {
        foreach (var line in doc.Lines)
        {
            line.AutoDealType = DealTypeOf(line.Bucket);
            line.Why = line.Bucket == LedgerBucket.SaleNoDiary
                ? "няма стойност в колоните за дневника"
                : "кол. " + LedgerBuckets.Title(line.Bucket);
        }

        var groups = new List<VatGroup>();
        CheckVat(doc, LedgerBucket.Sale20, 20m, rates, groups);
        CheckVat(doc, LedgerBucket.Sale9, 9m, rates, groups);
        AddIssues(doc, groups);
        return groups;
    }

    private static void CheckVat(OperationDocument doc, LedgerBucket bucket, decimal rate, VatRates rates, List<VatGroup> groups)
    {
        var lines = doc.Lines.Where(l => l.Bucket == bucket).ToList();
        if (lines.Count == 0) return;

        decimal @base = lines.Sum(l => l.Base), vat = lines.Sum(l => l.Vat);
        var match = rates.Match(@base, vat, rate, rates.DocumentTolerance, onlyDeclared: true);
        groups.Add(new VatGroup
        {
            Document = doc,
            Bucket = bucket,
            Lines = lines,
            Status = match.Status,
            Base = @base,
            Vat = vat,
            DeclaredRate = rate,
            Rate = match.Rate,
            Expected = match.Expected,
        });
    }

    private static void AddIssues(OperationDocument doc, List<VatGroup> groups)
    {
        foreach (var group in groups)
        {
            if (group.Lines.All(l => l.DealTypeOverride != null)) continue;

            switch (group.Status)
            {
                case VatStatus.Unresolved:
                    doc.Issues.Add(new Issue(IssueCode.VatMismatch,
                        $"ДДС {Money(group.Vat)} не е {Percent(group.DeclaredRate)} от {Money(group.Base)} " +
                        $"(очаквано {Money(group.Expected)}, разлика {Money(group.Difference)}). Проверете."));
                    break;

                case VatStatus.Zero:
                    doc.Issues.Add(new Issue(IssueCode.VatZeroButDeclared,
                        $"Основа {Money(group.Base)} е в колоната за {Percent(group.DeclaredRate)}, но няма ДДС. " +
                        "Ако е доставка по чл. 163а, тя изисква и колони Q и R (следваща версия)."));
                    break;

                case VatStatus.WithinTolerance:
                    doc.Issues.Add(new Issue(IssueCode.VatWithinTolerance,
                        $"ДДС {Money(group.Vat)} се различава с {Money(group.Difference)} от {Percent(group.DeclaredRate)} върху " +
                        $"{Money(group.Base)} – в допустимата разлика."));
                    break;
            }
        }

        if (doc.Lines.Any(l => l.Bucket == LedgerBucket.SaleNoDiary && l.DealTypeOverride == null))
            doc.Issues.Add(new Issue(IssueCode.OutsideLedger,
                "Има сума без стойност в колоните за дневника – вид сделка 1, не влиза в дневника. " +
                "Ако трябва да влезе, изберете вид сделка."));

        // „Клетки в дневник продажби" of the file against the column the amount is in.
        foreach (var line in doc.Lines)
        {
            int column = LedgerBuckets.SalesLedgerColumn(line.Bucket);
            if (column == 0 || line.JournalCell.Length == 0 || line.DealTypeOverride != null) continue;

            var cells = line.JournalCell.Split(',', ';', ' ')
                .Select(t => int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 0)
                .Where(n => n > 0).ToList();
            if (cells.Count == 0 || cells.Contains(column)) continue;

            doc.Issues.Add(new Issue(IssueCode.JournalCellMismatch,
                $"Файлът сочи клетка {line.JournalCell} в дневника (кол. AA), а сумата е в колоната за к. {column}. " +
                $"Взет е вид сделка {line.AutoDealType}."));
            break;
        }

        // VIES needs the VAT number of another member state.
        int[] needVies = [DealTypes.SaleIntraCommunitySupply, DealTypes.SaleServicesArticle21, DealTypes.SaleTriangular];
        var used = doc.Lines.Select(l => l.DealType).Where(needVies.Contains).Distinct().ToList();
        if (used.Count > 0 && !new PartnerIdentity(doc.Eik, doc.VatNumber).IsOtherEuMember)
            doc.Issues.Add(new Issue(IssueCode.ViesPartner,
                $"Вид сделка {string.Join(", ", used)} влиза във VIES, а контрагентът няма ДДС номер от друга държава членка" +
                (doc.VatNumber.Length > 0 ? $" ({doc.VatNumber})." : doc.PartnerId.Length > 0 ? $" ({doc.PartnerId})." : ".")));
    }
}

/// <summary>Amounts and rates inside messages.</summary>
internal static class RuleText
{
    private static readonly CultureInfo Bulgarian = CultureInfo.GetCultureInfo("bg-BG");

    public static string Money(decimal value) => value.ToString("0.00##", Bulgarian);

    public static string Percent(decimal? rate) => rate is { } r ? r.ToString("0.##", Bulgarian) + "%" : "неизвестна ставка";
}
