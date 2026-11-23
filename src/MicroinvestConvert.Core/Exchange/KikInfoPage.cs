using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using MicroinvestConvert.Core.Values;

namespace MicroinvestConvert.Core.Exchange;

/// <summary>
/// Reads the page „Валутни курсове" of kik-info.com. The date is the one in its title, not the
/// one that was asked for: for a Sunday, for today before the publication and for any day to
/// come the page shows the last table there is. A page that is not certainly understood is
/// refused – a wrong rate is worse than none.
/// </summary>
public static partial class KikInfoPage
{
    private const int FewestRows = 5;

    public static RateTable Parse(string html)
    {
        if (Title().Match(html) is not { Success: true } title ||
            !DateTime.TryParseExact(title.Groups[1].Value, "d.M.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw Refuse("на нея няма дата");

        foreach (Match table in Table().Matches(html))
        {
            var rows = Row().Matches(table.Value).Select(r => Cells(r.Value)).ToList();
            int header = rows.FindIndex(r => Columns.Of(r) != null);
            if (header < 0) continue;

            var columns = Columns.Of(rows[header])!;
            var read = rows.Skip(header + 1).Select(r => ReadRow(r, columns)).OfType<RateRow>().DistinctBy(r => r.Code).ToList();
            if (Doubt(columns.Currency, read) is { } doubt) throw Refuse(doubt);
            return new RateTable(date, columns.Currency, read);
        }

        throw Refuse("на нея няма таблица с курсове");
    }

    private static RateException Refuse(string why) => new(RateFailure.BadPage,
        $"{KikInfoSource.Host} отговори, но страницата не е в очаквания вид: {why}. Курсовете, които вече са изтеглени, не са променени.");

    // ----- the table -----

    /// <summary>Where the columns are, by the words in the header.</summary>
    private sealed record Columns(int Code, int Rate, int Reverse, int Units, string Currency)
    {
        public static Columns? Of(List<string> header)
        {
            int At(string word) => header.FindIndex(h => h.Contains(word, StringComparison.OrdinalIgnoreCase));

            int code = header.FindIndex(h => h.Equals("код", StringComparison.OrdinalIgnoreCase));
            int rate = header.FindIndex(h => TableCurrency().IsMatch(h));
            int reverse = At("обратен"), units = At("единиц");
            return code < 1 || rate < 0 || reverse < 0 || units < 0
                ? null
                : new Columns(code, rate, reverse, units, TableCurrency().Match(header[rate]).Groups[1].Value);
        }
    }

    private static List<string> Cells(string row)
    {
        var cells = new List<string>();
        foreach (Match cell in Cell().Matches(row))
        {
            cells.Add(CellValues.Clean(WebUtility.HtmlDecode(Tag().Replace(cell.Groups[2].Value, " "))));

            // „Валута" stands over two columns.
            int span = ColumnSpan().Match(cell.Groups[1].Value) is { Success: true } m ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 1;
            for (int i = 1; i < span; i++) cells.Add("");
        }

        return cells;
    }

    // A currency that is not quoted any more is there with zeros.
    private static RateRow? ReadRow(List<string> cells, Columns c)
    {
        if (cells.Count <= Math.Max(Math.Max(c.Code, c.Rate), Math.Max(c.Reverse, c.Units))) return null;
        string code = cells[c.Code].ToUpperInvariant();
        if (!Currencies.IsCode(code)) return null;

        return Number(cells[c.Units]) is { } units and > 0m && Number(cells[c.Rate]) is { } rate and > 0m && Number(cells[c.Reverse]) is { } reverse
            ? new RateRow(code, cells[c.Code - 1], units, rate, reverse)
            : null;
    }

    private static decimal? Number(string text) =>
        decimal.TryParse(text.Replace(" ", "").Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal n) ? n : null;

    // ----- is it what it says -----

    /// <summary>Currencies that say which way round a table in euro is.</summary>
    private static readonly Dictionary<string, (decimal Least, decimal Most)> PerEuro = new()
    {
        ["DKK"] = (6.5m, 8.5m),   // tied to the euro
        ["CZK"] = (10m, 100m),
        ["HUF"] = (100m, 2000m),
        ["JPY"] = (20m, 1000m),
    };

    private static string? Doubt(string currency, List<RateRow> rows)
    {
        if (rows.Count < FewestRows) return "таблицата е празна";

        // The reverse rate has few digits (0.000050 for a rupiah), so the difference may be bigger there.
        foreach (var row in rows)
            if (row.Reverse > 0m && Math.Abs(row.Rate * row.Reverse / row.Units - 1m) > 0.02m + 0.000001m / row.Reverse)
                return $"курсът и обратният курс на {row.Code} не си съответстват";

        if (currency == Currencies.Lev)
            return rows.Any(r => r.Code == Currencies.Euro && r.Units == 1m && r.Rate == Currencies.LevaPerEuro) ? null : "курсът на еврото не е 1.95583";

        var known = rows.Where(r => PerEuro.ContainsKey(r.Code)).ToList();
        return known.Count > 0 && known.TrueForAll(r => r.Rate >= PerEuro[r.Code].Least && r.Rate <= PerEuro[r.Code].Most)
            ? null
            : "курсовете не са за едно евро";
    }

    [GeneratedRegex(@"Валутни\s+курсове\s+за\s+(\d{1,2}\.\d{1,2}\.\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex Title();

    [GeneratedRegex(@"<table\b.*?</table>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Table();

    [GeneratedRegex(@"<tr\b.*?</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Row();

    [GeneratedRegex(@"<t[hd]\b([^>]*)>(.*?)</t[hd]>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Cell();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"colspan\s*=\s*[""']?(\d{1,2})", RegexOptions.IgnoreCase)]
    private static partial Regex ColumnSpan();

    [GeneratedRegex(@"\((EUR|BGN)\)")]
    private static partial Regex TableCurrency();
}
