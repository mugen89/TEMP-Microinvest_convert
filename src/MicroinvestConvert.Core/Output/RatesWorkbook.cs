using MicroinvestConvert.Core.Source;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using NPOI.XSSF.UserModel;

namespace MicroinvestConvert.Core.Output;

/// <summary>The rate of a currency on a day.</summary>
/// <param name="Day">The day it is for.</param>
/// <param name="Code">The currency.</param>
/// <param name="Name">Its name.</param>
/// <param name="PerEuro">Units of it for one euro.</param>
/// <param name="Published">The day of the table it is from.</param>
public sealed record RateLine(DateTime Day, string Code, string Name, decimal PerEuro, DateTime Published);

/// <summary>
/// Exchange rates as an Excel workbook, for whoever wants to check them: dates are dates and rates
/// are numbers, so they can be filtered and calculated with.
/// </summary>
public static class RatesWorkbook
{
    public const string Extension = ".xlsx";
    public const string SheetName = "Курсове";

    private static readonly (string Header, int Width)[] Columns =
    [
        ("Дата", 13), ("Код", 8), ("Валута", 30), ("За 1 евро", 16), ("Евро за единица", 19), ("Курс от", 13),
    ];

    /// <param name="path">The file.</param>
    /// <param name="title">What the rates are of.</param>
    /// <param name="notes">Lines under the title: the period, where the rates are from.</param>
    /// <param name="lines">The rates, in the order they are written.</param>
    /// <exception cref="ConvertException">The file cannot be written.</exception>
    public static void Write(string path, string title, IReadOnlyList<string> notes, IReadOnlyList<RateLine> lines) =>
        SafeFile.Write(path, Build(title, notes, lines));

    public static byte[] Build(string title, IReadOnlyList<string> notes, IReadOnlyList<RateLine> lines)
    {
        if (lines.Count == 0) throw new ConvertException("Няма курсове за записване.");

        using var workbook = new XSSFWorkbook();
        ISheet sheet = workbook.CreateSheet(SheetName);
        for (int c = 0; c < Columns.Length; c++) sheet.SetColumnWidth(c, Columns[c].Width * 256);

        ICellStyle heading = Style(workbook, bold: true, points: 13), header = Style(workbook, bold: true);
        header.FillForegroundColor = IndexedColors.Grey25Percent.Index;
        header.FillPattern = FillPattern.SolidForeground;
        ICellStyle date = Style(workbook, "dd.mm.yyyy"), rate = Style(workbook, "#,##0.0000####"), reverse = Style(workbook, "#,##0.000000####");

        int at = 0;
        Text(sheet.CreateRow(at++), 0, title, heading);
        foreach (string note in notes) sheet.CreateRow(at++).CreateCell(0).SetCellValue(note);
        at++;

        int headers = at++;
        IRow titles = sheet.CreateRow(headers);
        for (int c = 0; c < Columns.Length; c++) Text(titles, c, Columns[c].Header, header);

        foreach (var line in lines)
        {
            IRow row = sheet.CreateRow(at++);
            Date(row, 0, line.Day, date);
            row.CreateCell(1).SetCellValue(line.Code);
            row.CreateCell(2).SetCellValue(line.Name);
            Number(row, 3, line.PerEuro, rate);
            Number(row, 4, 1m / line.PerEuro, reverse);
            Date(row, 5, line.Published, date);
        }

        sheet.CreateFreezePane(0, headers + 1);
        sheet.SetAutoFilter(new CellRangeAddress(headers, at - 1, 0, Columns.Length - 1));

        using var stream = new MemoryStream();
        workbook.Write(stream, leaveOpen: true);
        return stream.ToArray();
    }

    private static ICellStyle Style(XSSFWorkbook workbook, string format = "", bool bold = false, double points = 0)
    {
        ICellStyle style = workbook.CreateCellStyle();
        if (format.Length > 0) style.DataFormat = workbook.CreateDataFormat().GetFormat(format);
        if (!bold) return style;

        IFont font = workbook.CreateFont();
        font.IsBold = true;
        if (points > 0) font.FontHeightInPoints = points;
        style.SetFont(font);
        return style;
    }

    private static void Text(IRow row, int column, string value, ICellStyle style)
    {
        ICell cell = row.CreateCell(column);
        cell.SetCellValue(value);
        cell.CellStyle = style;
    }

    private static void Date(IRow row, int column, DateTime value, ICellStyle style)
    {
        ICell cell = row.CreateCell(column);
        cell.SetCellValue(value);
        cell.CellStyle = style;
    }

    private static void Number(IRow row, int column, decimal value, ICellStyle style)
    {
        ICell cell = row.CreateCell(column);
        cell.SetCellValue((double)value);
        cell.CellStyle = style;
    }
}
