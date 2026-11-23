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

    /// <summary>Column map of a sales export; null for purchases.</summary>
    internal AiColumns.Sales? Sales { get; init; }

    public string LayoutName => Kind == OperationKind.Purchase
        ? "Експорт Покупки"
        : Sales!.IsCurrent ? "Експорт Продажби" : "Експорт Продажби (колони до юли 2026)";

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
            switch (Recognise(sheet, indexRow, out string? why))
            {
                case OperationKind.Purchase:
                    return new AiFile { Kind = OperationKind.Purchase, Sheet = sheet, FirstDataRow = first };
                case OperationKind.Sale:
                    return new AiFile
                    {
                        Kind = OperationKind.Sale, Sheet = sheet, FirstDataRow = first,
                        Sales = SalesLayout(sheet, indexRow, first),
                    };
            }

            refusal ??= why;
        }

        throw new ConvertException(refusal ??
            "Файлът не прилича на експорт от AI Accounting.\n\n" +
            "Очаква се „Експорт Покупки“ или „Експорт Продажби“ – с ред, в който колоните са номерирани 1, 2, 2.1, 3 …");
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
        string treatment = Header(sheet, header, AiColumns.Treatment);
        string name = sheet.Name.ToLowerInvariant();

        // The foreign export inserts two columns, so its „Данъчна основа" sits one to the right.
        if (name.Contains("чуждестран") || Header(sheet, header, AiColumns.SellerBase).Contains("цена"))
        {
            refusal = "Това е експортът „Чуждестранни“. Той се осчетоводява с протоколи, които ще бъдат добавени в следваща версия.";
            return null;
        }

        if (name.Contains("протокол"))
        {
            refusal = "Това е експорт на протоколи. Те ще бъдат добавени в следваща версия.";
            return null;
        }

        if (treatment.Contains("купувач") || partner.Contains("доставчик") || name.Contains("покупки"))
            return OperationKind.Purchase;
        if (Header(sheet, header, AiColumns.Sales.JournalCell).Contains("дневник") || partner.Contains("клиент") || name.Contains("продажби"))
            return OperationKind.Sale;

        return null;
    }

    private static AiColumns.Sales SalesLayout(SheetData sheet, int indexRow, int firstDataRow)
    {
        int header = indexRow - 1;
        var current = AiColumns.Sales.Current;
        var old = AiColumns.Sales.BeforeJuly2026;

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
