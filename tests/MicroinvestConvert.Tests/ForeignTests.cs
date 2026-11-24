using MicroinvestConvert.Core.Checks;
using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Output;
using MicroinvestConvert.Core.Rules;
using MicroinvestConvert.Core.Settings;
using static MicroinvestConvert.Tests.TestSheet;

namespace MicroinvestConvert.Tests;

public class ForeignTests
{
    private static TestSheet Sheet => new(Layout.Foreign);

    private static string[] Cells(TestSheet sheet, int row = 0)
    {
        var batch = sheet.Process();
        return OperationRows.Build(batch, ConverterOptions.Default)[row].Cells;
    }

    [Fact]
    public void An_invoice_is_one_row_in_euro_with_deal_type_1_and_no_protocol()
    {
        string[] cells = Cells(Sheet.Invoice(1d, "INV-0001", 100, "USD", 86.27m));
        Assert.Equal(("86.27 (100|0.8627|0.8627)", "602/1-4011", "1", ""), (cells[0], cells[1], cells[OperationsLayout.DealType], cells[OperationsLayout.Vies]));
        Assert.Equal(("2026-09-10", "Example Software Inc.", "INV-0001", "Ф-ра", "Разход за външни услуги"), (cells[2], cells[3], cells[7], cells[8], cells[10]));
        Assert.Equal("2026-09-10", cells[OperationsLayout.ExportMonth]);
        Assert.All(cells[OperationsLayout.FirstProtocolColumn..], c => Assert.Equal("", c));
    }

    [Fact]
    public void Every_column_is_read_one_or_two_to_the_right()
    {
        var doc = Sheet.Invoice(1d, "INV-1", 10, "EUR", 10, ("AQ", "бележка"), ("AJ", "В брой"), ("AL", "5011"), ("AP", "Плащане")).Read().Documents[0];
        var line = Assert.Single(doc.Lines);
        Assert.Equal(("602", "1", "Софтуер", "Разходи за външни услуги"), (line.Account, line.Analytic, line.AnalyticName, line.AccountName));
        Assert.Equal(("4011", "5011", "Плащане", "бележка", "Разход за външни услуги"), (doc.CounterAccount, doc.CashAccount, doc.CashReason, doc.Note, doc.Reason));
        Assert.Equal((PaymentMethod.Cash, "Месечен абонамент", "Необлагаема"), (doc.Payment, line.Article, line.RateText));
        Assert.Equal((LedgerBucket.Foreign, 10m, 0m), (line.Bucket, line.Base, line.Vat));
        Assert.Equal(("EUR", null), (line.Currency, line.Euro));
    }

    [Fact]
    public void The_amount_of_the_invoice_and_the_euro_of_the_file_are_both_read()
    {
        var doc = Sheet.Invoice(1d, "INV-1", 100, "usd", 80m).Read().Documents[0];
        Assert.Equal(("USD", "USD", 100m, 80m), (doc.Currency, doc.Lines[0].Currency, doc.Lines[0].Base, doc.Lines[0].Euro));
    }

    [Theory]
    [InlineData("-", "999999999999999Examp", "999999999999999", "")]
    [InlineData("-", "IE1234567AB", "", "IE1234567AB")]
    [InlineData("12-3456789", "-", "123456789", "")]
    [InlineData("HRB 12345", "DE123456789", "", "DE123456789")]
    [InlineData("123456", "ATU12345678", "123456", "ATU12345678")]
    public void The_supplier_has_its_number_or_the_nines(string id, string vat, string eik, string vatNumber)
    {
        var doc = Sheet.Invoice(1d, "INV-1", 10, "EUR", 10, ("J", id), ("K", vat)).Single();
        Assert.Equal((eik, vatNumber), (doc.Eik, doc.VatNumber));
        Assert.DoesNotContain(doc.Issues, i => i.IsOpen);
    }

    [Fact]
    public void The_lines_of_an_invoice_come_together_by_account()
    {
        string[] cells = Cells(Sheet
            .Invoice(1d, "INV-1", 10, "EUR", 10)
            .Invoice(1d, "INV-1", 5, "EUR", 5)
            .Invoice(1d, "INV-1", 40, "EUR", 40, ("L", "601"), ("N", "-")));
        Assert.Equal(("40;15", "601;602/1-4011", "1;1"), (cells[0], cells[1], cells[OperationsLayout.DealType]));
    }

    [Fact]
    public void A_credit_note_has_a_minus()
    {
        Assert.Equal("-61.35", Cells(Sheet.Invoice(1d, "CN-1", -61.35m, "EUR", -61.35m, ("B", "КИ"), ("C", 3d)))[0]);
        var positive = Sheet.Invoice(1d, "CN-1", 50, "EUR", 50, ("B", "КИ"), ("C", 3d)).Single();
        Assert.Equal((-50m, true), (positive.TotalGross, positive.Has(IssueCode.SignFlipped)));
    }

    [Fact]
    public void Paid_in_cash_gets_its_second_row()
    {
        var sheet = Sheet.Invoice(1d, "INV-1", 10, "EUR", 10, ("AJ", "В брой"), ("AL", "5011"));
        string[] cells = Cells(sheet, 1);
        Assert.Equal(("10", "4011-5011", "РКО-INV-1", "РКО", "1", ""), (cells[0], cells[1], cells[7], cells[8], cells[OperationsLayout.DealType], cells[OperationsLayout.Vies]));
    }

    [Fact]
    public void Without_the_euro_column_an_invoice_in_euro_takes_its_own_total() =>
        Assert.Equal("10", Cells(Sheet.Invoice(1d, "INV-1", 10, "EUR", null))[0]);

    [Fact]
    public void Without_the_euro_column_another_currency_waits_and_no_rate_is_guessed()
    {
        var doc = Sheet.Invoice(1d, "INV-1", 100, "USD", null).Single();
        Assert.False(doc.WillExport);
        Assert.Contains("Няма курс на USD за 10.09.2026", doc.Issues.Single(i => i.Code == IssueCode.MissingRate).Message);

        // The amount in euro can be typed …
        doc.Lines[0].TrySet(LineField.Euro, "86,27", out _);
        Process(doc);
        Assert.Equal(("86.27", true), (doc.Amounts(), doc.WillExport));
        doc.Lines[0].TrySet(LineField.Euro, "", out _);
        Process(doc);
        Assert.False(doc.WillExport);

        // … or the amount is said to be in euro already.
        doc.Lines[0].TrySet(LineField.Base, "86,27", out _);
        doc.TrySet(DocField.Currency, "EUR", out _);
        Process(doc);
        Assert.Equal(("86.27", true), (doc.Amounts(), doc.WillExport));
    }

    private static void Process(OperationDocument doc) =>
        new DocumentProcessor(ConverterOptions.Default).Process(Sheet.Invoice(9d, "9", 1, "EUR", 1).Read(), doc);

    [Fact]
    public void The_conversion_is_shown_with_its_rate() =>
        Assert.Contains("100,00 USD = 80,00 EUR (1 EUR = 1,25 USD)",
            Sheet.Invoice(1d, "INV-1", 100, "USD", 80).Single().Issues.Single(i => i.Code == IssueCode.ConvertedToEuro).Message);

    [Fact]
    public void An_invoice_in_euro_has_the_same_amount_in_both_columns()
    {
        Assert.False(Sheet.Invoice(1d, "INV-1", 100, "EUR", 100).Single().Has(IssueCode.EuroTotalDiffers));
        Assert.True(Sheet.Invoice(1d, "INV-1", 100, "EUR", 120).Single().Has(IssueCode.EuroTotalDiffers));
    }

    [Fact]
    public void A_rate_unlike_the_others_is_pointed_out()
    {
        var batch = Sheet
            .Invoice(1d, "INV-1", 100, "USD", 86)
            .Invoice(2d, "INV-2", 200, "USD", 172)
            .Invoice(3d, "INV-3", 50, "USD", 50)      // not converted at all
            .Invoice(4d, "INV-4", 10, "USD", 8.65m)
            .Process();
        Assert.Equal([false, false, true, false], batch.Documents.Select(d => d.Has(IssueCode.EuroRate)));

        batch.Documents[2].Lines[0].TrySet(LineField.Euro, "43", out _);
        new DocumentProcessor(ConverterOptions.Default).Process(batch, batch.Documents[2]);
        Assert.DoesNotContain(batch.Documents, d => d.Has(IssueCode.EuroRate));
    }

    [Fact]
    public void What_is_not_like_a_foreign_invoice_is_pointed_out()
    {
        Assert.True(Sheet.Invoice(1d, "INV-1", 10, "EUR", 10, ("AH", "-")).Single().Has(IssueCode.NotMarkedNoDiary));
        Assert.True(Sheet.Invoice(1d, "INV-1", 10, "EUR", 10, ("AD", 10d), ("AE", 2d)).Single().Has(IssueCode.NotMarkedNoDiary));
        Assert.True(Sheet.Invoice(1d, "INV-1", 10, "EUR", 10, ("K", SupplierVat)).Single().Has(IssueCode.NotForeign));
        Assert.True(Sheet.Invoice(1d, "INV-1", 10, "EUR", 10, ("J", SupplierEik), ("K", "-")).Single().Has(IssueCode.NotForeign));
        Assert.True(Sheet.Invoice(1d, "INV-1", 12, "EUR", 12, ("W", 10d), ("Y", 2d)).Single().Has(IssueCode.SellerVatIncluded));
    }

    [Fact]
    public void The_no_diary_setting_and_the_vat_setting_do_not_touch_foreign_invoices()
    {
        foreach (var mode in Enum.GetValues<NoDiaryMode>())
        {
            var doc = Sheet.Invoice(1d, "INV-1", 10, "EUR", 10).Single(new ConverterSettings { NoDiary = mode });
            Assert.Equal(("1", "10", true), (doc.Deals(), doc.Amounts(), doc.WillExport));
        }
    }

    [Fact]
    public void The_books_are_in_euro_whatever_the_date()
    {
        var euro = Sheet.Invoice(1d, "INV-1", 10, "EUR", 10, ("E", "10.11.2025"), ("H", "11.2025")).Single();
        Assert.Equal(("10", true), (euro.Amounts(), euro.WillExport));
        var leva = Sheet.Invoice(1d, "INV-1", 19.56m, "BGN", null, ("E", "10.11.2025"), ("H", "11.2025")).Single();
        Assert.Equal(("10", true), (leva.Amounts(), leva.WillExport));
    }

    [Fact]
    public void The_deal_type_can_still_be_chosen_by_hand()
    {
        var sheet = Sheet.Invoice(1d, "INV-1", 10, "EUR", 10, ("K", SupplierVat));
        var doc = sheet.Single();
        doc.Lines[0].TrySet(LineField.DealType, "3", out _);
        new DocumentProcessor(ConverterOptions.Default).Process(sheet.Read(), doc);
        Assert.Equal(("3", "10", false), (doc.Deals(), doc.Amounts(), doc.Has(IssueCode.NotForeign)));
    }
}

public class CheckCatalogTests
{
    [Fact]
    public void Every_check_is_described_once()
    {
        Assert.Equal(Enum.GetValues<IssueCode>().OrderBy(c => c), CheckCatalog.All.Select(c => c.Code).OrderBy(c => c));
        Assert.All(CheckCatalog.All, c =>
        {
            Assert.NotEmpty(c.Title);
            Assert.EndsWith(".", c.Help);
            Assert.NotEmpty(c.Kinds);
            Assert.Equal(c.Severity != IssueSeverity.Error, c.CanBeOff);
        });
        Assert.Equal(CheckCatalog.All.Count, CheckCatalog.All.Select(c => c.Title).Distinct().Count());
    }

    [Fact]
    public void Areas_are_listed_in_their_order()
    {
        var areas = CheckCatalog.All.Select(c => c.Area).ToList();
        Assert.Equal(areas.OrderBy(a => a), areas);
    }

    /// <summary>A check that finds something in a module has to be listed in it.</summary>
    [Fact]
    public void What_the_rules_find_is_listed_in_the_module()
    {
        OperationBatch[] batches =
        [
            new TestSheet(Layout.Purchases)
                .Purchase(1d, "", 100, 20).Purchase(2d, "2", 480, 90).Purchase(3d, "3", 50, 10, ("T", "USD"))
                .Purchase(4d, "4", 10, 0, ("AC", "-"), ("AD", "-"), ("AG", "ДА"), ("J", "DE123456789"))
                .Purchase(5d, "5", 10, 0, ("J", "DE123456789")).Purchase(6d, "6", 10, 2, ("K", ""), ("AI", "40 1"), ("J", "12345678901"))
                .Purchase(7d, "7", 10, 2, ("AB", 5d)).Purchase(8d, "8", 0, 0).Purchase(9d, "9", -5, -1).Purchase(9d, "9", -5, -1, ("A", 10d))
                .Purchase(11d, "11", 10, 2, ("Z", "Обратно начисляване 163а"), ("H", "05.2026"), ("B", "нещо"), ("C", 77d))
                .Process(),
            new TestSheet(Layout.Sales)
                .Sale(1d, "1", 100, 0).Sale(2d, "2", 100, 5, ("AB", 100d), ("AC", 5d)).Sale(3d, "3", 100, 0, ("AB", 100d))
                .Sale(4d, "4", 100, 0, ("AF", 100d)).Sale(5d, "5", 30, 0, ("AF", 10d), ("AI", 20d), ("AA", "11"))
                .Process(),
            new TestSheet(Layout.Foreign)
                .Invoice(1d, "1", 100, "USD", null).Invoice(2d, "2", 100, "EUR", 90, ("K", SupplierVat), ("AH", "-"), ("Y", 5d))
                .Invoice(3d, "3", 100, "USD", 86).Invoice(4d, "4", 100, "USD", 86).Invoice(5d, "5", 100, "USD", 100).Invoice(5d, "5", 1, "EUR", 1, ("A", 6d))
                .Process(),
        ];

        foreach (var batch in batches)
        {
            var listed = CheckCatalog.For(batch.Kind).Select(c => c.Code).ToHashSet();
            var found = batch.Documents.SelectMany(d => d.Issues).Select(i => i.Code).Distinct().ToList();
            Assert.True(found.Count > 5, $"{batch.Kind}: {string.Join(", ", found)}");
            Assert.DoesNotContain(found, code => !listed.Contains(code));
        }
    }

    [Fact]
    public void A_check_that_is_off_finds_nothing_and_holds_nothing_back()
    {
        var sheet = new TestSheet(Layout.Purchases).Purchase(1d, "1", 480, 90).Purchase(2d, "2", 480, 90, ("T", "USD"));
        var settings = new ConverterSettings();
        settings.SetOff(IssueCode.VatUnresolved, true);
        settings.ChecksOff.Add(nameof(IssueCode.MissingRate));   // by hand in the file: an error stays an error

        var batch = sheet.Process(settings);
        Assert.Equal(("4;4", false, true), (batch.Documents[0].Deals(), batch.Documents[0].HasWarnings, batch.Documents[0].WillExport));
        Assert.False(batch.Documents[1].WillExport);
    }

    [Fact]
    public void A_document_left_out_of_the_file_is_no_duplicate()
    {
        var batch = new TestSheet(Layout.Purchases).Purchase(1d, "1", 100, 20).Purchase(2d, "1", 100, 20).Process();
        Assert.All(batch.Documents, d => Assert.True(d.Has(IssueCode.Duplicate)));
        batch.Documents[1].Export = false;
        new DocumentProcessor(ConverterOptions.Default).Process(batch, batch.Documents[1]);
        Assert.All(batch.Documents, d => Assert.False(d.Has(IssueCode.Duplicate)));
    }

    [Fact]
    public void Back_to_the_file_also_takes_the_ticks_back()
    {
        var sheet = new TestSheet(Layout.Purchases).Purchase(1d, "1", 480, 90);
        var batch = sheet.Process();
        var doc = batch.Documents[0];
        var processor = new DocumentProcessor(ConverterOptions.Default);
        doc.SetReviewed(doc.Issues.Single(i => i.Code == IssueCode.VatUnresolved), true);
        processor.Process(batch, doc);
        Assert.False(doc.HasWarnings);

        doc.Revert();
        processor.Process(batch, doc);
        Assert.True(doc.HasWarnings);
    }
}
