using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Source;
using static MicroinvestConvert.Tests.TestSheet;

namespace MicroinvestConvert.Tests;

public class AiFileTests
{
    private static AiFile Detect(TestSheet sheet, string? name = null) => AiFile.Detect([sheet.ToSheet(name)]);

    [Fact]
    public void Purchases_are_recognised() =>
        Assert.Equal(OperationKind.Purchase, Detect(new TestSheet(Layout.Purchases).Purchase(1d, "0000000001", 100, 20)).Kind);

    [Fact]
    public void Sales_are_recognised_in_both_layouts()
    {
        var current = Detect(new TestSheet(Layout.Sales).Sale(1d, "0000000001", 100, 20));
        var old = Detect(new TestSheet(Layout.SalesBeforeJuly2026).Sale(1d, "0000000001", 100, 20));
        Assert.Equal(("Експорт Продажби", OperationKind.Sale), (current.LayoutName, current.Kind));
        Assert.Equal(("Експорт Продажби (колони до юли 2026)", OperationKind.Sale), (old.LayoutName, old.Kind));
    }

    [Fact]
    public void Without_headers_the_payment_column_tells_the_sales_layout()
    {
        var current = Detect(new TestSheet(Layout.Sales, headers: false).Sale(1d, "0000000001", 100, 20));
        var old = Detect(new TestSheet(Layout.SalesBeforeJuly2026, headers: false).Sale(1d, "0000000001", 100, 20));
        Assert.Equal("Експорт Продажби", current.LayoutName);
        Assert.Equal("Експорт Продажби (колони до юли 2026)", old.LayoutName);
    }

    [Fact]
    public void Without_the_number_row_the_header_is_enough() =>
        Assert.Equal(4, Detect(new TestSheet(Layout.Purchases, indexRow: false).Purchase(1d, "0000000001", 100, 20)).FirstDataRow);

    [Fact]
    public void A_renamed_sheet_is_still_recognised() =>
        Assert.Equal(OperationKind.Sale, Detect(new TestSheet(Layout.Sales).Sale(1d, "1", 100, 20), "Sheet1").Kind);

    [Fact]
    public void The_right_sheet_of_several_is_found()
    {
        var other = new SheetData("Бележки", [["нещо", "друго"]]);
        var file = AiFile.Detect([other, new TestSheet(Layout.Purchases).Purchase(1d, "1", 100, 20).ToSheet()]);
        Assert.Equal("Експорт Покупки", file.Sheet.Name);
    }

    [Fact]
    public void The_foreign_export_is_recognised_by_its_two_columns()
    {
        var file = Detect(new TestSheet(Layout.Foreign).Invoice(1d, "INV-1", 10, "EUR", 10));
        Assert.Equal((OperationKind.Foreign, "Експорт Чуждестранни"), (file.Kind, file.LayoutName));
        Assert.Equal(OperationKind.Foreign, Detect(new TestSheet(Layout.Foreign).Invoice(1d, "INV-1", 10, "EUR", 10), "Sheet1").Kind);
    }

    [Fact]
    public void A_foreign_sheet_with_other_columns_is_refused_and_not_read_shifted()
    {
        var ex = Assert.Throws<ConvertException>(() => Detect(new TestSheet(Layout.Purchases).Purchase(1d, "1", 100, 20), "Експорт Чуждестранни"));
        Assert.Contains("колоните му не са очакваните", ex.Message);
    }

    [Fact]
    public void An_export_of_protocols_is_refused_with_a_reason()
    {
        var ex = Assert.Throws<ConvertException>(() => Detect(new TestSheet(Layout.Purchases).Purchase(1d, "1", 100, 20), "Протоколи"));
        Assert.Contains("протоколи", ex.Message);
    }

    [Fact]
    public void Another_workbook_is_refused()
    {
        var ex = Assert.Throws<ConvertException>(() => AiFile.Detect([new SheetData("Sheet1", [["a", "b"], ["c"]])]));
        Assert.Contains("не прилича на експорт", ex.Message);
    }
}

public class AiReaderTests
{
    [Fact]
    public void The_client_comes_from_the_first_row()
    {
        var batch = new TestSheet(Layout.Purchases).Purchase(1d, "0000000001", 100, 20).Read();
        Assert.Equal(("Тестова Фирма ЕООД", "987654326"), (batch.ClientName, batch.ClientEik));
    }

    [Fact]
    public void Rows_of_one_document_are_grouped_and_blank_rows_skipped()
    {
        var batch = new TestSheet(Layout.Purchases)
            .Purchase(1d, "0000000001", 100, 20)
            .Purchase(1d, "0000000001", 50, 10, ("K", "602"))
            .Blank()
            .Raw(("AL", "дръпнат надолу текст"))
            .Purchase(2d, "0000000002", 10, 2)
            .Read();

        Assert.Equal([2, 1], batch.Documents.Select(d => d.Lines.Count));
        Assert.Contains("дръпнат надолу текст", Assert.Single(batch.SkippedRows).Reason);
    }

    [Fact]
    public void The_same_index_with_another_number_is_another_document()
    {
        // two exports pasted one under the other
        var batch = new TestSheet(Layout.Purchases)
            .Purchase(1d, "0000000001", 100, 20)
            .Purchase(1d, "0000000009", 100, 20)
            .Read();
        Assert.Equal(2, batch.Documents.Count);
    }

    [Fact]
    public void A_row_without_a_number_joins_the_document_above()
    {
        var batch = new TestSheet(Layout.Purchases)
            .Purchase(1d, "0000000001", 100, 20)
            .Purchase(1d, "", 50, 10)
            .Read();
        Assert.Equal(2, Assert.Single(batch.Documents).Lines.Count);
    }

    [Fact]
    public void Header_fields_are_read()
    {
        var doc = new TestSheet(Layout.Purchases)
            .Purchase(1d, "0000000001", 100, 20, ("AH", "В брой"), ("AJ", "501"), ("AN", "Плащане"), ("AO", "бележка"))
            .Read().Documents[0];

        Assert.Equal("Ф-ра", doc.DocType);
        Assert.Equal(new DateTime(2026, 6, 10), doc.Date);
        Assert.Equal(new DateTime(2026, 6, 1), doc.Period);
        Assert.Equal((SupplierEik, SupplierVat), (doc.Eik, doc.VatNumber));
        Assert.Equal(("401", "501"), (doc.CounterAccount, doc.CashAccount));
        Assert.Equal(("Разходи за материали", "Плащане", "бележка"), (doc.Reason, doc.CashReason, doc.Note));
        Assert.True(doc.IsCash);
    }

    [Fact]
    public void A_number_stored_as_a_number_gets_its_zeros_back()
    {
        var sheet = new TestSheet(Layout.Purchases).Purchase(1d, "x", 100, 20, ("D", 1234d));
        var doc = sheet.Read().Documents[0];
        Assert.Equal("0000001234", doc.Number);
        Assert.Contains(doc.ReadIssues, i => i.Code == IssueCode.PaddedNumber);
    }

    [Fact]
    public void Text_numbers_are_never_touched() =>
        Assert.Equal("1234", new TestSheet(Layout.Purchases).Purchase(1d, "1234", 100, 20).Read().Documents[0].Number);

    [Fact]
    public void The_operation_of_the_largest_account_names_the_document()
    {
        var doc = new TestSheet(Layout.Purchases)
            .Purchase(1d, "1", 10, 2, ("K", "602"), ("AL", "Услуги"))
            .Purchase(1d, "1", 100, 20, ("K", "601"), ("AL", "Материали"))
            .Read().Documents[0];
        Assert.Equal("Материали", doc.Reason);
    }

    [Fact]
    public void Without_an_operation_the_account_names_the_document()
    {
        var doc = new TestSheet(Layout.Purchases).Purchase(1d, "1", 10, 2, ("AL", "-")).Read().Documents[0];
        Assert.Equal("Разходи за материали", doc.Reason);
        Assert.Contains(doc.ReadIssues, i => i.Code == IssueCode.ReasonFromAccount);
    }

    [Fact]
    public void Each_ledger_column_of_a_purchase_has_its_bucket()
    {
        static LedgerLine Line(params (string, object?)[] cells) =>
            new TestSheet(Layout.Purchases).Purchase(1d, "1", 100, 20, cells).Read().Documents[0].Lines.Single();

        (string, object?)[] none = [("AC", "-"), ("AD", "-")];

        Assert.Equal(LedgerBucket.PurchaseFullCredit, Line().Bucket);

        var noCredit = Line([.. none, ("AB", 120d), ("Z", "без Дан. Кредит")]);
        Assert.Equal((LedgerBucket.PurchaseNoCredit, 120m, 0m), (noCredit.Bucket, noCredit.Base, noCredit.Vat));

        var partial = Line([.. none, ("AE", 100d), ("AF", 20d), ("Z", "частичен Дан. Кредит")]);
        Assert.Equal((LedgerBucket.PurchasePartialCredit, 100m, 20m), (partial.Bucket, partial.Base, partial.Vat));

        var noDiary = Line([.. none, ("AG", "ДА"), ("Z", "без Дневник Покупки")]);
        Assert.Equal((LedgerBucket.PurchaseNoDiary, 120m, 0m), (noDiary.Bucket, noDiary.Base, noDiary.Vat));

        var reverse = Line([.. none, ("Z", "Обратно начисляване 163а - 01")]);
        Assert.Equal(LedgerBucket.PurchaseReverseCharge, reverse.Bucket);

        var nothing = Line([.. none, ("Z", "")]);
        Assert.Equal((LedgerBucket.PurchaseUnspecified, 100m, 20m), (nothing.Bucket, nothing.Base, nothing.Vat));
    }

    [Fact]
    public void A_row_with_two_ledger_columns_becomes_two_lines()
    {
        var doc = new TestSheet(Layout.Purchases)
            .Purchase(1d, "1", 100, 20, ("AB", 60d), ("AC", 50d), ("AD", 10d))
            .Read().Documents[0];

        Assert.Equal([LedgerBucket.PurchaseNoCredit, LedgerBucket.PurchaseFullCredit], doc.Lines.Select(l => l.Bucket));
        Assert.Equal(120m, doc.Lines.Sum(l => l.SellerTotal));
        Assert.Contains(doc.ReadIssues, i => i.Code == IssueCode.MixedRow);
    }

    [Fact]
    public void Each_ledger_column_of_a_sale_has_its_bucket()
    {
        var columns = new Dictionary<string, LedgerBucket>
        {
            ["AB"] = LedgerBucket.Sale20, ["AD"] = LedgerBucket.Sale9, ["AF"] = LedgerBucket.SaleIntraCommunity,
            ["AG"] = LedgerBucket.SaleExport, ["AH"] = LedgerBucket.SaleArticles140, ["AI"] = LedgerBucket.SaleUntaxedEu,
            ["AJ"] = LedgerBucket.SaleUntaxedNonEu, ["AK"] = LedgerBucket.SaleExempt,
        };
        foreach (var (column, bucket) in columns)
        {
            var line = new TestSheet(Layout.Sales).Sale(1d, "1", 100, 0, (column, 100d)).Read().Documents[0].Lines.Single();
            Assert.Equal((bucket, 100m), (line.Bucket, line.Base));
        }
    }

    [Fact]
    public void The_old_sales_layout_is_read_by_its_own_columns()
    {
        var doc = new TestSheet(Layout.SalesBeforeJuly2026)
            .Sale(1d, "1", 100, 0, ("AH", 100d), ("AI", "В брой"), ("AK", "501"), ("AO", "Получено плащане"))
            .Read().Documents[0];

        Assert.Equal(LedgerBucket.SaleExempt, doc.Lines.Single().Bucket);
        Assert.Equal(("411", "501", "Получено плащане"), (doc.CounterAccount, doc.CashAccount, doc.CashReason));
        Assert.True(doc.IsCash);
    }

    [Fact]
    public void A_sale_without_ledger_values_stays_outside_the_ledger()
    {
        var line = new TestSheet(Layout.Sales).Sale(1d, "1", 100, 0, ("Z", "Освободена")).Read().Documents[0].Lines.Single();
        Assert.Equal((LedgerBucket.SaleNoDiary, 100m), (line.Bucket, line.Base));
    }

    [Fact]
    public void A_zero_row_takes_its_bucket_from_the_text()
    {
        var sale = new TestSheet(Layout.Sales).Sale(1d, "1", 0, 0, ("Z", "20%")).Read().Documents[0].Lines.Single();
        var purchase = new TestSheet(Layout.Purchases).Purchase(1d, "1", 0, 0).Read().Documents[0].Lines.Single();
        Assert.Equal(LedgerBucket.Sale20, sale.Bucket);
        Assert.Equal(LedgerBucket.PurchaseFullCredit, purchase.Bucket);
    }

    [Fact]
    public void A_workbook_without_documents_is_refused() =>
        Assert.Throws<ConvertException>(() => new TestSheet(Layout.Purchases).Blank().Read());

    [Fact]
    public void A_file_that_is_not_Excel_is_refused()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "това не е Excel");
            Assert.Throws<ConvertException>(() => WorkbookLoader.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_missing_file_is_refused() =>
        Assert.Throws<ConvertException>(() => WorkbookLoader.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xlsx")));
}
