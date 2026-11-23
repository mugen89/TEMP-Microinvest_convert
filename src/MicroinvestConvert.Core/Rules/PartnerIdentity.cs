using System.Text;

namespace MicroinvestConvert.Core.Rules;

/// <summary>
/// The „ЕИК / ДДС номер" cell split into column E (ЕИК) and column F (ДДС номер).
/// </summary>
public readonly record struct PartnerIdentity(string Eik, string VatNumber)
{
    private static readonly HashSet<string> EuPrefixes = new(StringComparer.Ordinal)
    {
        "AT", "BE", "BG", "CY", "CZ", "DE", "DK", "EE", "EL", "ES", "FI", "FR", "HR", "HU", "IE", "IT",
        "LT", "LU", "LV", "MT", "NL", "PL", "PT", "RO", "SE", "SI", "SK",
        "XI", // Северна Ирландия (стоки)
    };

    public static PartnerIdentity Parse(string? raw)
    {
        string id = Normalise(raw);
        if (id.Length == 0) return new PartnerIdentity("", "");

        // BG123456786 → ЕИК 123456786 and ДДС BG123456786
        if (id.StartsWith("BG", StringComparison.Ordinal) && id.Length > 2 && AllDigits(id, 2))
            return new PartnerIdentity(id[2..], id);

        // 123456786 → ЕИК (or ЕГН) only
        if (AllDigits(id, 0)) return new PartnerIdentity(id, "");

        // 999999999999999Name → the „няма номер" placeholder with a suffix
        if (id.StartsWith("9999999999999", StringComparison.Ordinal))
            return new PartnerIdentity(new string(id.TakeWhile(char.IsAsciiDigit).ToArray()), "");

        return new PartnerIdentity("", id);
    }

    /// <summary>Two letters other than BG at the start.</summary>
    public bool IsForeign => Prefix.Length == 2 && Prefix != "BG";

    public string Prefix => VatNumber.Length > 2 && char.IsAsciiLetterUpper(VatNumber[0]) && char.IsAsciiLetterUpper(VatNumber[1])
        ? VatNumber[..2]
        : "";

    /// <summary>A VAT number of another member state — what VIES needs.</summary>
    public bool IsOtherEuMember => IsForeign && EuPrefixes.Contains(Prefix);

    /// <summary>VAT number BG…, or an ЕИК with the right check digit.</summary>
    public bool IsBulgarian => Prefix == "BG" ||
        (VatNumber.Length == 0 && Eik.Length is 9 or 13 && !IsPlaceholder(Eik) && HasValidEikCheckDigit(Eik[..9]));

    /// <summary>„Няма номер" – nines only, as the VAT ledgers want it.</summary>
    public static bool IsPlaceholder(string eik) => eik.Length >= 9 && eik.All(c => c == '9');

    /// <summary>
    /// Digits that cannot be a Bulgarian number by their count: an ЕИК has 9 or 13, an ЕГН 10.
    /// </summary>
    public static bool HasPossibleLength(string eik) =>
        eik.Length == 0 || IsPlaceholder(eik) || eik.Length is 9 or 10 or 13 || !eik.All(char.IsAsciiDigit);

    /// <summary>
    /// The check digit of a 9-digit ЕИК. Other lengths (ЕГН, ЕИК на клон, placeholders) are
    /// not judged here.
    /// </summary>
    public static bool HasValidEikCheckDigit(string eik)
    {
        if (eik.Length != 9 || !AllDigits(eik, 0)) return true;
        if (eik.Distinct().Count() == 1) return true;   // 999999999 and the like

        int Sum(int firstWeight)
        {
            int sum = 0;
            for (int i = 0; i < 8; i++) sum += (eik[i] - '0') * (firstWeight + i);
            return sum % 11;
        }

        int check = Sum(1);
        if (check == 10)
        {
            check = Sum(3);
            if (check == 10) check = 0;
        }

        return check == eik[8] - '0';
    }

    private static bool AllDigits(string text, int from)
    {
        for (int i = from; i < text.Length; i++)
            if (!char.IsAsciiDigit(text[i])) return false;
        return text.Length > from;
    }

    // Spaces and separators go; Cyrillic letters that look Latin become Latin („ВG" is a
    // usual OCR slip).
    private static string Normalise(string? raw)
    {
        var sb = new StringBuilder();
        foreach (char ch in (raw ?? "").ToUpperInvariant())
        {
            if (char.IsWhiteSpace(ch) || ch is '-' or '.' or '/' or ',') continue;
            int i = "АВЕКМНОРСТХ".IndexOf(ch);
            sb.Append(i >= 0 ? "ABEKMHOPCTX"[i] : ch);
        }

        return sb.ToString();
    }
}
