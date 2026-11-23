// miconvert <source> [--out file.xls] [--rows] [--issues] [--skipped]
//
// Converts an AI Accounting export the way the program does it with its default settings.
// --rows prints the rows tab-separated (A..P), which makes two versions easy to compare.

using System.Text;
using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Output;
using MicroinvestConvert.Core.Rules;
using MicroinvestConvert.Core.Settings;
using MicroinvestConvert.Core.Source;

Console.OutputEncoding = new UTF8Encoding(false);

if (args.Length == 0)
{
    Console.Error.WriteLine("miconvert <source> [--out file.xls] [--rows] [--issues] [--skipped]");
    return 2;
}

string source = args[0];
string? output = null;
for (int i = 1; i < args.Length - 1; i++)
    if (args[i] == "--out") output = args[i + 1];

try
{
    var options = ConverterOptions.Default;
    var batch = AiReader.Read(source);
    new DocumentProcessor(options).Process(batch);
    var rows = OperationRows.Build(batch, options);

    Console.WriteLine($"# {Path.GetFileName(source)} | {batch.LayoutName} | {batch.ClientName} ({batch.ClientEik})");
    Console.WriteLine($"# документи {batch.Documents.Count}, за запис {batch.Documents.Count(d => d.WillExport)}, " +
                      $"с грешки {batch.Documents.Count(d => d.HasErrors)}, с предупреждения {batch.Documents.Count(d => d.HasWarnings)}, " +
                      $"редове {rows.Count}, прескочени редове {batch.SkippedRows.Count}");
    foreach (var group in batch.Documents.Where(d => d.WillExport).SelectMany(d => d.Segments).GroupBy(s => s.DealType).OrderBy(g => g.Key))
        Console.WriteLine($"# вид {group.Key,2}: суми {group.Count()}, общо {group.Sum(s => s.Amount):0.00}");

    if (args.Contains("--issues"))
        foreach (var doc in batch.Documents)
            foreach (var issue in doc.Issues)
                Console.WriteLine($"{issue.Severity}\t{issue.Code}\t№ {doc.Index}\t{doc.Number}\t{issue.Message}");

    if (args.Contains("--skipped"))
        foreach (var row in batch.SkippedRows)
            Console.WriteLine($"ред {row.Row}\t{row.Reason}");

    if (args.Contains("--rows"))
        foreach (var row in rows)
            Console.WriteLine(string.Join('\t', row.Cells.Take(OperationsLayout.FirstProtocolColumn)));

    if (output != null && rows.Count > 0)
    {
        DeltaWorkbook.Write(rows, output);
        Console.WriteLine("# записан " + output);
    }

    return 0;
}
catch (ConvertException ex)
{
    Console.Error.WriteLine("ГРЕШКА: " + ex.Message);
    return 1;
}
