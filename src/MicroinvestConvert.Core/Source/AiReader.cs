using System.Text.RegularExpressions;
using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Nomenclature;
using MicroinvestConvert.Core.Rules;
using MicroinvestConvert.Core.Values;
using C = MicroinvestConvert.Core.Source.AiColumns;

namespace MicroinvestConvert.Core.Source;

/// <summary>
/// Reads an AI Accounting export into documents. Many rows may make one document (the
/// analytic export) or one row each (the synthetic one) — the same code serves both.
/// Nothing is interpreted here beyond „which ledger column is the amount in".
/// </summary>
public static partial class AiReader
{
    public static OperationBatch Read(string path) => Read(WorkbookLoader.Load(path), path);

    public static OperationBatch Read(IReadOnlyList<SheetData> sheets, string path)
    {
        var file = AiFile.Detect(sheets);
        var sheet = file.Sheet;

        var batch = new OperationBatch
        {
            Kind = file.Kind,
            SourcePath = path,
            SheetName = sheet.Name,
            LayoutName = file.LayoutName,
            ClientName = ExportDate().Replace(CellValues.Norm(sheet.Cell(0, 0)), ""),
            ClientEik = PartnerIdentity.Parse(CellValues.Norm(sheet.Cell(0, 1))).Eik,
        };

        var byKey = new Dictionary<string, OperationDocument>(StringComparer.Ordinal);
        string previousKey = "", previousIndex = "";

        for (int r = file.FirstDataRow; r < sheet.Rows.Count; r++)
        {
            string index = CellValues.Norm(sheet.Cell(r, C.Index));
            string number = CellValues.Norm(sheet.Cell(r, C.DocNumber));

            if (index.Length == 0 && number.Length == 0)
            {
                // The blank row between two documents; some carry dragged-down texts.
                string content = FirstContent(sheet.Rows[r]);
                if (content.Length > 0)
                    batch.SkippedRows.Add(new SkippedRow(r + 1, $"няма № и номер на документ (има „{content}“)"));
                continue;
            }

            // Rows of one document repeat its header. The number is part of the key, so two
            // files pasted one under the other (the same № twice) stay separate documents.
            string key = string.Join('|', index, CellValues.Norm(sheet.Cell(r, C.DocType)).ToLowerInvariant(),
                number, CellValues.Norm(sheet.Cell(r, C.PartnerId)));
            if (number.Length == 0 && index == previousIndex) key = previousKey;

            if (!byKey.TryGetValue(key, out var doc))
            {
                doc = ReadHeader(file, r, index);
                byKey.Add(key, doc);
                batch.Documents.Add(doc);
            }

            if (file.Kind == OperationKind.Purchase) ReadPurchaseRow(file, r, doc);
            else ReadSaleRow(file, r, doc);

            previousKey = key;
            previousIndex = index;
        }

        if (batch.Documents.Count == 0) throw new ConvertException("Във файла няма документи.");

        foreach (var doc in batch.Documents)
        {
            Complete(file, doc);
            doc.RememberOriginal();
        }

        return batch;
    }

    // ----- document header -----

    private static OperationDocument ReadHeader(AiFile file, int row, string index)
    {
        var sheet = file.Sheet;
        string Text(int column) => CellValues.Norm(sheet.Cell(row, column));

        string rawType = Text(C.DocType);
        int? code = CellValues.Integer(sheet.Cell(row, C.DocCode));
        string partnerId = Text(C.PartnerId);
        var partner = PartnerIdentity.Parse(partnerId);

        bool purchase = file.Kind == OperationKind.Purchase;
        int payment = purchase ? C.Purchase.Payment : file.Sales!.Payment;
        int counter = purchase ? C.Purchase.CounterAccount : file.Sales!.CounterAccount;

        var doc = new OperationDocument
        {
            Kind = file.Kind,
            Index = index,
            SourceDocType = rawType,
            SourceDocCode = code,
            PartnerId = partnerId,
            DocType = DocumentTypes.FromSource(rawType, code),
            Date = CellValues.Date(sheet.Cell(row, C.DocDate)),
            Period = CellValues.Period(sheet.Cell(row, C.TaxPeriod)),
            PartnerName = Text(C.PartnerName),
            Eik = partner.Eik,
            VatNumber = partner.VatNumber,
            Currency = BookCurrency.Normalise(Text(C.Currency)),
            Payment = PaymentMethods.FromText(Text(payment)),
            CounterAccount = Text(counter),
        };

        doc.Number = ReadNumber(sheet.Cell(row, C.DocNumber), doc);

        var byText = DocumentTypes.Find(rawType);
        if (byText != null && code is 1 or 2 or 3 && byText.ShortName != DocumentTypes.FromSource("", code))
            doc.ReadIssues.Add(new Issue(IssueSeverity.Warning, IssueCode.DocumentTypeConflict,
                $"Видът „{rawType}“ и кодът {code} в кол. C не съвпадат – взет е „{byText.ShortName}“."));

        return doc;
    }

    // Excel drops the leading zeros of a number typed as a number: 9679 was 0000009679.
    private static string ReadNumber(object? cell, OperationDocument doc)
    {
        string number = CellValues.Norm(cell);
        if (!CellValues.IsNumeric(cell) || number.Length is 0 or >= 10 || !number.All(char.IsAsciiDigit)) return number;
        if (doc.DocType is not (DocumentTypes.Invoice or DocumentTypes.DebitNote or DocumentTypes.CreditNote)) return number;

        string padded = number.PadLeft(10, '0');
        doc.ReadIssues.Add(new Issue(IssueSeverity.Info, IssueCode.PaddedNumber,
            $"Номерът в клетката е число ({number}) – допълнен е до 10 знака: {padded}."));
        return padded;
    }

    // ----- rows -----

    private static void ReadPurchaseRow(AiFile file, int row, OperationDocument doc)
    {
        var sheet = file.Sheet;
        decimal Amount(int column) => CellValues.NumberOrZero(sheet.Cell(row, column));
        string Text(int column) => CellValues.Norm(sheet.Cell(row, column));

        string treatment = Text(C.Treatment);
        decimal gross = Gross(sheet, row);
        bool noDiary = IsYes(Text(C.Purchase.NoDiary));

        var parts = new List<(LedgerBucket Bucket, decimal Base, decimal Vat)>();
        if (Amount(C.Purchase.NoCredit) is var none && none != 0m)
            parts.Add((LedgerBucket.PurchaseNoCredit, none, 0m));
        if (Amount(C.Purchase.FullBase) is var fb && Amount(C.Purchase.FullVat) is var fv && (fb != 0m || fv != 0m))
            parts.Add((LedgerBucket.PurchaseFullCredit, fb, fv));
        if (Amount(C.Purchase.PartialBase) is var pb && Amount(C.Purchase.PartialVat) is var pv && (pb != 0m || pv != 0m))
            parts.Add((LedgerBucket.PurchasePartialCredit, pb, pv));

        LedgerBucket byTreatment = PurchaseBucketOf(treatment);

        if (byTreatment == LedgerBucket.PurchaseReverseCharge)
        {
            if (parts.Count == 0) parts.Add((LedgerBucket.PurchaseReverseCharge, gross, 0m));
            for (int i = 0; i < parts.Count; i++) parts[i] = parts[i] with { Bucket = LedgerBucket.PurchaseReverseCharge };
        }
        else if (noDiary || (parts.Count == 0 && byTreatment == LedgerBucket.PurchaseNoDiary))
        {
            if (parts.Count > 0)
                AddOnce(doc.ReadIssues, new Issue(IssueSeverity.Warning, IssueCode.NoDiary,
                    "Отбелязан е „без ДДС дневник“, а има суми в колоните за дневника – взето е „без дневник“."));
            parts = [(LedgerBucket.PurchaseNoDiary, gross, 0m)];
        }
        else if (parts.Count == 0)
        {
            parts.Add(gross == 0m
                ? (byTreatment, 0m, 0m)
                : (LedgerBucket.PurchaseUnspecified, Amount(C.SellerBase) != 0m || Amount(C.SellerVat) != 0m
                    ? Amount(C.SellerBase)
                    : gross, Amount(C.SellerVat)));
        }

        AddLines(file, row, doc, parts, gross, "");
    }

    private static void ReadSaleRow(AiFile file, int row, OperationDocument doc)
    {
        var sheet = file.Sheet;
        var layout = file.Sales!;
        decimal Amount(int column) => column < 0 ? 0m : CellValues.NumberOrZero(sheet.Cell(row, column));
        string Text(int column) => CellValues.Norm(sheet.Cell(row, column));

        decimal gross = Gross(sheet, row);
        var parts = new List<(LedgerBucket Bucket, decimal Base, decimal Vat)>();

        void Add(LedgerBucket bucket, decimal @base, decimal vat = 0m)
        {
            if (@base != 0m || vat != 0m) parts.Add((bucket, @base, vat));
        }

        Add(LedgerBucket.Sale20, Amount(C.Sales.Base20), Amount(C.Sales.Vat20));
        Add(LedgerBucket.Sale9, Amount(C.Sales.Base9), Amount(C.Sales.Vat9));
        Add(LedgerBucket.SaleIntraCommunity, Amount(C.Sales.IntraCommunity));
        Add(LedgerBucket.SaleExport, Amount(C.Sales.Export));
        Add(LedgerBucket.SaleArticles140, Amount(layout.Articles140));
        Add(LedgerBucket.SaleUntaxedEu, Amount(layout.UntaxedEu));
        Add(LedgerBucket.SaleUntaxedNonEu, Amount(layout.UntaxedNonEu));
        Add(LedgerBucket.SaleExempt, Amount(layout.Exempt));

        if (parts.Count == 0)
            parts.Add(gross == 0m
                ? (SaleBucketOf(Text(C.Treatment)), 0m, 0m)
                : (LedgerBucket.SaleNoDiary, gross - Amount(C.SellerVat), Amount(C.SellerVat)));

        AddLines(file, row, doc, parts, gross, Text(C.Sales.JournalCell));
    }

    private static void AddLines(AiFile file, int row, OperationDocument doc,
        List<(LedgerBucket Bucket, decimal Base, decimal Vat)> parts, decimal gross, string journalCell)
    {
        var sheet = file.Sheet;
        string Text(int column) => CellValues.Norm(sheet.Cell(row, column));

        if (parts.Count > 1)
            AddOnce(doc.ReadIssues, new Issue(IssueSeverity.Info, IssueCode.MixedRow,
                $"Ред {row + 1} има суми в няколко колони за дневника – разделен е на {parts.Count} части."));

        decimal sellerVat = CellValues.NumberOrZero(sheet.Cell(row, C.SellerVat));
        bool first = true;
        foreach (var (bucket, @base, vat) in parts)
        {
            doc.Lines.Add(new LedgerLine
            {
                SourceRow = row + 1,
                Bucket = bucket,
                Account = Text(C.Account),
                AccountName = Text(C.AccountName),
                Analytic = Text(C.Analytic),
                AnalyticName = Text(C.AnalyticName),
                Article = Text(C.Article),
                Base = @base,
                Vat = vat,
                RateText = Text(C.SellerRate),
                DeclaredRate = VatRates.ParseRate(sheet.Cell(row, C.SellerRate)),
                Treatment = Text(C.Treatment),
                JournalCell = journalCell,
                SellerTotal = first ? gross : 0m,
                SellerVat = parts.Count == 1 ? sellerVat : 0m,
            });
            first = false;
        }
    }

    // ----- what is decided once the document is whole -----

    private static void Complete(AiFile file, OperationDocument doc)
    {
        bool purchase = file.Kind == OperationKind.Purchase;
        int operation = purchase ? C.Purchase.OperationName : file.Sales!.OperationName;
        int cashOperation = purchase ? C.Purchase.CashOperationName : file.Sales!.CashOperationName;
        int cash = purchase ? C.Purchase.CashAccount : file.Sales!.CashAccount;
        int counter = purchase ? C.Purchase.CounterAccount : file.Sales!.CounterAccount;
        int comment = purchase ? C.Purchase.Comment : file.Sales!.Comment;

        // Header texts may be filled on some rows of the document only.
        string FirstText(int column) => doc.Lines
            .Select(l => CellValues.Norm(file.Sheet.Cell(l.SourceRow - 1, column)))
            .FirstOrDefault(t => t.Length > 0) ?? "";

        doc.CashAccount = FirstText(cash);
        if (doc.CounterAccount.Length == 0) doc.CounterAccount = FirstText(counter);
        doc.CashReason = FirstText(cashOperation);
        doc.Note = FirstText(comment);

        // The operation of the largest account names the whole document.
        var biggest = doc.Lines.GroupBy(l => l.AccountKey)
            .OrderByDescending(g => Math.Abs(g.Sum(l => l.Gross)))
            .First();
        doc.Reason = biggest
            .Select(l => CellValues.Norm(file.Sheet.Cell(l.SourceRow - 1, operation)))
            .FirstOrDefault(t => t.Length > 0) ?? FirstText(operation);

        if (doc.Reason.Length == 0)
        {
            doc.Reason = biggest.Select(l => l.AccountName).FirstOrDefault(t => t.Length > 0)
                ?? biggest.Select(l => l.Article).FirstOrDefault(t => t.Length > 0) ?? "";
            if (doc.Reason.Length > 0)
                doc.ReadIssues.Add(new Issue(IssueSeverity.Info, IssueCode.MissingReason,
                    "Във файла няма име на операция – основанието е името на сметката."));
        }
    }

    // ----- helpers -----

    private static decimal Gross(SheetData sheet, int row)
    {
        decimal total = CellValues.NumberOrZero(sheet.Cell(row, C.SellerTotal));
        return total != 0m
            ? total
            : CellValues.NumberOrZero(sheet.Cell(row, C.SellerBase)) + CellValues.NumberOrZero(sheet.Cell(row, C.SellerVat));
    }

    private static bool IsYes(string text) => text.Equals("да", StringComparison.OrdinalIgnoreCase);

    private static LedgerBucket PurchaseBucketOf(string treatment)
    {
        string s = treatment.ToLowerInvariant();
        if (s.Contains("163")) return LedgerBucket.PurchaseReverseCharge;
        if (s.Contains("дневник")) return LedgerBucket.PurchaseNoDiary;
        if (s.Contains("пълен")) return LedgerBucket.PurchaseFullCredit;
        if (s.Contains("частич")) return LedgerBucket.PurchasePartialCredit;
        if (s.Contains("без")) return LedgerBucket.PurchaseNoCredit;
        return LedgerBucket.PurchaseUnspecified;
    }

    private static LedgerBucket SaleBucketOf(string treatment)
    {
        string s = treatment.ToLowerInvariant();
        if (s.Contains("освободен")) return LedgerBucket.SaleExempt;
        if (s.Contains("извън")) return LedgerBucket.SaleUntaxedNonEu;
        if (s.Contains("необл")) return LedgerBucket.SaleUntaxedEu;
        if (s.Contains("трансп")) return LedgerBucket.SaleArticles140;
        if (s.Contains("вод")) return LedgerBucket.SaleIntraCommunity;
        if (s.Contains("20")) return LedgerBucket.Sale20;
        if (s.Contains('9')) return LedgerBucket.Sale9;
        if (s.Contains('0')) return LedgerBucket.SaleExport;
        return LedgerBucket.SaleNoDiary;
    }

    private static string FirstContent(object?[] cells) =>
        cells.Select(CellValues.Norm).FirstOrDefault(t => t.Length > 0) ?? "";

    private static void AddOnce(List<Issue> issues, Issue issue)
    {
        if (!issues.Any(i => i.Code == issue.Code && i.Message == issue.Message)) issues.Add(issue);
    }

    // „Фирма ЕООД 12.08.2026" or „Фирма ООД 01.07.2025 - 31.07.2025"
    [GeneratedRegex(@"\s*\d{1,2}\.\d{1,2}\.\d{4}(\s*-\s*\d{1,2}\.\d{1,2}\.\d{4})?\s*$")]
    private static partial Regex ExportDate();
}
