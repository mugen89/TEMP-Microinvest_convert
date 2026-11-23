using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Nomenclature;
using MicroinvestConvert.Core.Rules;
using MicroinvestConvert.Core.Settings;
using MicroinvestConvert.Core.Values;

namespace MicroinvestConvert.Tests;

public class CellValuesTests
{
    [Theory]
    [InlineData("245.65", 245.65)]
    [InlineData("245,65", 245.65)]
    [InlineData("1 234,56", 1234.56)]
    [InlineData("1 234,56", 1234.56)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1.234.567", 1234567)]
    [InlineData("-4.79", -4.79)]
    [InlineData("−4.79", -4.79)]
    [InlineData("(4.79)", -4.79)]
    [InlineData("4.79 лв.", 4.79)]
    [InlineData("0", 0)]
    public void Number_reads_every_spelling(string text, double expected) =>
        Assert.Equal((decimal)expected, CellValues.Number(text));

    [Theory]
    [InlineData("-")]
    [InlineData("–")]
    [InlineData("")]
    [InlineData("няма")]
    public void Number_is_null_when_there_is_none(string text) => Assert.Null(CellValues.Number(text));

    [Fact]
    public void A_double_loses_its_binary_noise() => Assert.Equal(0.3m, CellValues.Number(0.1 + 0.2));

    [Fact]
    public void Text_of_a_whole_number_has_no_decimals()
    {
        Assert.Equal("601", CellValues.Text(601d));
        Assert.Equal("1000001234", CellValues.Text(1000001234d));
        Assert.Equal("4.5", CellValues.Text(4.5d));
        Assert.Equal("", CellValues.Norm("-"));
        Assert.Equal("a b", CellValues.Text("  a\r\n  b​ "));
    }

    [Theory]
    [InlineData("10.06.2026")]
    [InlineData("10/06/2026")]
    [InlineData("10.6.26")]
    [InlineData("2026-06-10")]
    [InlineData("10.06.2026 г.")]
    [InlineData("10.06.2026г.")]
    [InlineData("2026-06-10T14:30:00")]
    [InlineData("10.06.2026 14:30")]
    public void Date_reads_every_spelling(string text) => Assert.Equal(new DateTime(2026, 6, 10), CellValues.Date(text));

    [Fact]
    public void Date_reads_date_cells_and_serials()
    {
        Assert.Equal(new DateTime(2026, 6, 10), CellValues.Date(new DateTime(2026, 6, 10, 14, 30, 0)));
        Assert.Equal(new DateTime(2024, 1, 9), CellValues.Date(45300d));
        Assert.Null(CellValues.Date("31.02.2026"));
        Assert.Null(CellValues.Date("7"));
        Assert.Null(CellValues.Date(0.5d));
    }

    [Theory]
    [InlineData("06.2026", 2026, 6)]
    [InlineData("6/2026", 2026, 6)]
    [InlineData("2026-06", 2026, 6)]
    [InlineData("12.06.2026", 2026, 6)]
    public void Period_reads_text(string text, int year, int month) =>
        Assert.Equal(new DateTime(year, month, 1), CellValues.Period(text));

    [Theory]
    [InlineData(6.2026, 2026, 6)]
    [InlineData(10.2026, 2026, 10)]
    [InlineData(12.203, 2030, 12)]   // 12.2030 loses its last zero as a number
    public void Period_reads_the_number_Excel_made_of_it(double number, int year, int month) =>
        Assert.Equal(new DateTime(year, month, 1), CellValues.Period(number));

    [Fact]
    public void Period_of_a_date_is_its_month() =>
        Assert.Equal(new DateTime(2026, 6, 1), CellValues.Period(new DateTime(2026, 6, 17)));

    [Theory]
    [InlineData("13.2026")]
    [InlineData("-")]
    [InlineData("юни")]
    public void Period_is_null_when_there_is_none(string text) => Assert.Null(CellValues.Period(text));
}

public class NomenclatureTests
{
    [Fact]
    public void Deal_types_are_the_18_of_the_model()
    {
        Assert.Equal(Enumerable.Range(1, 18), DealTypes.All.Select(d => d.Code));
        Assert.Equal("2 - Получени доставки, ВОП с право на ПДК", DealTypes.Get(2).Label);
        Assert.Equal("16 - Дистанционни продажби по чл. 69, ал. 2", DealTypes.Get(16).Label);
        Assert.Throws<ArgumentOutOfRangeException>(() => DealTypes.Get(19));
    }

    [Fact]
    public void Vies_goes_with_the_deal_type()
    {
        Assert.Equal(2, DealTypes.Get(13).Vies);
        Assert.Equal(4, DealTypes.Get(15).Vies);
        Assert.Equal(3, DealTypes.Get(18).Vies);
        Assert.All(DealTypes.All.Where(d => d.Code is not (13 or 15 or 18)), d => Assert.Equal(1, d.Vies));
    }

    [Fact]
    public void Ledger_columns_are_as_documented()
    {
        var expected = new Dictionary<int, string>
        {
            [2] = "10; 11", [3] = "9", [4] = "12; 13", [5] = "14", [6] = "15",
            [7] = "11; 12", [8] = "13; 15", [9] = "14; 15", [10] = "16", [11] = "17; 18", [12] = "19",
            [13] = "20", [14] = "21", [15] = "22", [16] = "23", [17] = "24", [18] = "25",
        };
        foreach (var (code, columns) in expected) Assert.Equal(columns, DealTypes.Get(code).LedgerColumns);
    }

    [Fact]
    public void A_purchase_offers_1_to_6_and_a_sale_1_and_7_to_18()
    {
        Assert.Equal([1, 2, 3, 4, 5, 6], DealTypes.ChoicesFor(DealSide.Purchase).Select(d => d.Code));
        Assert.Equal([1, .. Enumerable.Range(7, 12)], DealTypes.ChoicesFor(DealSide.Sale).Select(d => d.Code));
    }

    [Fact]
    public void Document_types_are_the_39_of_the_model_without_repeats()
    {
        Assert.Equal(39, DocumentTypes.All.Count);
        Assert.Equal(39, DocumentTypes.All.Select(t => t.ShortName).Distinct().Count());
        Assert.Equal(["Ф-ра", "ДИ", "КИ"], DocumentTypes.All.Take(3).Select(t => t.ShortName));
    }

    [Theory]
    [InlineData("Фактура", null, "Ф-ра")]
    [InlineData("фактура", 1, "Ф-ра")]
    [InlineData("КИ", 3, "КИ")]
    [InlineData("ди", null, "ДИ")]
    [InlineData("Кредитно известие", null, "КИ")]
    [InlineData("Митническа декларация", null, "МД")]
    [InlineData("", 3, "КИ")]
    [InlineData("нещо друго", 2, "ДИ")]
    [InlineData("нещо друго", 9, "")]
    [InlineData("", null, "")]
    public void The_type_comes_from_the_text_and_then_from_the_code(string text, int? code, string expected) =>
        Assert.Equal(expected, DocumentTypes.FromSource(text, code));

    [Theory]
    [InlineData("В брой", PaymentMethod.Cash)]
    [InlineData("По сметка - банкова", PaymentMethod.Bank)]
    [InlineData("С карта - банкова", PaymentMethod.Card)]
    [InlineData("наложен платеж", PaymentMethod.Bank)]
    [InlineData("", PaymentMethod.Unknown)]
    public void Payment_is_read_from_its_text(string text, PaymentMethod expected) =>
        Assert.Equal(expected, PaymentMethods.FromText(text));
}

public class PartnerIdentityTests
{
    [Theory]
    [InlineData("BG123456786", "123456786", "BG123456786")]
    [InlineData("bg 123456786", "123456786", "BG123456786")]
    [InlineData("ВG123456786", "123456786", "BG123456786")]   // Cyrillic В
    [InlineData("123456786", "123456786", "")]
    [InlineData("1234567890", "1234567890", "")]
    [InlineData("DE123456789", "", "DE123456789")]
    [InlineData("ATU12345678", "", "ATU12345678")]
    [InlineData("NL123456789B01", "", "NL123456789B01")]
    [InlineData("999999999999999", "999999999999999", "")]
    [InlineData("999999999999999TEST", "999999999999999", "")]
    [InlineData("", "", "")]
    [InlineData(null, "", "")]
    public void The_cell_splits_into_eik_and_vat_number(string? raw, string eik, string vat) =>
        Assert.Equal(new PartnerIdentity(eik, vat), PartnerIdentity.Parse(raw));

    [Theory]
    [InlineData("DE123456789", true, true)]
    [InlineData("EL123456789", true, true)]
    [InlineData("XI123456789", true, true)]
    [InlineData("GB123456789", true, false)]
    [InlineData("BG123456786", false, false)]
    [InlineData("123456786", false, false)]
    public void Vies_is_for_the_other_member_states(string raw, bool foreign, bool eu)
    {
        var id = PartnerIdentity.Parse(raw);
        Assert.Equal(foreign, id.IsForeign);
        Assert.Equal(eu, id.IsOtherEuMember);
    }

    [Theory]
    [InlineData("123456786", true)]
    [InlineData("123456787", false)]
    [InlineData("1234567890123", true)]    // a branch: not judged
    [InlineData("1234567890", true)]       // ЕГН: not judged
    [InlineData("999999999", true)]        // a placeholder
    public void The_check_digit_of_a_9_digit_eik(string eik, bool valid) =>
        Assert.Equal(valid, PartnerIdentity.HasValidEikCheckDigit(eik));
}

public class AccountCodesTests
{
    private static string Format(string account, string analytic, Action<ConverterSettings>? change = null, ClientSettings? client = null)
    {
        var settings = new ConverterSettings();
        change?.Invoke(settings);
        return AccountCodes.Format(account, analytic, new ConverterOptions(settings, client));
    }

    [Theory]
    [InlineData("601", "5", "601/5")]
    [InlineData("401", "", "401")]
    [InlineData("4111", "", "4111")]          // the digits are never split
    [InlineData("4111", "1", "4111/1")]
    [InlineData("411/1", "", "411/1")]
    [InlineData("411/1", "1", "411/1/1")]     // the sub-account is always added, even when it repeats
    [InlineData("7012", "81", "7012/81")]
    [InlineData("453/1", "", "453/1")]
    [InlineData("401/1", "5/2", "401/1/5/2")]
    [InlineData("", "5", "")]
    public void Main_account_then_sub_accounts_after_a_slash(string account, string analytic, string expected) =>
        Assert.Equal(expected, Format(account, analytic));

    [Theory]
    [InlineData("411/", "1", "411/1")]
    [InlineData("411/1/", "", "411/1")]
    [InlineData("/411/1", "/1/", "411/1/1")]
    [InlineData("411 / 1", " 1 ", "411/1/1")]
    [InlineData("411//1", "", "411/1")]
    [InlineData("411/1", "-", "411/1")]
    [InlineData("-", "1", "")]
    [InlineData("/", "1", "")]
    public void The_slashes_people_type_are_cleaned(string account, string analytic, string expected) =>
        Assert.Equal(expected, Format(account, analytic));

    [Theory]
    [InlineData("411 1")]      // 4111 or 411/1?
    [InlineData("411.1")]
    [InlineData("411-1")]
    [InlineData("411\\1")]
    public void What_could_mean_two_things_is_reported_and_not_guessed(string account)
    {
        Assert.Equal(account, Format(account, ""));
        Assert.NotNull(AccountCodes.Problem(Format(account, "")));
    }

    [Fact]
    public void A_mapping_of_the_whole_account_wins() =>
        Assert.Equal("609/9", Format("601", "5", s => s.Accounts["601/5"] = "609/9"));

    [Fact]
    public void A_mapping_of_the_main_account_keeps_the_analytic() =>
        Assert.Equal("602/7/5", Format("601", "5", s => s.Accounts["601"] = "602/7"));

    [Fact]
    public void The_mapping_of_the_client_wins_over_the_common_one()
    {
        var client = new ClientSettings { Accounts = { ["401"] = "401/2" } };
        Assert.Equal("401/2", Format("401", "", s => s.Accounts["401"] = "401/1", client));
    }

    [Theory]
    [InlineData("601/5", true)]
    [InlineData("601;5", false)]
    [InlineData("601-5", false)]
    [InlineData("602%", false)]
    [InlineData("601 5", false)]
    [InlineData("601/", false)]
    [InlineData("601//5", false)]
    [InlineData("1/2/3/4/5/6", true)]
    [InlineData("1/2/3/4/5/6/7", false)]
    [InlineData("", false)]
    public void Accounts_that_would_break_columns_A_and_B_are_refused(string account, bool fine) =>
        Assert.Equal(fine, AccountCodes.Problem(account) == null);
}

public class AccountingDateTests
{
    private static readonly DateTime June = new(2026, 6, 1);

    [Fact]
    public void A_document_before_the_period_is_booked_on_its_first_day() =>
        Assert.Equal(new AccountingDate(June, false), AccountingDates.Resolve(new DateTime(2026, 3, 17), June));

    [Fact]
    public void Within_the_period_the_date_is_never_before_the_document() =>
        Assert.Equal(new AccountingDate(new DateTime(2026, 6, 24), false), AccountingDates.Resolve(new DateTime(2026, 6, 24), June));

    [Fact]
    public void A_document_after_the_period_is_a_violation() =>
        Assert.Equal(new AccountingDate(new DateTime(2026, 7, 2), true), AccountingDates.Resolve(new DateTime(2026, 7, 2), June));

    [Fact]
    public void Without_a_period_the_date_is_the_document_date() =>
        Assert.Equal(new AccountingDate(new DateTime(2026, 7, 2), false), AccountingDates.Resolve(new DateTime(2026, 7, 2), null));
}

public class VatRatesTests
{
    private static VatMatch Match(decimal @base, decimal vat, decimal? declared = null, decimal tolerance = 0.05m,
        VatRounding rounding = VatRounding.HalfUp, bool only = false) =>
        new VatRates(new ConverterSettings { Rounding = rounding }).Match(@base, vat, declared, tolerance, only);

    [Theory]
    [InlineData(245.65, 49.13, 20)]
    [InlineData(55.10, 4.96, 9)]
    [InlineData(-4.80, -0.96, 20)]
    [InlineData(0.02, 0.00, 0)]
    public void Exact_amounts_give_their_rate(double @base, double vat, double rate)
    {
        var match = Match((decimal)@base, (decimal)vat);
        Assert.Equal((decimal)rate, match.Rate);
        Assert.Contains(match.Status, new[] { VatStatus.Exact, VatStatus.Zero });
    }

    [Theory]
    [InlineData(17.63, 3.52)]      // 3.526 → 3.53
    [InlineData(312.47, 62.47)]    // 62.494 → 62.49
    public void Cents_of_difference_are_accepted(double @base, double vat)
    {
        var match = Match((decimal)@base, (decimal)vat, 20m);
        Assert.Equal(VatStatus.WithinTolerance, match.Status);
        Assert.Equal(20m, match.Rate);
    }

    [Fact]
    public void The_allowed_difference_decides()
    {
        Assert.Equal(VatStatus.WithinTolerance, Match(312.47m, 62.47m, 20m, 0.02m).Status);
        Assert.Equal(VatStatus.Unresolved, Match(312.47m, 62.47m, 20m, 0.01m).Status);
        Assert.Equal(VatStatus.Unresolved, Match(150.55m, 30.01m, 20m).Status);
    }

    [Fact]
    public void The_rounding_decides()
    {
        // 0.125 × 20% = 0.025
        Assert.Equal(0.03m, new VatRates(new ConverterSettings()).Expected(0.125m, 20m));
        Assert.Equal(0.02m, new VatRates(new ConverterSettings { Rounding = VatRounding.HalfEven }).Expected(0.125m, 20m));
        Assert.Equal(0.02m, new VatRates(new ConverterSettings { Rounding = VatRounding.Truncate }).Expected(0.125m, 20m));
        Assert.Equal(VatStatus.Exact, Match(17.63m, 3.52m, 20m, 0m, VatRounding.Truncate).Status);
    }

    [Fact]
    public void An_exact_rate_wins_over_a_near_declared_one()
    {
        // 0.30: 9% is 0.03 exactly, 20% is 0.06 — three cents away
        var match = Match(0.30m, 0.03m, 20m);
        Assert.Equal((VatStatus.Exact, 9m), (match.Status, match.Rate!.Value));
    }

    [Fact]
    public void No_vat_is_zero_even_when_a_rate_is_declared()
    {
        var match = Match(600m, 0m, 20m);
        Assert.Equal((VatStatus.Zero, 0m, 120m), (match.Status, match.Rate!.Value, match.Expected));
    }

    [Fact]
    public void A_base_of_two_cents_has_no_vat_at_any_rate() =>
        Assert.Equal((VatStatus.Exact, (decimal?)20m), (Match(0.02m, 0m, 20m).Status, Match(0.02m, 0m, 20m).Rate));

    [Theory]
    [InlineData(0, 150.00)]       // VAT without a base
    [InlineData(100, -20)]        // opposite signs
    [InlineData(100, 19)]         // a German rate
    [InlineData(480.00, 90.00)]   // a part without VAT
    public void What_fits_no_rate_is_unresolved(double @base, double vat) =>
        Assert.Equal(VatStatus.Unresolved, Match((decimal)@base, (decimal)vat, 20m).Status);

    [Fact]
    public void A_foreign_declared_rate_is_not_a_Bulgarian_one() =>
        Assert.Equal(VatStatus.Unresolved, Match(100m, 19m, 19m).Status);

    [Fact]
    public void Nothing_at_all_is_empty() => Assert.Equal(VatStatus.Empty, Match(0m, 0m, 20m).Status);

    [Fact]
    public void Only_the_declared_rate_when_asked_so() =>
        Assert.Equal(VatStatus.Unresolved, Match(100m, 9m, 20m, only: true).Status);

    [Theory]
    [InlineData("20%", 20)]
    [InlineData("9 %", 9)]
    [InlineData("0%", 0)]
    [InlineData("Износ – 0%", 0)]
    [InlineData("необлагаема", 0)]
    [InlineData("освободена", 0)]
    [InlineData("ВОД", 0)]
    [InlineData("Необл. извън ЕС ", 0)]
    [InlineData("20", 20)]
    public void Rate_texts(string text, double expected) => Assert.Equal((decimal)expected, VatRates.ParseRate(text));

    [Fact]
    public void Rate_numbers()
    {
        Assert.Equal(20m, VatRates.ParseRate(0.2d));
        Assert.Equal(9m, VatRates.ParseRate(9d));
        Assert.Null(VatRates.ParseRate("-"));
        Assert.Null(VatRates.ParseRate(null));
    }
}
