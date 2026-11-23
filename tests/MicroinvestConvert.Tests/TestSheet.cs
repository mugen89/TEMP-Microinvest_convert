using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Rules;
using MicroinvestConvert.Core.Settings;
using MicroinvestConvert.Core.Source;

namespace MicroinvestConvert.Tests;

/// <summary>
/// Builds AI Accounting sheets in memory, by Excel column letter. Every name and number in
/// the tests is invented: the repository is public.
/// </summary>
internal sealed class TestSheet
{
    public enum Layout
    {
        Purchases,
        Sales,
        SalesBeforeJuly2026,
    }

    public const string SupplierVat = "BG123456786";   // a valid check digit
    public const string SupplierEik = "123456786";

    private readonly Layout _layout;
    private readonly int _columns;
    private readonly List<object?[]> _rows = [];

    public TestSheet(Layout layout, bool indexRow = true, bool headers = true)
    {
        _layout = layout;
        _columns = layout switch { Layout.Purchases => 43, Layout.Sales => 47, _ => 44 };

        Raw(("A", "Тестова Фирма ЕООД 12.08.2026"), ("B", "987654326"));
        Raw(("A", "Общи данни на Документ"));
        if (headers) Header();
        else Raw();
        if (indexRow) Raw(("A", 1d), ("B", 2d), ("C", 2.1), ("D", 3d), ("E", 4d));
        else Raw();
    }

    private void Header()
    {
        var cells = new List<(string, object?)>
        {
            ("A", "№"), ("B", "Вид Документ"), ("C", "Код Документ"), ("H", "Данъчен\nПериод на\nЕкспорта\n"),
            ("I", _layout == Layout.Purchases ? "Доставчик" : "Клиент"), ("U", "Един.\nЦена"),
            ("V", "Данъчна \nОснова\nПродавач"),
        };
        switch (_layout)
        {
            case Layout.Purchases:
                cells.Add(("Z", "Вид на ДДС\nТретиране от Купувача\n "));
                break;
            case Layout.Sales:
                cells.AddRange([("Z", "Вид на ДДС\n"), ("AA", "Клетки в\nдневник\nпродажби"), ("AK", "ДО при\nосвободени")]);
                break;
            default:
                cells.AddRange([("Z", "Вид на ДДС\n"), ("AA", "Клетки в\nдневник\nпродажби"), ("AH", "ДО при\nосвободени")]);
                break;
        }

        Raw([.. cells]);
    }

    public TestSheet Raw(params (string Column, object? Value)[] cells)
    {
        var row = new object?[_columns];
        foreach (var (column, value) in cells) row[IndexOf(column)] = value;
        _rows.Add(row);
        return this;
    }

    public TestSheet Blank() => Raw();

    /// <summary>A purchase with full tax credit at 20%, cells overridden by <paramref name="cells"/>.</summary>
    public TestSheet Purchase(object index, string number, decimal @base, decimal vat, params (string Column, object? Value)[] cells) =>
        Row(index, number,
        [
            ("K", "601"), ("L", "Разходи за материали"), ("M", "5"), ("N", "Материали за производство"),
            ("V", (double)@base), ("W", "20%"), ("X", (double)vat), ("Y", (double)(@base + vat)),
            ("Z", "пълен Дан. Кредит"), ("AA", "100%"), ("AB", "-"), ("AC", (double)@base), ("AD", (double)vat),
            ("AE", "-"), ("AF", "-"), ("AG", "-"),
            ("AH", "По сметка - банкова"), ("AI", "401"), ("AJ", "-"), ("AL", "Разходи за материали"), ("AN", "-"),
            .. cells,
        ]);

    /// <summary>A sale in the current layout, with nothing in the ledger columns.</summary>
    public TestSheet Sale(object index, string number, decimal @base, decimal vat, params (string Column, object? Value)[] cells)
    {
        string pay = _layout == Layout.Sales ? "AL" : "AI";
        string debit = _layout == Layout.Sales ? "AM" : "AJ";
        string operation = _layout == Layout.Sales ? "AP" : "AM";
        return Row(index, number,
        [
            ("K", "703"), ("L", "Приходи от услуги"), ("M", "-"),
            ("V", (double)@base), ("X", (double)vat), ("Y", (double)(@base + vat)),
            (pay, "По сметка - банкова"), (debit, "411"), (operation, "Продажба на услуги"),
            .. cells,
        ]);
    }

    private TestSheet Row(object index, string number, (string Column, object? Value)[] cells)
    {
        var all = new List<(string, object?)>
        {
            ("A", index), ("B", "Фактура"), ("C", 1d), ("D", number), ("E", "10.06.2026"), ("F", "12.07.2026"),
            ("H", "06.2026"), ("I", "Тест Партньор ООД"), ("J", SupplierVat), ("P", "12.07.2026"),
            ("Q", "Стока"), ("R", "Артикул"), ("S", 1d), ("T", "EUR"),
        };
        all.AddRange(cells);
        return Raw([.. all]);
    }

    public SheetData ToSheet(string? name = null) =>
        new(name ?? (_layout == Layout.Purchases ? "Експорт Покупки" : "Експорт Продажби"), _rows);

    public OperationBatch Read() => AiReader.Read([ToSheet()], "test.xlsx");

    public OperationBatch Process(ConverterSettings? settings = null, ClientSettings? client = null)
    {
        var batch = Read();
        new DocumentProcessor(new ConverterOptions(settings ?? new ConverterSettings(), client)).Process(batch);
        return batch;
    }

    /// <summary>The only document of the sheet, processed.</summary>
    public OperationDocument Single(ConverterSettings? settings = null, ClientSettings? client = null) =>
        Assert.Single(Process(settings, client).Documents);

    public static int IndexOf(string letters)
    {
        int n = 0;
        foreach (char ch in letters) n = n * 26 + (ch - 'A' + 1);
        return n - 1;
    }
}

internal static class DocumentExtensions
{
    public static bool Has(this OperationDocument doc, IssueCode code) => doc.Issues.Any(i => i.Code == code);

    public static string Amounts(this OperationDocument doc) =>
        string.Join(';', doc.Segments.Select(s => Core.Output.OperationRows.Amount(s.Amount)));

    public static string Accounts(this OperationDocument doc) => string.Join(';', doc.Segments.Select(s => s.Account));

    public static string Deals(this OperationDocument doc) => string.Join(';', doc.Segments.Select(s => s.DealType));
}
