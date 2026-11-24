using System.Globalization;
using System.Text.RegularExpressions;
using MicroinvestConvert.Core.Exchange;
using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Output;
using MicroinvestConvert.Core.Rules;
using MicroinvestConvert.Core.Settings;
using static MicroinvestConvert.Tests.TestSheet;

namespace MicroinvestConvert.Tests;

/// <summary>Documents in other currencies: what they become in euro, by which rate, and what is said about it.</summary>
public class CurrencyTests
{
    // 10.06.2026 is the date of the test purchases and sales, 10.09.2026 of the foreign invoices.
    private static readonly SomeRates Rates = new SomeRates()
        .Add("USD", "10.06.2026", 1.25m).Add("GBP", "10.06.2026", 0.8m)
        .Add("USD", "12.06.2026", 1.2m).Add("USD", "14.06.2026", 1.2m, published: "12.06.2026")
        .Add("USD", "10.09.2026", 1.15m).Add("GBP", "10.09.2026", 0.85m).Add("USD", "13.09.2026", 1.16m, published: "11.09.2026");

    private static TestSheet Purchases => new(Layout.Purchases);
    private static TestSheet Sales => new(Layout.Sales);
    private static TestSheet Foreign => new(Layout.Foreign);

    private static string Message(OperationDocument doc, IssueCode code) => doc.Issues.Single(i => i.Code == code).Message;

    private static void Process(OperationDocument doc, IExchangeRates? rates = null) =>
        new DocumentProcessor(ConverterOptions.Default, rates ?? Rates).Process(Purchases.Purchase(9d, "9", 1, 0.2m).Read(), doc);

    // ----- the rate of the date -----

    [Fact]
    public void A_purchase_is_written_in_euro_by_the_rate_of_its_date()
    {
        var doc = Purchases.Purchase(1d, "1", 1000, 200, ("T", "USD")).Single(rates: Rates);
        Assert.Equal(("800;160", "601/5;453/1", "2;2", true), (doc.Amounts(), doc.Accounts(), doc.Deals(), doc.WillExport));
        Assert.Equal((800m, 160m, 960m, 1200m), (doc.TotalBase, doc.TotalVat, doc.TotalGross, doc.TotalInCurrency));
        Assert.Equal("1200,00 USD = 960,00 EUR (1 EUR = 1,25 USD), по курса за 10.06.2026.", Message(doc, IssueCode.ConvertedToEuro));
        Assert.DoesNotContain(doc.Issues, i => i.IsOpen);
    }

    [Fact]
    public void A_sale_too()
    {
        var doc = Sales.Sale(1d, "1", 500, 0, ("AG", 500d), ("T", "GBP")).Single(rates: Rates);
        Assert.Equal(("625", "12"), (doc.Amounts(), doc.Deals()));
    }

    [Fact]
    public void Amounts_are_divided_by_the_units_for_one_euro()
    {
        // 70 / 1.16 = 60.3448; with the reverse rate 0.8621 it would be 60.347.
        var rates = new SomeRates().Add("USD", "10.06.2026", 1.16m);
        Assert.Equal("60.34", Purchases.Purchase(1d, "1", 70, 0, ("T", "USD"), ("W", "0%")).Single(rates: rates).Amounts());
    }

    [Fact]
    public void The_document_adds_up_to_its_total_in_euro()
    {
        // 33.33 / 1.15 = 28.9826 each, 86.9478 together
        var rates = new SomeRates().Add("USD", "10.06.2026", 1.15m);
        var doc = Purchases
            .Purchase(1d, "1", 33.33m, 0, ("T", "USD"), ("W", "0%"), ("K", "601"))
            .Purchase(1d, "1", 33.33m, 0, ("T", "USD"), ("W", "0%"), ("K", "602"))
            .Purchase(1d, "1", 33.33m, 0, ("T", "USD"), ("W", "0%"), ("K", "609"))
            .Single(rates: rates);
        Assert.Equal("28.99;28.98;28.98", doc.Amounts());
        Assert.Equal(86.95m, doc.TotalGross);
    }

    [Fact]
    public void What_comes_to_zero_in_the_currency_is_zero_in_euro()
    {
        var doc = Purchases
            .Purchase(1d, "1", 1000, 200, ("T", "BGN"), ("K", "601"), ("M", "-"))
            .Purchase(1d, "1", -1000, -200, ("T", "BGN"), ("K", "602"), ("M", "7"))
            .Single();
        Assert.Equal(("511.29;102.26;-511.29;-102.26", "601;453/1;602/7;453/1", 0m), (doc.Amounts(), doc.Accounts(), doc.TotalGross));
        Assert.False(doc.Has(IssueCode.NegativeDocument));
    }

    [Fact]
    public void The_vat_is_judged_in_the_currency_of_the_document()
    {
        Assert.Equal("2;2", Purchases.Purchase(1d, "1", 17.63m, 3.53m, ("T", "USD")).Single(rates: Rates).Deals());
        var off = Purchases.Purchase(1d, "1", 480, 90, ("T", "USD")).Single(rates: Rates);
        Assert.Equal(("4;4", true), (off.Deals(), off.Has(IssueCode.VatUnresolved)));
    }

    [Fact]
    public void A_credit_note_has_its_minus_in_euro()
    {
        var doc = Purchases.Purchase(1d, "1", 100, 20, ("T", "USD"), ("B", "КИ"), ("C", 3d)).Single(rates: Rates);
        Assert.Equal(("-80;-16", true), (doc.Amounts(), doc.Has(IssueCode.SignFlipped)));
        Assert.Contains("Ако трябва курсът на фактурата", Message(doc, IssueCode.CreditNoteRate));
    }

    // ----- another date, another rate -----

    [Fact]
    public void Another_date_brings_its_rate_and_without_it_the_date_of_the_document_is_back()
    {
        var doc = Purchases.Purchase(1d, "1", 1000, 200, ("T", "USD")).Single(rates: Rates);
        Assert.True(doc.TrySet(DocField.RateDate, "12.6.2026", out _));
        Process(doc);
        Assert.Equal(("833.33;166.67", true), (doc.Amounts(), doc.IsEdited(DocField.RateDate)));
        Assert.Contains("по курса за 12.06.2026.", Message(doc, IssueCode.ConvertedToEuro));
        Assert.Equal(new DateTime(2026, 6, 10), doc.Date);   // only the rate moves
        Assert.Equal(new DateTime(2026, 6, 10), doc.AccountingDate);

        Assert.True(doc.TrySet(DocField.RateDate, "", out _));
        Process(doc);
        Assert.Equal(("800;160", false), (doc.Amounts(), doc.IsEdited(DocField.RateDate)));
    }

    [Fact]
    public void On_a_sunday_the_rate_is_of_friday_and_that_is_said()
    {
        var doc = Purchases.Purchase(1d, "1", 1000, 200, ("T", "USD"), ("E", "14.06.2026")).Single(rates: Rates);
        Assert.Equal(new DateTime(2026, 6, 12), doc.Conversions[0].Published);
        Assert.Contains("по курса за 14.06.2026 – от 12.06.2026.", Message(doc, IssueCode.ConvertedToEuro));
    }

    [Fact]
    public void A_typed_rate_stands_for_the_official_one()
    {
        var doc = Purchases.Purchase(1d, "1", 1000, 200, ("T", "USD")).Single();   // no rates at all
        Assert.False(doc.WillExport);
        Assert.True(doc.TrySet(DocField.Rate, "1,6", out _));
        Process(doc, NoRates.Instance);
        Assert.Equal(("625;125", true), (doc.Amounts(), doc.WillExport));
        Assert.Contains("по въведен курс", Message(doc, IssueCode.ConvertedToEuro));

        Process(doc);   // against the official 1.25
        Assert.Contains("официалният за 10.06.2026 е 1,25", Message(doc, IssueCode.RateDiffers));
        doc.TrySet(DocField.Rate, "1.26", out _);
        Process(doc);
        Assert.False(doc.Has(IssueCode.RateDiffers));
        doc.TrySet(DocField.Rate, "", out _);
        Process(doc);
        Assert.Equal("800;160", doc.Amounts());
    }

    [Theory]
    [InlineData(DocField.RateDate, "петък")]
    [InlineData(DocField.RateDate, "31.02.2026")]
    [InlineData(DocField.Rate, "скъпо")]
    [InlineData(DocField.Rate, "0")]
    [InlineData(DocField.Rate, "-1,2")]
    public void What_is_no_date_and_no_rate_is_not_taken(DocField field, string text)
    {
        var doc = Purchases.Purchase(1d, "1", 1000, 200, ("T", "USD")).Single(rates: Rates);
        Assert.False(doc.TrySet(field, text, out string error));
        Assert.NotEmpty(error);
        Process(doc);
        Assert.Equal("800;160", doc.Amounts());
    }

    [Fact]
    public void Back_to_the_file_takes_the_date_and_the_rate_too()
    {
        var doc = Purchases.Purchase(1d, "1", 1000, 200, ("T", "USD")).Single(rates: Rates);
        doc.TrySet(DocField.RateDate, "12.06.2026", out _);
        doc.TrySet(DocField.Rate, "2", out _);
        Assert.True(doc.AnyEdited);
        doc.Revert();
        Process(doc);
        Assert.Equal(("800;160", false), (doc.Amounts(), doc.AnyEdited));
    }

    // ----- no rate -----

    [Fact]
    public void Without_a_rate_the_document_waits_and_says_what_is_missing()
    {
        var doc = Purchases.Purchase(1d, "1", 1000, 200, ("T", "USD"), ("E", "01.07.2026"), ("H", "07.2026")).Single(rates: Rates);
        Assert.Equal((false, false, new DateTime(2026, 7, 1)), (doc.WillExport, doc.IsInEuro, doc.UnknownRateDay));
        Assert.Contains("Няма курс на USD за 01.07.2026. Изтеглете го", Message(doc, IssueCode.MissingRate));
        Assert.False(doc.Has(IssueCode.ConvertedToEuro));

        doc.TrySet(DocField.RateDate, "10.06.2026", out _);
        Process(doc);
        Assert.Equal((true, true, null), (doc.WillExport, doc.IsInEuro, doc.UnknownRateDay));
    }

    [Fact]
    public void A_rate_that_is_not_published_yet_is_not_to_be_downloaded()
    {
        string today = DateTime.Today.ToString("dd.MM.yyyy");
        var doc = Purchases.Purchase(1d, "1", 1000, 200, ("T", "USD"), ("E", today), ("H", "-")).Single(rates: Rates);
        Assert.Contains($"за {today} още не е публикуван", Message(doc, IssueCode.MissingRate));
    }

    [Fact]
    public void A_currency_that_is_not_in_the_table_is_not_waited_for()
    {
        var doc = Purchases.Purchase(1d, "1", 1000, 200, ("T", "RUB")).Single(rates: Rates);
        Assert.Contains("няма официален курс на RUB", Message(doc, IssueCode.MissingRate));
        Assert.Null(doc.UnknownRateDay);   // the day is there, downloading it again would bring nothing
    }

    [Fact]
    public void What_is_no_currency_is_asked_about()
    {
        var doc = Purchases.Purchase(1d, "1", 1000, 200, ("T", "долар")).Single(rates: Rates);
        Assert.Contains("„ДОЛАР“ не е код на валута", Message(doc, IssueCode.MissingRate));
        doc.TrySet(DocField.Currency, "usd", out _);
        Process(doc);
        Assert.Equal("800;160", doc.Amounts());
    }

    [Fact]
    public void Without_a_date_there_is_no_rate()
    {
        var doc = Purchases.Purchase(1d, "1", 1000, 200, ("T", "USD"), ("E", "-")).Single(rates: Rates);
        Assert.Contains("няма дата", Message(doc, IssueCode.MissingRate));
        Assert.True(doc.Has(IssueCode.MissingDate));
    }

    // ----- leva and euro -----

    [Theory]
    [InlineData("BGN", "10.06.2026", "06.2026")]
    [InlineData("лв.", "10.12.2025", "12.2025")]
    public void Leva_become_euro_by_the_fixed_rate_whenever(string currency, string date, string period)
    {
        var doc = Purchases.Purchase(1d, "1", 195.583m, 39.1166m, ("T", currency), ("E", date), ("H", period)).Single();
        Assert.Equal(("100;20", true), (doc.Amounts(), doc.WillExport));
        Assert.Contains("(1 EUR = 1,95583 BGN), по фиксирания курс", Message(doc, IssueCode.ConvertedToEuro));
        Assert.Null(doc.UnknownRateDay);
    }

    [Theory]
    [InlineData("EUR")]
    [InlineData("евро")]
    [InlineData("€")]
    [InlineData("-")]
    [InlineData("")]
    public void Euro_stay_as_they_are(string currency)
    {
        var doc = Purchases.Purchase(1d, "1", 100, 20, ("T", currency)).Single();
        Assert.Equal(("100;20", "EUR", false, true), (doc.Amounts(), doc.Currency, doc.InForeignCurrency, doc.IsInEuro));
        Assert.Empty(doc.Conversions);
        Assert.Null(doc.TotalInCurrency);
    }

    [Fact]
    public void A_table_in_leva_converts_through_the_fixed_rate()
    {
        var usd = KikInfoPage.Parse(RatePages.InLeva()).Rows.Single(r => r.Code == "USD");
        var rates = new SomeRates().Add("USD", "31.12.2025", usd.PerEuro("BGN"), published: "30.12.2025");
        // 195.583 USD × 1.70 лв. = 332.4911 лв. = 170.00 EUR
        Assert.Equal("170", Purchases.Purchase(1d, "1", 195.583m, 0, ("T", "USD"), ("W", "0%"), ("E", "31.12.2025"), ("H", "12.2025")).Single(rates: rates).Amounts());
    }

    [Fact]
    public void A_currency_set_by_hand_stands_for_every_row()
    {
        var doc = Purchases
            .Purchase(1d, "1", 100, 20, ("T", "USD"))
            .Purchase(1d, "1", 100, 20, ("T", "USD"), ("K", "602")).Single(rates: Rates);
        doc.TrySet(DocField.Currency, "GBP", out _);
        Process(doc);
        Assert.Equal("125;25;125;25", doc.Amounts());
        doc.TrySet(DocField.Currency, "EUR", out _);
        Process(doc);
        Assert.Equal("100;20;100;20", doc.Amounts());
    }

    [Fact]
    public void Rows_in_different_currencies_are_converted_each_by_its_own()
    {
        var sheet = Purchases
            .Purchase(1d, "1", 100, 20, ("T", "USD"))
            .Purchase(1d, "1", 80, 16, ("T", "GBP"), ("K", "602"))
            .Purchase(1d, "1", 10, 2, ("T", "EUR"), ("K", "609"));
        var doc = sheet.Single(rates: Rates);
        Assert.Equal(("100;20;80;16;10;2", 2), (doc.Amounts(), doc.Conversions.Count));

        // One typed rate cannot be for both.
        doc.TrySet(DocField.Rate, "1,3", out _);
        Process(doc);
        Assert.False(doc.WillExport);
        Assert.Contains("в няколко валути (USD, GBP)", doc.Issues.First(i => i.Code == IssueCode.MissingRate).Message);
    }

    [Fact]
    public void A_row_that_says_nothing_is_in_the_currency_of_its_document()
    {
        var doc = Purchases
            .Purchase(1d, "1", 100, 20, ("T", "USD"))
            .Purchase(1d, "1", 100, 20, ("T", "-"), ("K", "602")).Single(rates: Rates);
        Assert.Equal("80;16;80;16", doc.Amounts());
    }

    // ----- foreign invoices: the file has the euro -----

    [Fact]
    public void The_euro_of_the_file_are_written_and_agree_with_the_rate()
    {
        var doc = Foreign.Invoice(1d, "INV-1", 100, "USD", 86.96m).Single(rates: Rates);
        Assert.Equal(("86.96", RateOrigin.File, 1.15m), (doc.Amounts(), doc.Conversions[0].Origin, doc.Conversions[0].PerEuro));
        Assert.Equal("100,00 USD = 86,96 EUR (1 EUR = 1,15 USD), от файла (кол. AI), по курса за 10.09.2026.", Message(doc, IssueCode.ConvertedToEuro));
        Assert.DoesNotContain(doc.Issues, i => i.IsOpen);
    }

    [Fact]
    public void Another_date_takes_the_place_of_the_file_and_back()
    {
        var doc = Foreign.Invoice(1d, "INV-1", 100, "USD", 86.96m).Single(rates: Rates);
        doc.TrySet(DocField.RateDate, "13.09.2026", out _);
        Process(doc);
        Assert.Equal(("86.21", RateOrigin.Official), (doc.Amounts(), doc.Conversions[0].Origin));
        doc.TrySet(DocField.RateDate, "", out _);
        Process(doc);
        Assert.Equal(("86.96", RateOrigin.File), (doc.Amounts(), doc.Conversions[0].Origin));
    }

    [Fact]
    public void Without_the_euro_of_the_file_the_rate_is_used() =>
        Assert.Equal("86.96", Foreign.Invoice(1d, "INV-1", 100, "USD", null).Single(rates: Rates).Amounts());

    [Fact]
    public void Euro_of_the_file_that_are_not_by_the_rate_are_pointed_out()
    {
        var doc = Foreign.Invoice(1d, "INV-1", 100, "USD", 85.5m).Single(rates: Rates);
        Assert.Equal("85.5", doc.Amounts());   // the file is written, the user decides
        Assert.Contains("100,00 USD са 86,96 EUR, а във файла са 85,50", Message(doc, IssueCode.RateDiffers));
        Assert.True(doc.HasWarnings);

        doc.TrySet(DocField.RateDate, "10.09.2026", out _);
        Process(doc);
        Assert.Equal(("86.96", false), (doc.Amounts(), doc.HasWarnings));
    }

    [Fact]
    public void Cents_of_rounding_are_no_difference() =>
        Assert.False(Foreign.Invoice(1d, "INV-1", 100, "USD", 86.97m).Single(rates: Rates).Has(IssueCode.RateDiffers));

    [Fact]
    public void A_typed_amount_in_euro_wins_over_every_rate()
    {
        var doc = Foreign.Invoice(1d, "INV-1", 100, "USD", 86.96m).Single(rates: Rates);
        Assert.True(doc.Lines[0].TrySet(LineField.Euro, "90", out _));
        doc.TrySet(DocField.RateDate, "13.09.2026", out _);
        Process(doc);
        Assert.Equal("90", doc.Amounts());
        Assert.Equal("100,00 USD = 90,00 EUR, въведена сума.", Message(doc, IssueCode.ConvertedToEuro));
        Assert.False(doc.Lines[0].TrySet(LineField.Euro, "много", out _));
        doc.Revert();
        Process(doc);
        Assert.Equal("86.96", doc.Amounts());
    }

    [Fact]
    public void Without_the_official_rate_the_documents_are_compared_with_each_other()
    {
        TestSheet Sheet() => Foreign.Invoice(1d, "1", 100, "USD", 86).Invoice(2d, "2", 200, "USD", 172).Invoice(3d, "3", 50, "USD", 50);
        Assert.Equal([false, false, true], Sheet().Process().Documents.Select(d => d.Has(IssueCode.EuroRate)));

        var batch = Sheet().Process(rates: Rates);
        Assert.DoesNotContain(batch.Documents, d => d.Has(IssueCode.EuroRate));   // asked about once, not twice
        Assert.True(batch.Documents[2].Has(IssueCode.RateDiffers));
    }

    [Fact]
    public void The_rows_of_one_invoice_may_be_in_several_currencies()
    {
        var doc = Foreign
            .Invoice(1d, "INV-1", 30, "USD", 26.09m)
            .Invoice(1d, "INV-1", 20, "GBP", 23.53m, ("L", "601")).Single(rates: Rates);
        Assert.Equal(("26.09;23.53", 2, null), (doc.Amounts(), doc.Conversions.Count, doc.TotalInCurrency));
        Assert.False(doc.Has(IssueCode.RateDiffers));
    }

    [Fact]
    public void A_day_without_rates_is_wanted_also_when_the_file_has_the_euro()
    {
        var doc = Foreign.Invoice(1d, "INV-1", 100, "USD", 86.96m).Single();   // to compare with
        Assert.Equal((true, new DateTime(2026, 9, 10)), (doc.WillExport, doc.UnknownRateDay));
    }

    // ----- with their currency in column A: euro (currency|rate|fixing) -----

    private static ConverterSettings With(string account, string currency) => new() { AccountCurrencies = { [account] = currency } };
    private static ConverterSettings EuroOnly(string account = "", string currency = "")
    {
        var settings = new ConverterSettings { CurrencyAmounts = CurrencyAmounts.EuroOnly };
        if (account.Length > 0) settings.AccountCurrencies[account] = currency;
        return settings;
    }

    private static List<string[]> Rows(TestSheet sheet, ConverterSettings? settings = null, IExchangeRates? rates = null, ClientSettings? client = null,
        Action<OperationDocument>? edit = null)
    {
        var options = new ConverterOptions(settings ?? new ConverterSettings(), client);
        var batch = sheet.Read();
        edit?.Invoke(batch.Documents[0]);
        new DocumentProcessor(options, rates ?? Rates).Process(batch);
        return OperationRows.Build(batch, options).Select(r => r.Cells).ToList();
    }

    /// <summary>The amounts of column A, taken apart: euro, currency, rate, fixing.</summary>
    private static List<(decimal Euro, decimal Currency, decimal Rate, decimal Fixing)> Parse(string amounts) => amounts.Split(';').Select(a =>
    {
        var m = Regex.Match(a, @"^(-?\d+(?:\.\d{1,2})?) \((-?\d+(?:\.\d{1,2})?)\|(\d+(?:\.\d+)?)\|(\d+(?:\.\d+)?)\)$");
        Assert.True(m.Success, a);
        decimal Number(int group) => decimal.Parse(m.Groups[group].Value, CultureInfo.InvariantCulture);
        return (Number(1), Number(2), Number(3), Number(4));
    }).ToList();

    [Fact]
    public void An_amount_in_a_currency_carries_it_as_in_the_model()
    {
        // Model.xls: 2000 (1000|2.00|1.95583) – the base currency is the amount times the rate.
        var rows = Rows(Foreign.Invoice(1d, "INV-1", 900, "USD", null));
        Assert.Equal(("782.61 (900|0.86957|0.86957)", "602/1-4011"), (rows[0][0], rows[0][1]));
        Assert.Equal("625 (500|1.25|1.25)", Rows(Sales.Sale(1d, "1", 500, 0, ("AG", 500d), ("T", "GBP")))[0][0]);
        Assert.Equal("-625 (-500|1.25|1.25)", Rows(Sales.Sale(1d, "1", -500, 0, ("AG", -500d), ("T", "GBP"), ("B", "КИ"), ("C", 3d)))[0][0]);
    }

    [Fact]
    public void Every_amount_of_a_document_and_its_payment_carry_it()
    {
        var rows = Rows(Purchases.Purchase(1d, "1", 1000, 200, ("T", "USD"), ("AI", "401/2"), ("AH", "В брой"), ("AJ", "501")));
        Assert.Equal(("800 (1000|0.8|0.8);160 (200|0.8|0.8)", "601/5;453/1-401/2", "2;2"), (rows[0][0], rows[0][1], rows[0][OperationsLayout.DealType]));
        Assert.Equal(("960 (1200|0.8|0.8)", "401/2-501"), (rows[1][0], rows[1][1]));
        Assert.Equal("100;20", Rows(Purchases.Purchase(1d, "1", 100, 20))[0][0]);   // in euro there is nothing to add
    }

    [Fact]
    public void A_credit_note_with_positive_amounts_is_turned_in_both()
    {
        var rows = Rows(Foreign.Invoice(1d, "INV-1", 115, "USD", null, ("B", "КИ"), ("C", 3d)));
        Assert.Equal("-100 (-115|0.86957|0.86957)", rows[0][0]);
    }

    [Fact]
    public void The_rate_is_the_one_of_the_conversion_and_the_fixing_the_official_one()
    {
        var sheet = Purchases.Purchase(1d, "1", 1000, 0, ("T", "USD"), ("W", "0%"));
        Assert.Equal("781.25 (1000|0.78125|0.8)", Rows(sheet, edit: d => d.TrySet(DocField.Rate, "1,28", out _))[0][0]);
        Assert.Equal("833.33 (1000|0.83333|0.83333)", Rows(sheet, edit: d => d.TrySet(DocField.RateDate, "14.06.2026", out _))[0][0]);   // both of that day
        Assert.Equal("500 (1000|0.5|0.5)", Rows(sheet, rates: NoRates.Instance, edit: d => d.TrySet(DocField.Rate, "2", out _))[0][0]);   // no other is known
    }

    [Fact]
    public void The_euro_of_the_file_by_the_official_rate_have_no_rate_of_their_own()
    {
        // 29 / 1.15 = 25.2174: rounded, they come to 0.869655 – which would look like a difference of rates.
        Assert.Equal("25.22 (29|0.86957|0.86957)", Rows(Foreign.Invoice(1d, "INV-1", 29, "USD", 25.22m))[0][0]);
        Assert.Equal("25.23 (29|0.86957|0.86957)", Rows(Foreign.Invoice(1d, "INV-1", 29, "USD", 25.23m))[0][0]);   // within its rounding
    }

    [Fact]
    public void The_euro_of_the_file_by_another_rate_give_the_rate_of_the_operation()
    {
        Assert.Equal("-86.9 (-100|0.869|0.86957)", Rows(Foreign.Invoice(1d, "INV-1", -100, "USD", -86.90m, ("B", "КИ"), ("C", 3d)))[0][0]);

        // Every amount is what its currency comes to by its own rate.
        var several = Foreign.Invoice(1d, "INV-1", 100, "USD", 90).Invoice(1d, "INV-1", 3, "USD", 2.71m, ("L", "601")).Invoice(1d, "INV-1", 50, "USD", 43.48m, ("L", "609"));
        Assert.Equal("90 (100|0.9|0.86957);43.48 (50|0.86957|0.86957);2.71 (3|0.90333|0.86957)", Rows(several)[0][0]);

        // Without rates of any kind there is only the one of the file.
        Assert.Equal("86.27 (100|0.8627|0.8627)", Rows(Foreign.Invoice(1d, "INV-1", 100, "USD", 86.27m), rates: NoRates.Instance)[0][0]);
    }

    [Fact]
    public void The_euro_typed_for_a_row_give_the_rate_of_its_amount_only()
    {
        var sheet = Foreign.Invoice(1d, "INV-1", 100, "USD", 86.96m).Invoice(1d, "INV-1", 50, "USD", 43.48m, ("L", "601"), ("AJ", "В брой"), ("AL", "501"));
        void TypeEuro(OperationDocument d) => Assert.True(d.Lines[1].TrySet(LineField.Euro, "45", out _));
        // Six decimals: with five, 150 USD would be a cent more.
        Assert.Equal("86.96 (100|0.869565|0.869565);45 (50|0.9|0.869565)", Rows(sheet, edit: TypeEuro)[0][0]);

        // Also when the rest is by the rate of another day: USD 1.16.
        Assert.Equal("86.21 (100|0.86207|0.86207);45 (50|0.9|0.86207)", Rows(sheet, edit: d =>
        {
            TypeEuro(d);
            d.TrySet(DocField.RateDate, "13.09.2026", out _);
        })[0][0]);
    }

    [Fact]
    public void A_file_that_contradicts_itself_is_written_in_euro_only()
    {
        var sheet = Foreign.Invoice(1d, "INV-1", -100, "USD", 86.96m);
        Assert.Equal("86.96", Rows(sheet)[0][0]);
        Assert.True(sheet.Single(rates: Rates).Has(IssueCode.RateDiffers));
    }

    [Theory]
    [InlineData("GBP", 0.8437, 640)]
    [InlineData("USD", 1.1357, 123456.78)]
    [InlineData("USD", 1.1357, 9876543.21)]
    [InlineData("JPY", 174.35, 250000)]
    [InlineData("HUF", 373.55, 1999)]
    [InlineData("IDR", 20071.37, 75000000)]
    public void The_rate_has_the_digits_that_give_the_amount_back(string currency, decimal perEuro, decimal amount)
    {
        var rates = new SomeRates().Add(currency, "10.06.2026", perEuro);
        var row = Parse(Rows(Sales.Sale(1d, "1", amount, 0, ("AG", (double)amount), ("T", currency)), rates: rates)[0][0]).Single();
        Assert.Equal((amount, Math.Round(amount / perEuro, 2, MidpointRounding.AwayFromZero)), (row.Currency, row.Euro));
        Assert.Equal((row.Euro, row.Rate), (Math.Round(row.Currency * row.Rate, 2, MidpointRounding.AwayFromZero), row.Fixing));
        Assert.InRange(row.Rate * perEuro, 0.99998m, 1.00002m);
    }

    [Fact]
    public void Any_amounts_come_back_from_what_is_written()
    {
        var random = new Random(20261123);
        for (int i = 0; i < 400; i++)
        {
            decimal perEuro = Math.Round((decimal)Math.Pow(10, random.NextDouble() * 5 - 1), 4) + 0.0001m;
            var rates = new SomeRates().Add("USD", "10.06.2026", perEuro);
            var sheet = Purchases;
            decimal total = 0m;
            int rows = random.Next(1, 5);
            for (int r = 0; r < rows; r++)
            {
                decimal amount = Math.Round((decimal)Math.Pow(10, random.NextDouble() * 6), 2) * (random.Next(8) == 0 ? -1 : 1);
                total += amount;
                sheet = sheet.Purchase(1d, "1", amount, 0, ("T", "USD"), ("W", "0%"), ("K", "60" + r), ("M", "-"));
            }

            string cell = Rows(sheet, rates: rates)[0][0];
            var written = Parse(cell);
            Assert.Equal(total, written.Sum(w => w.Currency));
            Assert.Single(written.Select(w => (w.Rate, w.Fixing)).Distinct());
            Assert.Equal(Math.Round(total / perEuro, 2, MidpointRounding.AwayFromZero), written.Sum(w => w.Euro));
            foreach (var w in written)   // a cent may go to another amount, so that they add up
                Assert.InRange(Math.Abs(Math.Round(w.Currency * w.Rate, 2, MidpointRounding.AwayFromZero) - w.Euro), 0m, 0.01m);
        }
    }

    [Fact]
    public void Leva_are_only_another_way_to_say_euro()
    {
        Assert.Equal("511.29", Rows(Purchases.Purchase(1d, "1", 1000, 0, ("T", "BGN"), ("W", "0%")))[0][0]);
        Assert.Equal("511.29 (1000|0.51129|0.51129)", Rows(Purchases.Purchase(1d, "1", 1000, 0, ("T", "BGN"), ("W", "0%")), With("401", "BGN"))[0][0]);
    }

    [Fact]
    public void A_document_in_several_currencies_is_in_euro_only_and_it_is_said()
    {
        var sheet = Foreign.Invoice(1d, "INV-1", 30, "USD", 26.09m).Invoice(1d, "INV-1", 20, "GBP", 23.53m, ("L", "601"));
        Assert.Equal("26.09;23.53", Rows(sheet)[0][0]);
        Assert.Contains("в няколко валути (USD, GBP)", Message(sheet.Single(rates: Rates), IssueCode.AccountCurrency));
        Assert.True(sheet.Single(rates: Rates).WillExport);
        Assert.False(sheet.Single(EuroOnly(), rates: Rates).Has(IssueCode.AccountCurrency));

        var partly = Purchases.Purchase(1d, "1", 1000, 0, ("T", "USD"), ("W", "0%")).Purchase(1d, "1", 500, 0, ("W", "0%"), ("K", "602"), ("M", "-"));
        Assert.Equal("800;500", Rows(partly)[0][0]);
        Assert.Contains("USD, EUR", Message(partly.Single(rates: Rates), IssueCode.AccountCurrency));
    }

    [Fact]
    public void With_the_setting_everything_is_in_euro_only_except_the_marked_accounts()
    {
        var sheet = Purchases.Purchase(1d, "1", 1000, 200, ("T", "USD"), ("AH", "В брой"), ("AJ", "501"));
        Assert.Equal(["800;160", "960"], Rows(sheet, EuroOnly()).Select(r => r[0]));
        Assert.Equal(["800 (1000|0.8|0.8);160 (200|0.8|0.8)", "960 (1200|0.8|0.8)"], Rows(sheet, EuroOnly("401", "USD")).Select(r => r[0]));
        Assert.Equal(["800;160", "960 (1200|0.8|0.8)"], Rows(sheet, EuroOnly("501", "USD")).Select(r => r[0]));
        Assert.Equal(["800 (1000|0.8|0.8);160", "960"], Rows(sheet, EuroOnly("601/5", "USD")).Select(r => r[0]));   // VAT has none of its own
    }

    [Fact]
    public void An_account_marked_with_the_euro_is_an_ordinary_one()
    {
        var sheet = Purchases.Purchase(1d, "1", 1000, 200, ("T", "USD"));
        Assert.Equal("800;160", Rows(sheet, With("401", "EUR"))[0][0]);
        Assert.False(sheet.Single(With("401", "EUR"), rates: Rates).Has(IssueCode.AccountCurrency));

        var client = new ClientSettings { AccountCurrencies = { ["401"] = "EUR", ["402"] = "GBP" } };
        Assert.Equal("800;160", Rows(sheet, client: client)[0][0]);
        var options = new ConverterOptions(With("401", "USD"), client);
        Assert.Equal(("EUR", "GBP", ""), (options.AccountCurrency("401", ""), options.AccountCurrency("402", ""), options.AccountCurrency("403", "")));
        Assert.Equal("USD", new ConverterOptions(With("401", "USD")).AccountCurrency("401", "7"));   // a sub-account of it
    }

    [Fact]
    public void A_currency_account_on_the_other_side_carries_only_its_own_amount()
    {
        var sheet = Purchases
            .Purchase(1d, "1", 1000, 0, ("T", "USD"), ("W", "0%"), ("K", "504"), ("M", "1"))
            .Purchase(1d, "1", 500, 0, ("T", "USD"), ("W", "0%"), ("K", "602"), ("M", "-"));
        var settings = With("401", "EUR");
        settings.AccountCurrencies["504/1"] = "USD";
        Assert.Equal("800 (1000|0.8|0.8);400", Rows(sheet, settings)[0][0]);
    }

    [Fact]
    public void An_account_in_another_currency_is_pointed_out_and_written_in_euro()
    {
        var sheet = Purchases.Purchase(1d, "1", 1000, 200, ("T", "USD"));
        Assert.Equal("800;160", Rows(sheet, With("401", "GBP"))[0][0]);
        var doc = sheet.Single(With("401", "GBP"), rates: Rates);
        Assert.Contains("Сметка 401 е във валута GBP, а документът е в USD", Message(doc, IssueCode.AccountCurrency));
        Assert.Contains("документът е в EUR", Message(Purchases.Purchase(1d, "1", 1000, 200).Single(With("401", "GBP")), IssueCode.AccountCurrency));
        Assert.False(sheet.Single(With("401", "USD"), rates: Rates).Has(IssueCode.AccountCurrency));
    }

    [Fact]
    public void Without_a_cash_account_in_the_file_the_one_of_the_client_is_used()
    {
        var sheet = Purchases.Purchase(1d, "1", 100, 20, ("AH", "В брой"));
        Assert.True(sheet.Single().Has(IssueCode.MissingCashAccount));
        var doc = sheet.Single(new ConverterSettings { DefaultCashAccount = "501" }, new ClientSettings { CashAccount = "501/7" });
        Assert.Equal("501/7", doc.CashAccountOut);
    }
}
