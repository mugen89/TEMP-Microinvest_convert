using System.Globalization;
using System.Text;

namespace MicroinvestConvert.Core.Values;

/// <summary>
/// Reads the loosely typed cells of an Excel sheet. Numbers and dates arrive either
/// as real numbers/dates or as text in several spellings; „-" means „няма стойност".
/// </summary>
public static class CellValues
{
    /// <summary>The cell as trimmed text. A whole number has no decimals; „-" stays „-".</summary>
    public static string Text(object? value) => value switch
    {
        null => "",
        string s => Clean(s),
        bool b => b ? "ДА" : "НЕ",
        DateTime d => d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
        _ when IsNumeric(value) => AsDecimal(value) is { } n
            ? n.ToString("0.############", CultureInfo.InvariantCulture)
            : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
        _ => Clean(value.ToString() ?? ""),
    };

    /// <summary>Like <see cref="Text"/>, but the file's „няма стойност" mark becomes empty.</summary>
    public static string Norm(object? value)
    {
        string text = Text(value);
        return text is "-" or "–" or "—" ? "" : text;
    }

    public static bool IsNumeric(object? value) =>
        value is double or float or int or long or short or decimal;

    // Excel keeps 15 significant digits; the conversion rounds to them, so the
    // binary noise of a double (0.1 + 0.2) never reaches an amount.
    private static decimal? AsDecimal(object? value)
    {
        try { return Convert.ToDecimal(value, CultureInfo.InvariantCulture); }
        catch (OverflowException) { return null; }
    }

    public static decimal? Number(object? value)
    {
        if (value is null) return null;
        if (IsNumeric(value)) return AsDecimal(value);

        string text = Norm(value);
        if (text.Length == 0) return null;

        // A minus may arrive as the Unicode minus sign, or as accounting brackets.
        bool negative = text.IndexOfAny(['-', '−']) >= 0 || (text.Contains('(') && text.Contains(')'));
        var digits = new StringBuilder(text.Length);
        foreach (char ch in text)
            if (char.IsAsciiDigit(ch) || ch is '.' or ',') digits.Append(ch);

        string s = digits.ToString().Trim('.', ',');
        if (s.Length == 0) return null;

        int dot = s.LastIndexOf('.'), comma = s.LastIndexOf(',');
        if (dot >= 0 && comma >= 0)
        {
            // The right-most one is the decimal mark, the other groups thousands.
            s = dot > comma ? s.Replace(",", "") : s.Replace(".", "").Replace(',', '.');
        }
        else if (comma >= 0)
        {
            s = s.IndexOf(',') == comma ? s.Replace(',', '.') : s.Replace(",", "");
        }
        else if (dot >= 0 && s.IndexOf('.') != dot)
        {
            s = s.Replace(".", "");
        }

        if (!decimal.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal number))
            return null;
        return negative ? -number : number;
    }

    public static decimal NumberOrZero(object? value) => Number(value) ?? 0m;

    public static int? Integer(object? value)
    {
        decimal? n = Number(value);
        return n is >= int.MinValue and <= int.MaxValue && n == Math.Truncate(n.Value) ? (int)n.Value : null;
    }

    private static readonly string[] DateFormats =
    [
        "d.M.yyyy", "d.M.yy", "d/M/yyyy", "d/M/yy", "d-M-yyyy", "d-M-yy", "yyyy-M-d", "yyyy.M.d", "yyyy/M/d",
    ];

    /// <summary>A date: a real date cell, an Excel serial, or dd.MM.yyyy / yyyy-MM-dd text.</summary>
    public static DateTime? Date(object? value)
    {
        if (value is DateTime d) return d.Date;
        if (IsNumeric(value)) return FromSerial(Convert.ToDouble(value, CultureInfo.InvariantCulture));

        string text = Norm(value);
        if (text.Length == 0) return null;

        // „05.06.2026 г.", „05.06.2026г.", „05.06.2026 10:15" — the date is what comes first.
        string token = new string(text.TakeWhile(c => char.IsAsciiDigit(c) || c is '.' or '/' or '-').ToArray()).TrimEnd('.', '-');
        return DateTime.TryParseExact(token, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }

    private static DateTime? FromSerial(double serial)
    {
        // 1 = 01.01.1900; 73050 = 31.12.2099. Anything else is not a date.
        if (serial < 1 || serial > 73050) return null;
        return DateTime.FromOADate(Math.Floor(serial));
    }

    /// <summary>
    /// The tax period „MM.yyyy" as the first day of its month. Excel may have turned
    /// „06.2026" into the number 6.2026, so that shape is read too.
    /// </summary>
    public static DateTime? Period(object? value)
    {
        if (value is DateTime d) return new DateTime(d.Year, d.Month, 1);

        if (IsNumeric(value))
        {
            if (AsDecimal(value) is not { } n) return null;
            if (n >= 1 && n < 13)
            {
                int month = (int)Math.Truncate(n);
                int year = (int)Math.Round((n - month) * 10000m);
                return MonthStart(year, month);
            }

            DateTime? serial = FromSerial((double)n);
            return serial is { } s ? new DateTime(s.Year, s.Month, 1) : null;
        }

        string text = Norm(value);
        if (text.Length == 0) return null;

        string[] parts = text.Split('.', '/', '-');
        if (parts.Length == 2 &&
            int.TryParse(parts[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int first) &&
            int.TryParse(parts[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int second))
        {
            // „06.2026" or „2026-06"
            return first > 12 ? MonthStart(first, second) : MonthStart(second, first);
        }

        DateTime? full = Date(text);
        return full is { } f ? new DateTime(f.Year, f.Month, 1) : null;
    }

    private static DateTime? MonthStart(int year, int month)
    {
        if (year is >= 0 and < 100) year += 2000;
        return month is >= 1 and <= 12 && year is >= 1990 and <= 2100 ? new DateTime(year, month, 1) : null;
    }

    /// <summary>Collapses line breaks, tabs and special spaces to one space; trims.</summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var sb = new StringBuilder(text.Length);
        bool space = false;
        foreach (char ch in text)
        {
            // char.IsWhiteSpace covers the non-breaking spaces; U+200B and U+FEFF are invisible.
            if (char.IsWhiteSpace(ch) || char.IsControl(ch) || ch is '​' or '﻿')
            {
                space = true;
                continue;
            }

            if (space && sb.Length > 0) sb.Append(' ');
            space = false;
            sb.Append(ch);
        }

        return sb.ToString();
    }
}
