using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Nomenclature;
using MicroinvestConvert.Core.Output;
using MicroinvestConvert.Core.Rules;
using MicroinvestConvert.Core.Settings;
using MicroinvestConvert.Core.Source;
using static MicroinvestConvert.Tests.TestSheet;

namespace MicroinvestConvert.Tests;

public class OperationRowsTests
{
    private static List<OperationRow> Rows(TestSheet sheet, ConverterSettings? settings = null)
    {
        var options = new ConverterOptions(settings ?? new ConverterSettings());
        var batch = sheet.Read();
        new DocumentProcessor(options).Process(batch);
        return OperationRows.Build(batch, options);
    }

    private static string Cell(OperationRow row, string column) => row.Cells[IndexOf(column)];

    [Fact]
    public void A_purchase_row_cell_by_cell()
    {
        var row = Assert.Single(Rows(new TestSheet(Layout.Purchases).Purchase(1d, "0000104321", 245.65m, 49.13m, ("AO", "бележка"))));

        Assert.Equal("245.65;49.13", Cell(row, "A"));
        Assert.Equal("601/5;453/1-401", Cell(row, "B"));
        Assert.Equal("2026-06-10", Cell(row, "C"));
        Assert.Equal("Тест Партньор ООД", Cell(row, "D"));
        Assert.Equal(SupplierEik, Cell(row, "E"));
        Assert.Equal(SupplierVat, Cell(row, "F"));
        Assert.Equal("", Cell(row, "G"));
        Assert.Equal("0000104321", Cell(row, "H"));
        Assert.Equal("Ф-ра", Cell(row, "I"));
        Assert.Equal("2026-06-10", Cell(row, "J"));
        Assert.Equal("Разходи за материали", Cell(row, "K"));
        Assert.Equal("бележка", Cell(row, "L"));
        Assert.Equal("", Cell(row, "M"));
        Assert.Equal("2;2", Cell(row, "N"));
        Assert.Equal("", Cell(row, "O"));             // VIES is for sales only
        Assert.Equal("2026-06-10", Cell(row, "P"));   // P = C
        Assert.All(row.Cells.Skip(OperationsLayout.FirstProtocolColumn), c => Assert.Equal("", c));
    }

    [Fact]
    public void A_sale_row_has_one_debit_and_many_credits()
    {
        var row = Assert.Single(Rows(new TestSheet(Layout.Sales).Sale(1d, "0000000001", 375, 75, ("AB", 375d), ("AC", 75d), ("AM", "4111"))));
        Assert.Equal("375;75", Cell(row, "A"));
        Assert.Equal("411/1-703;453/2", Cell(row, "B"));
        Assert.Equal("7;7", Cell(row, "N"));
        Assert.Equal("1", Cell(row, "O"));
        Assert.Equal("Продажба на услуги", Cell(row, "K"));
    }

    [Fact]
    public void The_posting_date_follows_the_period_and_P_follows_it()
    {
        var row = Assert.Single(Rows(new TestSheet(Layout.Purchases).Purchase(1d, "1", 100, 20, ("E", "17.03.2026"))));
        Assert.Equal(("2026-06-01", "2026-03-17", "2026-06-01"), (Cell(row, "C"), Cell(row, "J"), Cell(row, "P")));
    }

    [Fact]
    public void A_purchase_in_cash_has_a_second_row()
    {
        var rows = Rows(new TestSheet(Layout.Purchases)
            .Purchase(1d, "0001112223", -4.80m, -0.96m, ("B", "КИ"), ("C", 3d), ("AH", "В брой"), ("AJ", "501"), ("AN", "Разплащане")));

        Assert.Equal(2, rows.Count);
        var cash = rows[1];
        Assert.True(cash.IsCashRow);
        Assert.Equal(("-5.76", "401-501"), (Cell(cash, "A"), Cell(cash, "B")));
        Assert.Equal(("РКО-0001112223", "РКО"), (Cell(cash, "H"), Cell(cash, "I")));
        Assert.Equal(("Разплащане", "1", ""), (Cell(cash, "K"), Cell(cash, "N"), Cell(cash, "O")));
    }

    [Fact]
    public void A_sale_in_cash_is_received_with_a_PKO()
    {
        var rows = Rows(new TestSheet(Layout.Sales)
            .Sale(1d, "0000000001", 100, 20, ("AB", 100d), ("AC", 20d), ("AL", "В брой"), ("AN", "501")));

        var cash = rows[1];
        Assert.Equal(("120", "501-411"), (Cell(cash, "A"), Cell(cash, "B")));
        Assert.Equal(("ПКО-0000000001", "ПКО"), (Cell(cash, "H"), Cell(cash, "I")));
        Assert.Equal(("Плащане по ф-ра 0000000001", "1", "1"), (Cell(cash, "K"), Cell(cash, "N"), Cell(cash, "O")));
    }

    [Fact]
    public void Cash_rows_can_be_switched_off() =>
        Assert.Single(Rows(new TestSheet(Layout.Purchases).Purchase(1d, "1", 100, 20, ("AH", "В брой")), new ConverterSettings { CashRows = false }));

    [Fact]
    public void One_deal_type_for_equal_amounts_when_chosen()
    {
        var settings = new ConverterSettings { DealTypeFormat = DealTypeFormat.SingleWhenEqual };
        Assert.Equal("2", Cell(Rows(new TestSheet(Layout.Purchases).Purchase(1d, "1", 100, 20), settings)[0], "N"));

        var mixed = new TestSheet(Layout.Purchases)
            .Purchase(1d, "1", 100, 20)
            .Purchase(1d, "1", 30, 0, ("W", "0%"), ("K", "609"), ("M", "-"));
        Assert.Equal("2;2;3", Cell(Rows(mixed, settings)[0], "N"));
    }

    [Fact]
    public void Vies_is_one_value_unless_the_amounts_differ()
    {
        var mixed = new TestSheet(Layout.Sales)
            .Sale(1d, "1", 100, 0, ("AF", 100d), ("J", "DE123456789"))
            .Sale(1d, "1", 50, 0, ("AI", 50d), ("K", "704"), ("J", "DE123456789"));
        Assert.Equal("2;4", Cell(Rows(mixed)[0], "O"));
    }

    [Fact]
    public void Documents_with_errors_or_unticked_are_left_out()
    {
        var options = ConverterOptions.Default;
        var batch = new TestSheet(Layout.Purchases)
            .Purchase(1d, "0000000001", 100, 20)
            .Purchase(2d, "0000000002", 100, 20, ("T", "USD"))
            .Purchase(3d, "0000000003", 100, 20)
            .Read();
        new DocumentProcessor(options).Process(batch);
        batch.Documents[2].Export = false;
        Assert.Equal(["0000000001"], OperationRows.Build(batch, options).Select(r => Cell(r, "H")));
    }

    [Theory]
    [InlineData(1000, "1000")]
    [InlineData(4.5, "4.5")]
    [InlineData(-0.96, "-0.96")]
    [InlineData(0, "0")]
    public void Amounts_have_a_dot_and_no_grouping(double value, string expected) =>
        Assert.Equal(expected, OperationRows.Amount((decimal)value));
}

public class DeltaWorkbookTests
{
    private static List<OperationRow> SomeRows()
    {
        var options = ConverterOptions.Default;
        var batch = new TestSheet(Layout.Purchases)
            .Purchase(1d, "0000000001", 245.65m, 49.13m)
            .Purchase(2d, "0000000002", 100, 20, ("AH", "В брой"), ("AJ", "501"))
            .Read();
        new DocumentProcessor(options).Process(batch);
        return OperationRows.Build(batch, options);
    }

    [Fact]
    public void The_workbook_is_written_and_reads_back_the_same()
    {
        var rows = SomeRows();
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xls");
        try
        {
            DeltaWorkbook.Write(rows, path);   // verifies itself

            var sheets = WorkbookLoader.Load(path);
            Assert.Equal(["Операции", "Настройка Операции"], sheets.Select(s => s.Name));
            Assert.Equal(OperationsLayout.Columns.Select(c => c.Header), sheets[0].Rows[0].Cast<string>());
            Assert.Equal("601/5;453/1-401", sheets[0].Cell(1, 1));
            Assert.Equal("0000000001", sheets[0].Cell(1, 7));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void The_settings_sheet_says_where_everything_is()
    {
        var sheets = WorkbookLoader.Load(DeltaWorkbook.Build(SomeRows()));
        var settings = sheets[1];
        Assert.Equal("Настройка", settings.Cell(0, 0));
        Assert.Equal(("Sheet с данни", "Операции"), (settings.Cell(1, 0), settings.Cell(1, 1)));
        Assert.Equal(("Начален ред", 2d), (settings.Cell(2, 0), settings.Cell(2, 1)));
        Assert.Equal(("Краен ред", 4d), (settings.Cell(3, 0), settings.Cell(3, 1)));
        Assert.Equal(("Колона за суми", "A"), (settings.Cell(4, 0), settings.Cell(4, 1)));
        Assert.Equal(("Колона за вид сделка", "N"), (settings.Cell(17, 0), settings.Cell(17, 1)));
        Assert.Equal(("Сметка за разход", "AA"), (settings.Cell(30, 0), settings.Cell(30, 1)));
        Assert.Equal(31, settings.Rows.Count);
    }

    [Fact]
    public void Nothing_to_write_is_refused() =>
        Assert.Throws<ConvertException>(() => DeltaWorkbook.Build([]));

    [Fact]
    public void A_locked_target_is_reported_and_leaves_no_rubbish()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xls");
        try
        {
            using (new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                Assert.Throws<ConvertException>(() => DeltaWorkbook.Write(SomeRows(), path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void The_layout_is_the_one_of_the_model()
    {
        Assert.Equal(27, OperationsLayout.Columns.Count);
        for (int i = 0; i < 27; i++)
            Assert.Equal(i, IndexOf(OperationsLayout.Columns[i].Letter));
        Assert.Equal("Вид сделка", OperationsLayout.Columns[OperationsLayout.DealType].Header);
        Assert.Equal("VIES", OperationsLayout.Columns[OperationsLayout.Vies].Header);
        Assert.Equal("Месец за експорт", OperationsLayout.Columns[OperationsLayout.ExportMonth].Header);
    }
}

public class SettingsTests
{
    private static string TempFile() => Path.Combine(Path.GetTempPath(), "mic-" + Guid.NewGuid(), "settings.json");

    [Fact]
    public void Settings_survive_a_restart()
    {
        string path = TempFile();
        var file = new SettingsFile();
        file.General.PurchaseVatAccount = "453/7";
        file.General.Rounding = VatRounding.Truncate;
        file.General.Accounts["401"] = "401/1";
        var client = file.ClientFor("987654326", "Тестова Фирма ЕООД")!;
        client.SaleVatAccount = "453/2/1";
        client.Accounts["601/5"] = "601/9";
        file.AddRecent(@"C:\a.xlsx");
        new SettingsStore(path).Save(file);

        var loaded = new SettingsStore(path).Load();
        var options = loaded.OptionsFor("987654326");
        Assert.Equal(("453/7", "453/2/1"), (options.PurchaseVatAccount, options.SaleVatAccount));
        Assert.Equal(("401/1", "601/9"), (options.MappedAccount("401"), options.MappedAccount("601/5")));
        Assert.Equal(VatRounding.Truncate, loaded.General.Rounding);
        Assert.Equal([@"C:\a.xlsx"], loaded.RecentFiles);
        Assert.True(options.HasVatAccountException);
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }

    [Fact]
    public void A_client_without_exceptions_is_like_everybody()
    {
        var options = new SettingsFile().OptionsFor("000000000");
        Assert.Equal(("453/1", "453/2"), (options.PurchaseVatAccount, options.SaleVatAccount));
        Assert.False(options.HasVatAccountException);
    }

    [Fact]
    public void Empty_exceptions_are_not_kept()
    {
        var file = new SettingsFile();
        file.ClientFor("987654326", "Фирма");
        string path = TempFile();
        new SettingsStore(path).Save(file);
        Assert.Empty(new SettingsStore(path).Load().Clients);
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }

    [Theory]
    [InlineData("{ това не е json")]
    [InlineData("{\"general\":{\"rounding\":\"Nonsense\"}}")]
    public void A_damaged_file_gives_the_defaults_and_is_kept_aside(string content)
    {
        string path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        var store = new SettingsStore(path);
        Assert.Equal("453/1", store.Load().General.PurchaseVatAccount);
        Assert.NotEqual("", store.LoadProblem);
        Assert.True(File.Exists(path + ".bad"));
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }

    [Fact]
    public void Nulls_and_blanks_in_the_file_are_repaired()
    {
        string path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{\"general\":{\"purchaseVatAccount\":\" \",\"accounts\":null,\"tolerancePerDocument\":-3},\"clients\":{\"1\":null},\"recentFiles\":null}");
        var file = new SettingsStore(path).Load();
        Assert.Equal(("453/1", 0m), (file.General.PurchaseVatAccount, file.General.TolerancePerDocument));
        Assert.Empty(file.Clients);
        Assert.Empty(file.RecentFiles);
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }

    [Fact]
    public void Recent_files_are_unique_and_few()
    {
        var file = new SettingsFile();
        for (int i = 0; i < 12; i++) file.AddRecent($@"C:\{i}.xlsx");
        file.AddRecent(@"c:\5.XLSX");
        Assert.Equal(8, file.RecentFiles.Count);
        Assert.Equal(@"c:\5.XLSX", file.RecentFiles[0]);
    }
}
