using System.Text;
using ExcelDataReader;

namespace MicroinvestConvert.Core.Source;

/// <summary>The cells of one sheet, as Excel stores them (0-based rows and columns).</summary>
public sealed class SheetData(string name, IReadOnlyList<object?[]> rows)
{
    public string Name { get; } = name;
    public IReadOnlyList<object?[]> Rows { get; } = rows;

    public object? Cell(int row, int column)
    {
        if (row < 0 || row >= Rows.Count || column < 0) return null;
        object?[] cells = Rows[row];
        return column < cells.Length ? cells[column] : null;
    }
}

/// <summary>A problem the user has to be told about in plain words.</summary>
public class ConvertException(string message, Exception? inner = null) : Exception(message, inner);

public static class WorkbookLoader
{
    static WorkbookLoader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Reads every visible sheet of an .xlsx / .xlsm / .xls file, also while it is open in Excel.</summary>
    public static IReadOnlyList<SheetData> Load(string path)
    {
        byte[] content;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            content = copy.ToArray();
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new ConvertException("Файлът не е намерен:\n" + path, ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ConvertException("Файлът не може да бъде отворен:\n" + ex.Message, ex);
        }

        return Load(content);
    }

    public static IReadOnlyList<SheetData> Load(byte[] content)
    {
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var reader = ExcelReaderFactory.CreateReader(stream);

            var sheets = new List<SheetData>();
            do
            {
                var rows = new List<object?[]>();
                while (reader.Read())
                {
                    var cells = new object?[reader.FieldCount];
                    for (int i = 0; i < cells.Length; i++) cells[i] = reader.GetValue(i);
                    rows.Add(cells);
                }

                if (reader.VisibleState == "visible") sheets.Add(new SheetData(reader.Name ?? "", rows));
            }
            while (reader.NextResult());

            if (sheets.Count == 0) throw new ConvertException("Файлът няма видими работни листове.");
            return sheets;
        }
        catch (ConvertException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The reader reports a damaged or foreign file through many exception types.
            throw new ConvertException("Файлът не е разпознат като Excel (.xlsx или .xls).", ex);
        }
    }
}
