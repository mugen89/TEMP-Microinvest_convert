using System.Globalization;
using System.Text.RegularExpressions;
using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Nomenclature;
using MicroinvestConvert.Core.Output;
using MicroinvestConvert.Core.Rules;
using MicroinvestConvert.Core.Settings;
using static MicroinvestConvert.Tests.TestSheet;

namespace MicroinvestConvert.Tests;

/// <summary>
/// Deal_Type.md is what an accountant confirms, so what it says has to be what the program
/// does: its tables are read and compared with the engine.
/// </summary>
public class DealTypeDocumentTests
{
    private static readonly CultureInfo Bulgarian = CultureInfo.GetCultureInfo("bg-BG");
    private static readonly string[] Lines = File.ReadAllLines(FindDocument());

    private static string FindDocument()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder != null; folder = folder.Parent)
        {
            string path = Path.Combine(folder.FullName, "Deal_Type.md");
            if (File.Exists(path)) return path;
        }

        throw new FileNotFoundException("Deal_Type.md");
    }

    private static string[] Cells(string line) =>
        line.Trim().Trim('|').Split('|').Select(c => c.Trim().Replace("**", "")).ToArray();

    private static string[] Row(string id) => Cells(Lines.Single(l => l.StartsWith($"| {id} |", StringComparison.Ordinal)));

    [Fact]
    public void The_version_is_the_one_of_the_program() =>
        Assert.Contains(Lines, l => l.Contains($"Версия {App.AppInfo.VersionText} "));

    [Fact]
    public void The_table_of_deal_types_is_the_nomenclature()
    {
        foreach (var deal in DealTypes.All)
        {
            string[] cells = Cells(Lines.First(l => l.StartsWith($"| {deal.Code} |", StringComparison.Ordinal)));
            Assert.Equal(deal.Name, cells[1]);
            Assert.Equal(deal.LedgerText, cells[2]);
            Assert.Equal(deal.Side == DealSide.Purchase ? "–" : deal.Vies.ToString(CultureInfo.InvariantCulture), cells[3]);
        }
    }

    [Theory]
    [InlineData("П1", "AB", "без Дан. Кредит")]
    [InlineData("П3", "AE", "частичен Дан. Кредит")]
    [InlineData("П4", "AG", "без Дневник Покупки")]
    public void The_purchase_columns_give_the_documented_deal_type(string id, string column, string treatment)
    {
        string[] cells = Row(id);
        Assert.StartsWith(column, cells[1]);

        (string, object?)[] values = column == "AG"
            ? [("AC", "-"), ("AD", "-"), ("AG", "ДА"), ("Z", treatment)]
            : [("AC", "-"), ("AD", "-"), (column, 100d), ("Z", treatment)];
        var doc = new TestSheet(Layout.Purchases).Purchase(1d, "1", 100, 0, values).Single();
        Assert.Equal(cells[4], doc.Segments[0].DealType.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void The_sale_columns_give_the_documented_deal_type_and_vies()
    {
        for (int i = 1; i <= 8; i++)
        {
            string[] cells = Row("ПР" + i);
            string column = Regex.Match(cells[1], "^[A-Z]+").Value;
            var segment = new TestSheet(Layout.Sales).Sale(1d, "1", 100, 0, (column, 100d)).Single().Segments[0];

            Assert.Equal(cells[3], segment.DealType.ToString(CultureInfo.InvariantCulture));
            Assert.Equal(cells[4], segment.Vies.ToString(CultureInfo.InvariantCulture));
            int ledger = LedgerBuckets.SalesLedgerColumn(segment.Lines[0].Bucket);
            Assert.StartsWith("к. " + ledger, cells[2]);
            Assert.StartsWith(ledger.ToString(CultureInfo.InvariantCulture), DealTypes.Get(segment.DealType).LedgerColumns);
        }
    }

    [Fact]
    public void The_examples_of_the_vat_check_come_out_as_written()
    {
        var examples = Lines.Select(l => Regex.Match(l, @"^\| ([\d ]+,\d\d) \| ([\d ]+,\d\d) \| (\d+%) \| .* \| (\d)( ⚠)? \|$"))
            .Where(m => m.Success).ToList();
        Assert.Equal(7, examples.Count);

        foreach (var m in examples)
        {
            decimal @base = decimal.Parse(m.Groups[1].Value, NumberStyles.Number, Bulgarian);
            decimal vat = decimal.Parse(m.Groups[2].Value, NumberStyles.Number, Bulgarian);
            var doc = new TestSheet(Layout.Purchases).Purchase(1d, "1", @base, vat, ("W", m.Groups[3].Value)).Single();

            Assert.Equal(m.Groups[4].Value, doc.Segments[0].DealType.ToString(CultureInfo.InvariantCulture));
            Assert.Equal(m.Groups[5].Success, doc.HasWarnings);
        }
    }

    [Fact]
    public void The_examples_of_rows_come_out_as_written()
    {
        static string[] Built(TestSheet sheet)
        {
            var batch = sheet.Read();
            new DocumentProcessor(ConverterOptions.Default).Process(batch);
            var row = OperationRows.Build(batch, ConverterOptions.Default)[0];
            return [row.Cells[0], row.Cells[1], row.Cells[OperationsLayout.DealType], row.Cells[OperationsLayout.Vies]];
        }

        static string[] Written(string start)
        {
            string[] cells = Cells(Lines.Single(l => l.StartsWith("| " + start, StringComparison.Ordinal)));
            return cells.Skip(1).Select(c => c == "празно" ? "" : c.Trim('`')).ToArray();
        }

        Assert.Equal(Written("100,00 + 20% ДДС"), Built(new TestSheet(Layout.Purchases).Purchase(1d, "1", 100, 20)));
        Assert.Equal(Written("100,00 + 20%, 50,00 + 9%"), Built(new TestSheet(Layout.Purchases)
            .Purchase(1d, "1", 100, 20)
            .Purchase(1d, "1", 50, 4.50m, ("W", "9%"), ("K", "602"), ("M", "1"))
            .Purchase(1d, "1", 30, 0, ("W", "0%"), ("K", "609"), ("M", "2"))));
        Assert.Equal(Written("без данъчен кредит, 120,00"), Built(new TestSheet(Layout.Purchases)
            .Purchase(1d, "1", 100, 20, ("K", "609"), ("M", "3"), ("AB", 120d), ("AC", "-"), ("AD", "-"))));
        Assert.Equal(Written("кредитно известие |"), Built(new TestSheet(Layout.Purchases)
            .Purchase(1d, "1", -4.80m, -0.96m, ("B", "КИ"), ("C", 3d))));

        Assert.Equal(Written("375,00 + 20% ДДС"), Built(new TestSheet(Layout.Sales).Sale(1d, "1", 375, 75, ("AB", 375d), ("AC", 75d))));
        Assert.Equal(Written("100,00 + 20% и 100,00 + 9%"), Built(new TestSheet(Layout.Sales)
            .Sale(1d, "1", 100, 20, ("K", "702"), ("AB", 100d), ("AC", 20d))
            .Sale(1d, "1", 100, 9, ("K", "703"), ("AD", 100d), ("AE", 9d))));
        Assert.Equal(Written("ВОД 470,00"), Built(new TestSheet(Layout.Sales).Sale(1d, "1", 470, 0, ("K", "702"), ("AF", 470d))));
        Assert.Equal(Written("услуга към ЕС 130,00"), Built(new TestSheet(Layout.Sales).Sale(1d, "1", 130, 0, ("AI", 130d))));

        Assert.Equal(Written("фактура от чужбина, 100,00 USD"), Built(new TestSheet(Layout.Foreign).Invoice(1d, "1", 100, "USD", 86.27m)));
        Assert.Equal(Written("кредитно известие от чужбина"), Built(new TestSheet(Layout.Foreign)
            .Invoice(1d, "1", -50, "EUR", -50, ("B", "КИ"), ("C", 3d))));
    }

    [Fact]
    public void A_foreign_supplier_outside_the_ledger_is_written_and_warned_about()
    {
        Assert.StartsWith("вид 3 с предупреждение", Row("П17")[2]);
        var doc = new TestSheet(Layout.Purchases)
            .Purchase(1d, "1", 100, 0, ("AC", "-"), ("AD", "-"), ("AG", "ДА"), ("J", "DE123456789")).Single();
        Assert.Equal(("3", true, true), (doc.Deals(), doc.WillExport, doc.HasWarnings));
    }

    [Fact]
    public void The_vat_accounts_and_the_differences_are_the_defaults()
    {
        var settings = new ConverterSettings();
        Assert.Contains(Lines, l => l.Contains($"| ДДС при покупки | **{settings.PurchaseVatAccount}** |"));
        Assert.Contains(Lines, l => l.Contains($"| ДДС при продажби | **{settings.SaleVatAccount}** |"));
        Assert.Contains(Lines, l => l.Contains(
            $"**{settings.TolerancePerDocument.ToString("0.00", Bulgarian)}** за документ и **{settings.TolerancePerLine.ToString("0.00", Bulgarian)}** за отделен ред"));
    }
}
