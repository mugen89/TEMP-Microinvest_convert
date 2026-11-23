// miconvert <source> [--out file.xls] [--rows] [--issues] [--skipped] [--no-diary 1|3|skip] [--vat total|account]
//                    [--rates file.db] [--rate-date dd.MM.yyyy] [--currency-account 401/2=USD]
//
// Converts an AI Accounting export the way the program does it with its default settings.
// --rows prints the rows tab-separated (A..P), which makes two versions easy to compare;
// --no-diary and --vat are the two settings of „Настройки → Файл" that change them most.
// --rates takes the exchange rates of a database of the program; --rate-date uses another day
// for every document in another currency.
//
// miconvert rates <file.db> <from dd.MM.yyyy> [to]   downloads the exchange rates into a database
// miconvert rates <file.db>                          says which days it has

using System.Globalization;
using System.Text;
using MicroinvestConvert.Core.Exchange;
using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Output;
using MicroinvestConvert.Core.Rules;
using MicroinvestConvert.Core.Settings;
using MicroinvestConvert.Core.Source;
using MicroinvestConvert.Core.Values;
using MicroinvestConvert.Data;

Console.OutputEncoding = new UTF8Encoding(false);

if (args.Length == 0)
{
    Console.Error.WriteLine("miconvert <source> [--out file.xls] [--rows] [--issues] [--skipped] [--no-diary 1|3|skip] [--vat total|account] " +
                            "[--rates file.db] [--rate-date dd.MM.yyyy] [--currency-account 401/2=USD]");
    Console.Error.WriteLine("miconvert rates <file.db> [<from dd.MM.yyyy> [<to>]]");
    return 2;
}

if (args[0] == "rates" && args.Length > 1) return await RatesAsync();

string source = args[0];
string? Value(string name) => Array.IndexOf(args, name) is var i && i > 0 && i + 1 < args.Length ? args[i + 1] : null;
string? output = Value("--out");

var settings = new ConverterSettings();
if (Value("--no-diary") is { } noDiary)
    settings.NoDiary = noDiary switch { "1" => NoDiaryMode.Internal, "3" => NoDiaryMode.NoCredit, _ => NoDiaryMode.Skip };
if (Value("--vat") is { } vat)
    settings.VatLines = vat == "total" ? VatLines.PerDealType : VatLines.PerAccount;
if (Value("--currency-account") is { } account && account.Split('=') is [var number, var code])
    settings.AccountCurrencies[number] = code;

try
{
    var options = new ConverterOptions(settings);
    IExchangeRates exchange = Value("--rates") is { } db ? new RateStore(Database.Open(db)) : NoRates.Instance;
    var batch = AiReader.Read(source);
    if (Value("--rate-date") is { } day)
        foreach (var doc in batch.Documents.Where(d => d.InForeignCurrency)) doc.TrySet(DocField.RateDate, day, out _);
    new DocumentProcessor(options, exchange).Process(batch);
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
catch (Exception ex) when (ex is ConvertException or DatabaseException)
{
    Console.Error.WriteLine("ГРЕШКА: " + ex.Message);
    return 1;
}

async Task<int> RatesAsync()
{
    try
    {
        var store = new RateStore(Database.Open(args[1]));
        if (args.Length > 2)
        {
            if (CellValues.Date(args[2]) is not { } from) throw new ConvertException($"„{args[2]}“ не е дата.");
            var to = args.Length > 3 ? CellValues.Date(args[3]) ?? DateTime.Today : DateTime.Today;
            using var site = new KikInfoSource();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var result = await new RateUpdater(site, store).RunAsync(RateCalendar.Days(from, to), DateTime.Today);
            Console.WriteLine($"# нови дни {result.Added}, липсват {result.Missing}, {watch.Elapsed.TotalSeconds:0.0} s" +
                              (result.Failure is { } failure ? $", {failure.Title}: {failure.Message}" : ""));
        }

        foreach (var range in RateCalendar.Ranges(store.Days()))
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{range.From:dd.MM.yyyy} – {range.To:dd.MM.yyyy}  ({range.Days})"));
        return 0;
    }
    catch (Exception ex) when (ex is ConvertException or DatabaseException)
    {
        Console.Error.WriteLine("ГРЕШКА: " + ex.Message);
        return 1;
    }
}
