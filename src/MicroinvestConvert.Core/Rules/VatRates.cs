using System.Globalization;
using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Settings;
using MicroinvestConvert.Core.Values;

namespace MicroinvestConvert.Core.Rules;

/// <param name="Status">What the amounts say.</param>
/// <param name="Rate">The rate they correspond to; null when none does.</param>
/// <param name="Expected">The VAT that rate gives.</param>
public readonly record struct VatMatch(VatStatus Status, decimal? Rate, decimal Expected)
{
    public bool IsResolved => Status != VatStatus.Unresolved;
}

/// <summary>
/// Which VAT rate a base and a VAT amount correspond to. The VAT of an invoice is rounded
/// line by line, so the total is often a few cents away from „base × rate": a difference is
/// allowed — one for a whole document, a smaller one for a single line of it.
/// </summary>
public sealed class VatRates(ConverterSettings settings)
{
    /// <summary>The rates with VAT, most common first.</summary>
    public static readonly IReadOnlyList<decimal> Known = [20m, 9m];

    /// <summary>Allowed difference for the amounts of one document together.</summary>
    public decimal DocumentTolerance => Math.Max(settings.TolerancePerDocument, settings.TolerancePerLine);

    /// <summary>Allowed difference for one line of a document with many.</summary>
    public decimal LineTolerance => settings.TolerancePerLine;

    /// <summary>The VAT of a base at a rate, rounded the way the user chose.</summary>
    public decimal Expected(decimal @base, decimal rate)
    {
        decimal vat = @base * rate / 100m;
        return settings.Rounding switch
        {
            VatRounding.HalfEven => Math.Round(vat, 2, MidpointRounding.ToEven),
            VatRounding.Truncate => Math.Round(vat, 2, MidpointRounding.ToZero),
            _ => Math.Round(vat, 2, MidpointRounding.AwayFromZero),
        };
    }

    /// <param name="base">Sum of the bases.</param>
    /// <param name="vat">Sum of the VAT amounts.</param>
    /// <param name="declared">The rate in the file; null = unknown.</param>
    /// <param name="tolerance">Allowed difference.</param>
    /// <param name="onlyDeclared">Do not look for another rate.</param>
    public VatMatch Match(decimal @base, decimal vat, decimal? declared, decimal tolerance, bool onlyDeclared = false)
    {
        decimal nearest = declared > 0m ? declared.Value : Nearest(@base, vat);

        if (@base == 0m && vat == 0m) return new VatMatch(VatStatus.Empty, declared, 0m);

        if (vat == 0m)
        {
            // A base of a few cents has a VAT of zero cents at any rate.
            if (declared > 0m && Expected(@base, declared.Value) == 0m)
                return new VatMatch(VatStatus.Exact, declared, 0m);
            return new VatMatch(VatStatus.Zero, 0m, declared > 0m ? Expected(@base, declared.Value) : 0m);
        }

        if (@base != 0m && Math.Sign(@base) == Math.Sign(vat))
        {
            var rates = Candidates(declared, onlyDeclared).ToList();
            foreach (decimal rate in rates)
                if (Expected(@base, rate) == vat) return new VatMatch(VatStatus.Exact, rate, vat);

            decimal? best = null;
            foreach (decimal rate in rates)
            {
                decimal off = Math.Abs(vat - Expected(@base, rate));
                if (off <= tolerance && (best is null || off < Math.Abs(vat - Expected(@base, best.Value)))) best = rate;
            }

            if (best is { } found) return new VatMatch(VatStatus.WithinTolerance, found, Expected(@base, found));
        }

        return new VatMatch(VatStatus.Unresolved, null, Expected(@base, nearest));
    }

    private static IEnumerable<decimal> Candidates(decimal? declared, bool onlyDeclared)
    {
        // A foreign rate (19%, 21% …) is never a Bulgarian VAT amount.
        if (declared is { } known && Known.Contains(known)) yield return known;
        if (onlyDeclared) yield break;
        foreach (decimal rate in Known)
            if (rate != declared) yield return rate;
    }

    // For showing an expected amount when nothing fits.
    private static decimal Nearest(decimal @base, decimal vat)
    {
        if (@base == 0m) return Known[0];
        decimal percent = vat / @base * 100m;
        return Known.MinBy(r => Math.Abs(r - percent));
    }

    /// <summary>
    /// The text of „% ДДС продавач": 20%, 9%, 0% — and 0 for the words that mean „без ДДС"
    /// (необлагаема, освободена, ВОД …). Null when the cell is empty.
    /// </summary>
    public static decimal? ParseRate(object? cell)
    {
        if (CellValues.IsNumeric(cell))
        {
            decimal n = CellValues.NumberOrZero(cell);
            return n is > 0m and < 1m ? n * 100m : n;   // 0.2 is 20% formatted as a percentage
        }

        string text = CellValues.Norm(cell);
        if (text.Length == 0) return null;

        int percent = text.IndexOf('%');
        if (percent > 0)
        {
            int from = percent;
            while (from > 0 && (char.IsAsciiDigit(text[from - 1]) || text[from - 1] is '.' or ',' or ' ')) from--;
            string number = text[from..percent].Trim().Replace(',', '.');
            if (decimal.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal rate)) return rate;
        }

        return text.Any(char.IsLetter) ? 0m : CellValues.Number(text) ?? 0m;
    }
}
