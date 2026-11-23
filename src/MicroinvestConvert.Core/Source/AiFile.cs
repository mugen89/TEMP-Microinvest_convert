using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Values;

namespace MicroinvestConvert.Core.Source;

/// <summary>What the opened workbook is.</summary>
public sealed class AiFile
{
    public required OperationKind Kind { get; init; }
    public required SheetData Sheet { get; init; }

    /// <summary>First data row, 0-based.</summary>
    public required int FirstDataRow { get; init; }

    /// <summary>Where everything is.</summary>
    internal AiColumns Columns { get; }

    public string LayoutName => Columns.Name;

    internal AiFile(AiColumns columns) => Columns = columns;

    private const int DefaultFirstDataRow = 4;   // the fifth row
    private const int SearchRows = 15;

    /// <summary>Finds the AI Accounting sheet of a workbook and recognises what it is.</summary>
    public static AiFile Detect(IReadOnlyList<SheetData> sheets)
    {
        string? refusal = null;

        foreach (var sheet in sheets)
        {
            int indexRow = FindIndexRow(sheet);
            if (indexRow < 0) continue;

            int first = indexRow + 1;
            if (Recognise(sheet, indexRow, out string? why) is { } kind)
                return new AiFile(ColumnsOf(kind, sheet, indexRow, first)) { Kind = kind, Sheet = sheet, FirstDataRow = first };

            refusal ??= why;
        }

        throw new ConvertException(refusal ??
            "Файлът не прилича на експорт от AI Accounting.\n\n" +
            "Очаква се „Експорт Покупки“, „Експорт Продажби“ или „Експорт Чуждестранни“ – с ред, в който колоните са номерирани 1, 2, 2.1, 3 …");
    }

    // The platform numbers its columns on one row: 1 | 2 | 2.1 | 3 | 4 … That row, and
    // not the header texts, says „this is the export" and where the data starts.
    private static int FindIndexRow(SheetData sheet)
    {
        for (int row = 0; row < Math.Min(SearchRows, sheet.Rows.Count); row++)
            if (CellValues.Number(sheet.Cell(row, 0)) == 1m &&
                CellValues.Number(sheet.Cell(row, 1)) == 2m &&
                CellValues.Number(sheet.Cell(row, 2)) == 2.1m &&
                CellValues.Number(sheet.Cell(row, 3)) == 3m)
                return row;

        // A copy without that row still has the fixed place of the data.
        return LooksLikeHeader(sheet, DefaultFirstDataRow - 2) ? DefaultFirstDataRow - 1 : -1;
    }

    private static bool LooksLikeHeader(SheetData sheet, int row) =>
        Header(sheet, row, AiColumns.DocType).Contains("вид документ") &&
        Header(sheet, row, AiColumns.TaxPeriod).Contains("период");

    private static OperationKind? Recognise(SheetData sheet, int indexRow, out string? refusal)
    {
        refusal = null;
        int header = indexRow - 1;

        string partner = Header(sheet, header, AiColumns.PartnerName);
        string treatment = Header(sheet, header, AiColumns.Purchases.Treatment);
        string name = sheet.Name.ToLowerInvariant();

        // The foreign export inserts two columns. Read with the map of the ordinary one, every
        // account would land one or two columns off without any error – so both have to be there.
        if (Header(sheet, header, AiColumns.Foreign.VatId).Contains("vat") &&
            Header(sheet, header, AiColumns.Foreign.EuroTotal).Contains("евро"))
            return OperationKind.Foreign;

        if (name.Contains("чуждестран") || Header(sheet, header, AiColumns.Purchases.SellerBase).Contains("цена"))
        {
            refusal = "Това прилича на експорта „Чуждестранни“, но колоните му не са очакваните: " +
                      "в K трябва да е VAT номерът на доставчика, а в AI – общата сума в евро.";
            return null;
        }

        if (name.Contains("протокол"))
        {
            refusal = "Това е експорт на протоколи. Те ще бъдат добавени в следваща версия.";
            return null;
        }

        if (treatment.Contains("купувач") || partner.Contains("доставчик") || name.Contains("покупки"))
            return OperationKind.Purchase;
        if (Header(sheet, header, AiColumns.JournalCell).Contains("дневник") || partner.Contains("клиент") || name.Contains("продажби"))
            return OperationKind.Sale;

        return null;
    }

    private static AiColumns ColumnsOf(OperationKind kind, SheetData sheet, int indexRow, int firstDataRow) => kind switch
    {
        OperationKind.Purchase => AiColumns.Purchases,
        OperationKind.Foreign => AiColumns.Foreign,
        _ => SalesLayout(sheet, indexRow, firstDataRow),
    };

    private static AiColumns SalesLayout(SheetData sheet, int indexRow, int firstDataRow)
    {
        int header = indexRow - 1;
        var current = AiColumns.Sales;
        var old = AiColumns.SalesBeforeJuly2026;

        if (Header(sheet, header, current.Exempt).Contains("освободен")) return current;
        if (Header(sheet, header, old.Exempt).Contains("освободен")) return old;

        // No usable header: the payment column gives the layout away.
        for (int row = firstDataRow; row < sheet.Rows.Count; row++)
        {
            if (IsPaymentText(sheet.Cell(row, current.Payment))) return current;
            if (IsPaymentText(sheet.Cell(row, old.Payment))) return old;
        }

        return current;
    }

    private static bool IsPaymentText(object? cell) =>
        CellValues.Norm(cell).Length > 0 && PaymentMethods.FromText(CellValues.Norm(cell)) != PaymentMethod.Unknown;

    private static string Header(SheetData sheet, int row, int column) =>
        CellValues.Text(sheet.Cell(row, column)).ToLowerInvariant();
}
