using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Rules;
using MicroinvestConvert.Core.Settings;
using static MicroinvestConvert.Tests.TestSheet;

namespace MicroinvestConvert.Tests;

public class PurchaseRuleTests
{
    private static readonly (string, object?)[] NoLedger = [("AC", "-"), ("AD", "-")];

    private static TestSheet Sheet => new(Layout.Purchases);

    [Theory]
    [InlineData(245.65, 49.13, "20%")]
    [InlineData(100.00, 9.00, "9%")]
    [InlineData(312.47, 62.47, "20%")]   // two cents off
    public void Vat_of_20_or_9_percent_is_2(double @base, double vat, string rate)
    {
        var doc = Sheet.Purchase(1d, "1", (decimal)@base, (decimal)vat, ("W", rate)).Single();
        Assert.Equal("2;2", doc.Deals());
        Assert.Equal("601/5;453/1", doc.Accounts());
    }

    [Fact]
    public void No_vat_is_3_with_one_amount()
    {
        var doc = Sheet.Purchase(1d, "1", 443, 0, ("W", "0%")).Single();
        Assert.Equal(("443", "601/5", "3"), (doc.Amounts(), doc.Accounts(), doc.Deals()));
        Assert.False(doc.Has(IssueCode.VatZeroButDeclared));
    }

    [Fact]
    public void Declared_20_without_vat_is_3_and_is_pointed_out()
    {
        var doc = Sheet.Purchase(1d, "1", 600, 0).Single();
        Assert.Equal("3", doc.Deals());
        Assert.True(doc.Has(IssueCode.VatZeroButDeclared));
        Assert.True(doc.WillExport);
    }

    [Fact]
    public void Vat_that_fits_no_rate_is_4_and_is_pointed_out()
    {
        var doc = Sheet.Purchase(1d, "1", 480.00m, 90.00m).Single();
        Assert.Equal(("4;4", "480;90"), (doc.Deals(), doc.Amounts()));
        Assert.True(doc.Has(IssueCode.VatUnresolved));
        Assert.True(doc.WillExport);
    }

    [Fact]
    public void Without_credit_the_gross_goes_to_3()
    {
        var doc = Sheet.Purchase(1d, "1", 25.00m, 5.00m, [.. NoLedger, ("AB", 30d), ("Z", "без Дан. Кредит")]).Single();
        Assert.Equal(("30", "601/5", "3"), (doc.Amounts(), doc.Accounts(), doc.Deals()));
    }

    [Fact]
    public void Partial_credit_is_4()
    {
        var doc = Sheet.Purchase(1d, "1", 100, 20, [.. NoLedger, ("AE", 100d), ("AF", 20d), ("Z", "частичен Дан. Кредит")]).Single();
        Assert.Equal(("100;20", "4;4"), (doc.Amounts(), doc.Deals()));
        Assert.False(doc.Has(IssueCode.VatUnresolved));
    }

    [Theory]
    [InlineData(NoDiaryMode.Internal, "1", true)]
    [InlineData(NoDiaryMode.NoCredit, "3", true)]
    [InlineData(NoDiaryMode.Skip, "1", false)]
    public void Without_ledger_is_1_or_as_the_setting_says(NoDiaryMode mode, string deal, bool written)
    {
        var doc = Sheet.Purchase(1d, "1", 100, 20, [.. NoLedger, ("AG", "ДА")]).Single(new ConverterSettings { NoDiary = mode });
        Assert.Equal(("120", deal, written), (doc.Amounts(), doc.Deals(), doc.WillExport));
    }

    [Fact]
    public void Different_rates_in_one_document_are_resolved_apart()
    {
        var doc = Sheet
            .Purchase(1d, "1", 100, 20)
            .Purchase(1d, "1", 50, 4.50m, ("W", "9%"), ("K", "602"), ("M", "-"))
            .Purchase(1d, "1", 30, 0, ("W", "0%"), ("K", "609"), ("M", "-"))
            .Single();

        Assert.Equal("100;50;24.5;30", doc.Amounts());
        Assert.Equal("601/5;602;453/1;609", doc.Accounts());
        Assert.Equal("2;2;2;3", doc.Deals());
    }

    [Fact]
    public void Many_lines_off_by_a_cent_each_are_still_20_percent()
    {
        var sheet = Sheet;
        for (int i = 0; i < 12; i++) sheet.Purchase(1d, "1", 1.03m, 0.20m);   // 0.206 → 0.21, off by one cent each
        var doc = sheet.Single();
        Assert.Equal("2;2", doc.Deals());
        Assert.Equal(VatStatus.WithinTolerance, Assert.Single(doc.VatGroups).Status);
    }

    [Fact]
    public void Vat_on_a_line_of_its_own_belongs_to_the_bases()
    {
        var doc = Sheet
            .Purchase(1d, "1", 100, 0)
            .Purchase(1d, "1", 0, 20)
            .Single();
        Assert.Equal(("100;20", "2;2"), (doc.Amounts(), doc.Deals()));
    }

    [Fact]
    public void One_bad_line_does_not_spoil_the_good_ones()
    {
        var doc = Sheet
            .Purchase(1d, "1", 100, 20)
            .Purchase(1d, "1", 50, 7, ("K", "602"), ("M", "-"))
            .Single();
        Assert.Equal("100;20;50;7", doc.Amounts());
        Assert.Equal("2;2;4;4", doc.Deals());
    }

    [Theory]
    [InlineData("9%")]
    [InlineData("освободена")]
    public void A_declared_rate_that_is_not_the_one_of_the_amounts_is_pointed_out(string rate)
    {
        var doc = Sheet.Purchase(1d, "1", 100, 20, ("W", rate)).Single();
        Assert.Equal("2;2", doc.Deals());
        Assert.True(doc.Has(IssueCode.VatDifferentRate));
        Assert.False(Sheet.Purchase(1d, "1", 100, 20, ("W", "-")).Single().Has(IssueCode.VatDifferentRate));
    }

    [Fact]
    public void Nothing_in_the_ledger_columns_is_decided_by_the_amounts()
    {
        Assert.Equal("2;2", Sheet.Purchase(1d, "1", 100, 20, [.. NoLedger, ("Z", "")]).Single().Deals());
        Assert.Equal("3", Sheet.Purchase(1d, "1", 100, 0, [.. NoLedger, ("Z", ""), ("W", "0%")]).Single().Deals());

        var odd = Sheet.Purchase(1d, "1", 100, 13, [.. NoLedger, ("Z", "Смесен")]).Single();
        Assert.Equal("4;4", odd.Deals());
        Assert.True(odd.Has(IssueCode.Unspecified));
    }

    [Fact]
    public void A_zero_document_keeps_the_declared_treatment()
    {
        var doc = Sheet.Purchase(1d, "1", 0, 0).Single();
        Assert.Equal(("0;0", "601/5;453/1", "2;2"), (doc.Amounts(), doc.Accounts(), doc.Deals()));
        Assert.True(doc.Has(IssueCode.ZeroDocument));
    }

    [Fact]
    public void Reverse_charge_waits_for_the_protocols()
    {
        var doc = Sheet.Purchase(1d, "1", 100, 0, [.. NoLedger, ("Z", "Обратно начисляване 163а - 02")]).Single();
        Assert.True(doc.Has(IssueCode.ReverseCharge));
        Assert.False(doc.WillExport);
    }

    [Fact]
    public void A_purchase_from_abroad_outside_the_ledger_says_that_the_protocol_is_missing()
    {
        string Note(string id) => Sheet.Purchase(1d, "1", 100, 0, [.. NoLedger, ("AG", "ДА"), ("J", id)]).Single()
            .Issues.Single(i => i.Code == IssueCode.NoDiary).Message;

        Assert.Contains("протоколът за самоначисляване", Note("DE123456789"));
        Assert.DoesNotContain("протокол", Note(SupplierVat));

        var doc = Sheet.Purchase(1d, "1", 100, 0, [.. NoLedger, ("AG", "ДА"), ("J", "DE123456789")]).Single();
        Assert.Equal(("1", true, false), (doc.Deals(), doc.WillExport, doc.HasWarnings));   // as before: 1, and no warning
    }

    [Fact]
    public void A_foreign_supplier_in_the_ledger_is_pointed_out()
    {
        Assert.True(Sheet.Purchase(1d, "1", 443, 0, ("W", "0%"), ("J", "DK12345678")).Single().Has(IssueCode.ForeignSupplier));
        Assert.False(Sheet.Purchase(1d, "1", 443, 0, [.. NoLedger, ("AG", "ДА"), ("J", "DK12345678")]).Single().Has(IssueCode.ForeignSupplier));
    }

    [Fact]
    public void A_deal_type_set_by_hand_wins_and_silences_the_warning()
    {
        var batch = Sheet.Purchase(1d, "1", 480.00m, 90.00m).Process();
        var doc = batch.Documents[0];
        doc.Lines[0].DealTypeOverride = 2;
        new DocumentProcessor(ConverterOptions.Default).Process(batch, doc);

        Assert.Equal(("2;2", "480;90"), (doc.Deals(), doc.Amounts()));
        Assert.False(doc.Has(IssueCode.VatUnresolved));
        Assert.True(doc.Has(IssueCode.Overridden));
    }

    [Fact]
    public void Set_to_3_by_hand_the_vat_becomes_expense()
    {
        var batch = Sheet.Purchase(1d, "1", 100, 20).Process();
        batch.Documents[0].Lines[0].DealTypeOverride = 3;
        new DocumentProcessor(ConverterOptions.Default).Process(batch);
        Assert.Equal(("120", "601/5", "3"), (batch.Documents[0].Amounts(), batch.Documents[0].Accounts(), batch.Documents[0].Deals()));
    }

    [Fact]
    public void Set_from_no_credit_to_2_by_hand_the_gross_gives_its_vat_back()
    {
        var batch = Sheet.Purchase(1d, "1", 25.00m, 5.00m, [.. NoLedger, ("AB", 30d)]).Process();
        batch.Documents[0].Lines[0].DealTypeOverride = 2;
        new DocumentProcessor(ConverterOptions.Default).Process(batch);
        Assert.Equal(("25;5", "2;2"), (batch.Documents[0].Amounts(), batch.Documents[0].Deals()));
    }

    [Fact]
    public void A_mixed_line_can_be_split_and_put_back()
    {
        var batch = Sheet.Purchase(1d, "1", 480.00m, 90.00m).Process();
        var doc = batch.Documents[0];
        var processor = new DocumentProcessor(ConverterOptions.Default);

        Assert.Equal((450.00m, 30.00m), OperationDocument.SplitOf(doc.Lines[0], 20m));
        Assert.True(doc.Split(doc.Lines[0], 20m, 2, 3));
        processor.Process(batch, doc);
        Assert.Equal(("450;90;30", "2;2;3"), (doc.Amounts(), doc.Deals()));
        Assert.Equal("601/5;453/1;601/5", doc.Accounts());
        Assert.False(doc.HasWarnings);
        Assert.True(doc.AnyEdited);

        doc.Revert();
        processor.Process(batch, doc);
        Assert.Equal("480;90", doc.Amounts());
        Assert.False(doc.AnyEdited);
    }

    [Fact]
    public void There_is_no_split_when_the_vat_is_too_much()
    {
        var line = Sheet.Purchase(1d, "1", 100, 30).Read().Documents[0].Lines[0];
        Assert.Null(OperationDocument.SplitOf(line, 20m));
        Assert.Null(OperationDocument.SplitOf(line, 0m));
    }

    [Fact]
    public void The_allowed_difference_and_the_rounding_come_from_the_settings()
    {
        var strict = new ConverterSettings { TolerancePerDocument = 0.01m, TolerancePerLine = 0.01m };
        Assert.Equal("4;4", Sheet.Purchase(1d, "1", 312.47m, 62.47m).Single(strict).Deals());
        Assert.Equal("2;2", Sheet.Purchase(1d, "1", 312.47m, 62.47m).Single().Deals());
    }
}

public class SaleRuleTests
{
    private static TestSheet Sheet => new(Layout.Sales);

    [Theory]
    [InlineData("AB", "7", "1")]
    [InlineData("AD", "11", "1")]
    [InlineData("AG", "12", "1")]
    [InlineData("AF", "13", "2")]
    [InlineData("AH", "14", "1")]
    [InlineData("AI", "15", "4")]
    [InlineData("AJ", "16", "1")]
    [InlineData("AK", "17", "1")]
    public void Every_column_has_its_deal_type_and_vies(string column, string deal, string vies)
    {
        var doc = Sheet.Sale(1d, "1", 100, 0, (column, 100d)).Single();
        Assert.Equal(deal, doc.Deals());
        Assert.Equal(vies, string.Join(';', doc.Segments.Select(s => s.Vies)));
        Assert.Equal("703", doc.Accounts());
    }

    [Fact]
    public void Twenty_percent_has_its_vat_on_453_2()
    {
        var doc = Sheet.Sale(1d, "1", 375, 75, ("AB", 375d), ("AC", 75d)).Single();
        Assert.Equal(("375;75", "703;453/2", "7;7"), (doc.Amounts(), doc.Accounts(), doc.Deals()));
        Assert.Empty(doc.Issues);
    }

    [Fact]
    public void Mixed_rates_are_written_like_the_model()
    {
        // Model.xls row 14: 100;20;100;9 | 411-702;453/2;703;453/2 | 7;7;11;11
        var doc = Sheet
            .Sale(1d, "1", 100, 20, ("K", "702"), ("AB", 100d), ("AC", 20d))
            .Sale(1d, "1", 100, 9, ("K", "703"), ("AD", 100d), ("AE", 9d))
            .Single();
        Assert.Equal(("100;20;100;9", "702;453/2;703;453/2", "7;7;11;11"), (doc.Amounts(), doc.Accounts(), doc.Deals()));
    }

    [Fact]
    public void Wrong_vat_is_pointed_out_and_the_deal_type_stays()
    {
        var doc = Sheet.Sale(1d, "1", 100, 15, ("AB", 100d), ("AC", 15d)).Single();
        Assert.Equal("7;7", doc.Deals());
        Assert.True(doc.Has(IssueCode.VatMismatch));
        Assert.Equal(5m, -Assert.Single(doc.VatGroups).Difference);
    }

    [Fact]
    public void Cents_of_difference_are_only_noted()
    {
        var doc = Sheet.Sale(1d, "1", 312.47m, 62.47m, ("AB", 312.47d), ("AC", 62.47d)).Single();
        Assert.False(doc.HasWarnings);
        Assert.True(doc.Has(IssueCode.VatWithinTolerance));
    }

    [Fact]
    public void Twenty_percent_without_vat_is_pointed_out() =>
        Assert.True(Sheet.Sale(1d, "1", 155, 0, ("AB", 155d)).Single().Has(IssueCode.VatZeroButDeclared));

    [Fact]
    public void Nothing_in_the_ledger_columns_is_1_and_is_pointed_out()
    {
        var doc = Sheet.Sale(1d, "1", 100, 0).Single();
        Assert.Equal("1", doc.Deals());
        Assert.True(doc.Has(IssueCode.NoDiary));
    }

    [Fact]
    public void Vies_needs_a_vat_number_of_another_member_state()
    {
        Assert.True(Sheet.Sale(1d, "1", 100, 0, ("AF", 100d)).Single().Has(IssueCode.ViesPartner));
        Assert.True(Sheet.Sale(1d, "1", 100, 0, ("AF", 100d), ("J", "GB123456789")).Single().Has(IssueCode.ViesPartner));
        Assert.False(Sheet.Sale(1d, "1", 100, 0, ("AF", 100d), ("J", "DE123456789")).Single().Has(IssueCode.ViesPartner));
        Assert.False(Sheet.Sale(1d, "1", 100, 0, ("AG", 100d), ("J", "GB123456789")).Single().Has(IssueCode.ViesPartner));
    }

    [Fact]
    public void The_ledger_cell_of_the_file_is_compared()
    {
        Assert.True(Sheet.Sale(1d, "1", 100, 0, ("AG", 100d), ("AA", "22")).Single().Has(IssueCode.JournalCellMismatch));
        Assert.False(Sheet.Sale(1d, "1", 100, 0, ("AG", 100d), ("AA", "19")).Single().Has(IssueCode.JournalCellMismatch));
        Assert.False(Sheet.Sale(1d, "1", 375, 75, ("AB", 375d), ("AC", 75d), ("AA", "11,12")).Single().Has(IssueCode.JournalCellMismatch));
        Assert.False(Sheet.Sale(1d, "1", 100, 0, ("AG", 100d), ("AA", "-")).Single().Has(IssueCode.JournalCellMismatch));
    }

    [Fact]
    public void Different_vies_in_one_document_is_pointed_out()
    {
        var doc = Sheet
            .Sale(1d, "1", 100, 0, ("AF", 100d), ("J", "DE123456789"))
            .Sale(1d, "1", 50, 0, ("AI", 50d), ("K", "704"), ("J", "DE123456789"))
            .Single();
        Assert.True(doc.Has(IssueCode.ViesMixed));
    }
}

public class DocumentRuleTests
{
    private static TestSheet Purchases => new(Layout.Purchases);
    private static TestSheet Sales => new(Layout.Sales);

    [Fact]
    public void Accounts_of_a_document_are_added_up_largest_first()
    {
        var doc = Purchases
            .Purchase(1d, "1", 10, 2, ("K", "602"), ("M", "1"))
            .Purchase(1d, "1", 100, 20)
            .Purchase(1d, "1", 15, 3, ("K", "602"), ("M", "1"))
            .Single();
        Assert.Equal(("100;25;25", "601/5;602/1;453/1"), (doc.Amounts(), doc.Accounts()));
    }

    [Fact]
    public void The_accounts_of_the_file_are_written_as_they_are()
    {
        Assert.Equal("4111/1;453/1", Purchases.Purchase(1d, "1", 100, 20, ("K", "4111"), ("M", "1")).Single().Accounts());
        Assert.Equal("411/1/1;453/1", Purchases.Purchase(1d, "1", 100, 20, ("K", "411/1"), ("M", "1")).Single().Accounts());
        Assert.Equal("4111;453/1", Purchases.Purchase(1d, "1", 100, 20, ("K", "4111"), ("M", "-")).Single().Accounts());
        Assert.Equal("4111;453/1", Purchases.Purchase(1d, "1", 100, 20, ("K", 4111d), ("M", null)).Single().Accounts());
        Assert.Equal("4011", Purchases.Purchase(1d, "1", 100, 20, ("AI", "4011")).Single().CounterAccountOut);
        Assert.Equal("401/1", Purchases.Purchase(1d, "1", 100, 20, ("AI", "401 / 1/")).Single().CounterAccountOut);
        Assert.Equal("5011", Purchases.Purchase(1d, "1", 100, 20, ("AH", "В брой"), ("AJ", "5011")).Single().CashAccountOut);
    }

    [Fact]
    public void The_same_account_typed_in_two_ways_is_one_amount()
    {
        var doc = Purchases
            .Purchase(1d, "1", 100, 20, ("K", "601"), ("M", "5"))
            .Purchase(1d, "1", 50, 10, ("K", "601/5/"), ("M", "-"))
            .Single();
        Assert.Equal(("150;30", "601/5;453/1"), (doc.Amounts(), doc.Accounts()));
    }

    [Fact]
    public void An_account_with_two_possible_meanings_stops_the_document()
    {
        var doc = Purchases.Purchase(1d, "1", 100, 20, ("K", "411 1"), ("M", "-")).Single();
        Assert.True(doc.Has(IssueCode.BadAccount));
        Assert.False(doc.WillExport);

        doc.Lines[0].TrySet(LineField.Account, "411/1", out _);
        new DocumentProcessor(ConverterOptions.Default).Process(new TestSheet(Layout.Purchases).Purchase(1d, "1", 1, 0).Read(), doc);
        Assert.True(doc.WillExport);
    }

    [Fact]
    public void Vat_can_follow_every_account()
    {
        var doc = Purchases
            .Purchase(1d, "1", 100, 20)
            .Purchase(1d, "1", 10, 2, ("K", "602"), ("M", "1"))
            .Single(new ConverterSettings { VatLines = VatLines.PerAccount });
        Assert.Equal(("100;20;10;2", "601/5;453/1;602/1;453/1"), (doc.Amounts(), doc.Accounts()));
    }

    [Fact]
    public void Amounts_with_more_decimals_still_add_up_to_the_document()
    {
        var doc = Sales
            .Sale(1d, "1", 560.953m, 0, ("K", "701"), ("M", "1"), ("AG", 560.953d))
            .Sale(1d, "1", 297.787m, 0, ("K", "701"), ("M", "2"), ("AG", 297.787d))
            .Sale(1d, "1", 941.832m, 0, ("K", "701"), ("M", "3"), ("AG", 941.832d))
            .Single();
        Assert.Equal(1800.57m, doc.TotalGross);       // 1800.572
        Assert.Equal(doc.TotalGross, doc.Segments.Sum(s => s.Amount));
    }

    [Fact]
    public void Rounding_moves_the_imprecise_bases_and_never_the_vat()
    {
        // 10.004 + 10.004 + 4.00 = 24.008 → 24.01, while 10.00 + 10.00 + 4.00 = 24.00
        var doc = Sales
            .Sale(1d, "1", 10.004m, 2, ("M", "1"), ("AB", 10.004d), ("AC", 2d))
            .Sale(1d, "1", 10.004m, 2, ("M", "2"), ("AB", 10.004d), ("AC", 2d))
            .Single();
        Assert.Equal("10.01;10;4", doc.Amounts());
    }

    [Fact]
    public void A_credit_note_keeps_its_minus_and_gets_one_when_it_has_none()
    {
        var negative = Purchases.Purchase(1d, "1", -4.80m, -0.96m, ("B", "КИ"), ("C", 3d)).Single();
        Assert.Equal("-4.8;-0.96", negative.Amounts());
        Assert.False(negative.Has(IssueCode.SignFlipped));

        var positive = Purchases.Purchase(1d, "1", 4.80m, 0.96m, ("B", "КИ"), ("C", 3d)).Single();
        Assert.Equal("-4.8;-0.96", positive.Amounts());
        Assert.True(positive.Has(IssueCode.SignFlipped));
    }

    [Fact]
    public void A_negative_invoice_is_pointed_out_and_kept() =>
        Assert.True(Purchases.Purchase(1d, "1", -10, -2).Single().Has(IssueCode.NegativeDocument));

    [Fact]
    public void A_negative_line_of_an_invoice_keeps_its_sign()
    {
        var doc = Purchases
            .Purchase(1d, "1", 1000, 200, ("K", "304"), ("M", "-"))
            .Purchase(1d, "1", -200, -40, ("K", "402"), ("M", "-"))
            .Single();
        Assert.Equal(("1000;-200;160", "304;402;453/1"), (doc.Amounts(), doc.Accounts()));
    }

    [Fact]
    public void The_vat_accounts_come_from_the_settings_and_the_client()
    {
        var settings = new ConverterSettings { PurchaseVatAccount = "453/8", SaleVatAccount = "453/9" };
        Assert.Equal("601/5;453/8", Purchases.Purchase(1d, "1", 100, 20).Single(settings).Accounts());
        Assert.Equal("703;453/9", Sales.Sale(1d, "1", 100, 20, ("AB", 100d), ("AC", 20d)).Single(settings).Accounts());

        var client = new ClientSettings { PurchaseVatAccount = "453/1/7" };
        Assert.Equal("601/5;453/1/7", Purchases.Purchase(1d, "1", 100, 20).Single(settings, client).Accounts());
    }

    [Fact]
    public void Another_currency_waits_for_the_next_version()
    {
        Assert.True(Purchases.Purchase(1d, "1", 100, 20, ("T", "USD")).Single().Has(IssueCode.ForeignCurrency));
        Assert.True(Purchases.Purchase(1d, "1", 100, 20, ("T", "BGN")).Single().Has(IssueCode.ForeignCurrency));
        Assert.False(Purchases.Purchase(1d, "1", 100, 20, ("T", "BGN"), ("E", "10.12.2025"), ("H", "12.2025")).Single().HasErrors);
        Assert.False(Purchases.Purchase(1d, "1", 100, 20, ("T", "-")).Single().HasErrors);
    }

    [Fact]
    public void A_period_before_the_document_is_an_error_until_the_option_is_on()
    {
        var sheet = Purchases.Purchase(1d, "1", 100, 20, ("E", "02.07.2026"), ("H", "06.2026"));
        Assert.True(sheet.Single().Has(IssueCode.PeriodBeforeDocument));
        Assert.False(sheet.Single().WillExport);

        var doc = sheet.Single(new ConverterSettings { AccountingDateEqualsDocumentDate = true });
        Assert.True(doc.WillExport);
        Assert.Equal(new DateTime(2026, 7, 2), doc.AccountingDate);
    }

    [Fact]
    public void The_accounting_date_set_by_hand_may_not_be_before_the_document()
    {
        var batch = Purchases.Purchase(1d, "1", 100, 20).Process();
        var doc = batch.Documents[0];
        var processor = new DocumentProcessor(ConverterOptions.Default);

        Assert.True(doc.TrySet(DocField.AccountingDate, "09.06.2026", out _));
        processor.Process(batch, doc);
        Assert.True(doc.HasErrors);

        Assert.True(doc.TrySet(DocField.AccountingDate, "30.06.2026", out _));
        processor.Process(batch, doc);
        Assert.False(doc.HasErrors);
        Assert.Equal(new DateTime(2026, 6, 30), doc.AccountingDate);
    }

    [Theory]
    [InlineData("AI", IssueCode.MissingCounterAccount)]
    [InlineData("K", IssueCode.MissingAccount)]
    [InlineData("D", IssueCode.MissingNumber)]
    [InlineData("E", IssueCode.MissingDate)]
    [InlineData("B", IssueCode.MissingDocumentType)]
    public void Missing_essentials_stop_the_document(string column, IssueCode code)
    {
        var doc = Purchases.Purchase(1d, "1", 100, 20, (column, "-"), ("C", null)).Single();
        Assert.True(doc.Has(code));
        Assert.False(doc.WillExport);
    }

    [Fact]
    public void Cash_without_a_cash_account_is_an_error_or_takes_the_default()
    {
        var sheet = Purchases.Purchase(1d, "1", 100, 20, ("AH", "В брой"));
        Assert.True(sheet.Single().Has(IssueCode.MissingCashAccount));
        Assert.Equal("501/1", sheet.Single(new ConverterSettings { DefaultCashAccount = "501/1" }).CashAccountOut);
        Assert.False(sheet.Single(new ConverterSettings { CashRows = false }).HasErrors);
    }

    [Fact]
    public void Duplicates_are_pointed_out_until_one_changes()
    {
        var batch = Purchases
            .Purchase(1d, "0000000001", 100, 20)
            .Purchase(2d, "0000000001", 100, 20, ("R", "друг"))
            .Process();

        // the same № index is needed for one document, so these two are separate
        Assert.Equal(2, batch.Documents.Count);
        Assert.All(batch.Documents, d => Assert.True(d.Has(IssueCode.Duplicate)));

        batch.Documents[1].Number = "0000000002";
        new DocumentProcessor(ConverterOptions.Default).Process(batch, batch.Documents[1]);
        Assert.All(batch.Documents, d => Assert.False(d.Has(IssueCode.Duplicate)));
    }

    [Fact]
    public void Ledger_amounts_that_do_not_add_up_to_the_document_are_pointed_out() =>
        Assert.True(Purchases.Purchase(1d, "1", 100, 20, ("Y", 150d)).Single().Has(IssueCode.AmountsDoNotAdd));

    [Fact]
    public void Processing_twice_changes_nothing()
    {
        var batch = Purchases.Purchase(1d, "1", 480.00m, 90.00m).Process();
        var before = batch.Documents[0].Issues.Select(i => i.Message).ToList();
        new DocumentProcessor(ConverterOptions.Default).Process(batch);
        Assert.Equal(before, batch.Documents[0].Issues.Select(i => i.Message));
    }
}

public class EditingTests
{
    private static OperationDocument Doc => new TestSheet(Layout.Purchases).Purchase(1d, "1", 100, 20).Read().Documents[0];

    [Theory]
    [InlineData(DocField.DocType, "Кредитно известие", "КИ")]
    [InlineData(DocField.DocType, "Квит – Квитанция", "Квит")]
    [InlineData(DocField.Date, "5.6.2026", "05.06.2026")]
    [InlineData(DocField.Period, "7.2026", "07.2026")]
    [InlineData(DocField.Payment, "в брой", "В брой")]
    [InlineData(DocField.Payment, "с карта", "Карта")]
    [InlineData(DocField.Vies, "2 - Вътреобщностни доставки", "2")]
    [InlineData(DocField.Reason, "  нов\nтекст ", "нов текст")]
    public void Typed_text_is_understood(DocField field, string typed, string stored)
    {
        var doc = Doc;
        Assert.True(doc.TrySet(field, typed, out _));
        Assert.Equal(stored, doc.GetText(field));
        Assert.True(doc.IsEdited(field));
        Assert.True(doc.AnyEdited);
    }

    [Theory]
    [InlineData(DocField.DocType, "Няма такъв")]
    [InlineData(DocField.Date, "32.01.2026")]
    [InlineData(DocField.Period, "юни")]
    [InlineData(DocField.Payment, "хм")]
    [InlineData(DocField.Vies, "5")]
    public void Nonsense_is_refused_and_changes_nothing(DocField field, string typed)
    {
        var doc = Doc;
        string before = doc.GetText(field);
        Assert.False(doc.TrySet(field, typed, out string error));
        Assert.NotEqual("", error);
        Assert.Equal(before, doc.GetText(field));
    }

    [Fact]
    public void Everything_can_be_put_back()
    {
        var doc = Doc;
        doc.TrySet(DocField.Number, "77", out _);
        doc.Lines[0].TrySet(LineField.Vat, "21,50", out _);
        doc.Lines[0].TrySet(LineField.DealType, "4 - Получени доставки, ВОП с право на ЧДК", out _);
        doc.Export = false;
        Assert.Equal((21.50m, 4), (doc.Lines[0].Vat, doc.Lines[0].DealTypeOverride!.Value));

        doc.Revert();
        Assert.Equal(("1", 20m, null, true), (doc.Number, doc.Lines[0].Vat, doc.Lines[0].DealTypeOverride, doc.Export));
        Assert.False(doc.AnyEdited);
    }

    [Theory]
    [InlineData(LineField.Base, "abc")]
    [InlineData(LineField.DealType, "19")]
    [InlineData(LineField.DealType, "x")]
    public void A_line_refuses_nonsense(LineField field, string typed) =>
        Assert.False(Doc.Lines[0].TrySet(field, typed, out _));
}
