using MicroinvestConvert.Core.Source;
using MicroinvestConvert.Core.Values;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using L = MicroinvestConvert.Core.Output.OperationsLayout;

namespace MicroinvestConvert.Core.Output;

/// <summary>
/// Writes the import workbook of Делта Pro. Always BIFF8 (.xls) — the program does not read
/// .xlsx — and every cell of „Операции" as text: „601/5;453/1" and the leading zeros of the
/// document numbers must not turn into numbers or dates.
/// </summary>
public static class DeltaWorkbook
{
    public const string Extension = ".xls";

    /// <summary>BIFF8 ends at row 65536.</summary>
    public const int MaxRows = 65536 - 1;

    public static void Write(IReadOnlyList<OperationRow> rows, string path)
    {
        byte[] content = Build(rows);

        // Written next to the target and swapped in, so a failure never leaves half a file.
        string temp = path + ".tmp";
        try
        {
            File.WriteAllBytes(temp, content);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temp); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            throw new ConvertException(
                "Файлът не може да бъде записан. Ако е отворен в Excel или в Делта Pro, затворете го и опитайте отново.\n\n" +
                ex.Message, ex);
        }

        Verify(rows, path);
    }

    public static byte[] Build(IReadOnlyList<OperationRow> rows)
    {
        if (rows.Count == 0) throw new ConvertException("Няма документи за записване.");
        if (rows.Count > MaxRows) throw new ConvertException($"Редовете са {rows.Count}, а файлът .xls побира {MaxRows}.");

        using var workbook = new HSSFWorkbook();
        short textFormat = workbook.CreateDataFormat().GetFormat("@");

        ICellStyle text = workbook.CreateCellStyle();
        text.DataFormat = textFormat;

        IFont bold = workbook.CreateFont();
        bold.IsBold = true;
        ICellStyle header = workbook.CreateCellStyle();
        header.DataFormat = textFormat;
        header.SetFont(bold);

        WriteData(workbook.CreateSheet(L.DataSheet), rows, text, header);
        WriteSettings(workbook.CreateSheet(L.SettingsSheet), rows.Count, text, header);
        workbook.SetActiveSheet(0);

        using var stream = new MemoryStream();
        workbook.Write(stream, leaveOpen: true);
        return stream.ToArray();
    }

    private static void WriteData(ISheet sheet, IReadOnlyList<OperationRow> rows, ICellStyle text, ICellStyle header)
    {
        IRow titles = sheet.CreateRow(0);
        for (int c = 0; c < L.Columns.Count; c++) SetText(titles, c, L.Columns[c].Header, header);

        for (int r = 0; r < rows.Count; r++)
        {
            IRow row = sheet.CreateRow(L.FirstRow - 1 + r);
            for (int c = 0; c < L.Columns.Count; c++) SetText(row, c, rows[r].Cells[c], text);
        }

        sheet.CreateFreezePane(0, 1);

        // Widths are counted from the texts: NPOI's own measuring needs installed fonts.
        for (int c = 0; c < L.Columns.Count; c++)
        {
            int longest = Math.Max(L.Columns[c].Header.Length, rows.Max(row => row.Cells[c].Length));
            sheet.SetColumnWidth(c, Math.Clamp(longest + 2, 6, 45) * 256);
        }
    }

    private static void WriteSettings(ISheet sheet, int rowCount, ICellStyle text, ICellStyle header)
    {
        IRow title = sheet.CreateRow(0);
        SetText(title, 0, "Настройка", header);
        SetText(title, 1, "", header);
        sheet.AddMergedRegion(new CellRangeAddress(0, 0, 0, 1));

        int r = 1;
        SetText(Row(), 0, "Sheet с данни", text).SetCellTo(1, L.DataSheet, text);
        SetText(Row(), 0, "Начален ред", text).SetNumberTo(1, L.FirstRow);
        SetText(Row(), 0, "Краен ред", text).SetNumberTo(1, L.FirstRow + rowCount - 1);
        foreach (var column in L.Columns) SetText(Row(), 0, column.Setting, text).SetCellTo(1, column.Letter, text);

        sheet.SetColumnWidth(0, 32 * 256);
        sheet.SetColumnWidth(1, 14 * 256);

        IRow Row() => sheet.CreateRow(r++);
    }

    private static IRow SetText(IRow row, int column, string value, ICellStyle style) => row.SetCellTo(column, value, style);

    private static IRow SetCellTo(this IRow row, int column, string value, ICellStyle style)
    {
        // An unset string cell of BIFF8 points at the first shared string, so "" is written too.
        ICell cell = row.CreateCell(column, CellType.String);
        cell.CellStyle = style;
        cell.SetCellValue(value);
        return row;
    }

    private static IRow SetNumberTo(this IRow row, int column, int value)
    {
        row.CreateCell(column, CellType.Numeric).SetCellValue(value);
        return row;
    }

    /// <summary>Reads the file back and compares it cell by cell with what had to be written.</summary>
    public static void Verify(IReadOnlyList<OperationRow> rows, string path)
    {
        var sheets = WorkbookLoader.Load(path);
        var data = sheets.FirstOrDefault(s => s.Name == L.DataSheet) ??
                   throw new ConvertException($"Проверката след запис не намери листа „{L.DataSheet}“.");

        if (data.Rows.Count != L.FirstRow - 1 + rows.Count)
            throw new ConvertException(
                $"Проверката след запис намери {data.Rows.Count} реда вместо {L.FirstRow - 1 + rows.Count}.");

        for (int r = 0; r < rows.Count; r++)
            for (int c = 0; c < L.Columns.Count; c++)
            {
                string written = data.Cell(L.FirstRow - 1 + r, c) as string ?? "";
                if (written != rows[r].Cells[c])
                    throw new ConvertException(
                        $"Проверката след запис откри разлика в клетка {L.Columns[c].Letter}{L.FirstRow + r}: " +
                        $"„{written}“ вместо „{rows[r].Cells[c]}“.");
            }
    }
}
