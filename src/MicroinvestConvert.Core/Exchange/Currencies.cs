namespace MicroinvestConvert.Core.Exchange;

/// <summary>Currency codes. The books are in euro: whatever a document is in, it is written in euro.</summary>
public static class Currencies
{
    public const string Euro = "EUR";
    public const string Lev = "BGN";

    /// <summary>Leva for one euro, fixed by law: needs no table.</summary>
    public const decimal LevaPerEuro = 1.95583m;

    public static string Normalise(string? currency) => (currency ?? "").Trim().ToUpperInvariant() switch
    {
        "" => "",
        "EUR" or "EURO" or "ЕВРО" or "€" => Euro,
        "BGN" or "ЛВ" or "ЛВ." or "ЛЕВ" or "ЛЕВА" => Lev,
        var other => other,
    };

    /// <summary>Three Latin letters, as the tables of rates have them.</summary>
    public static bool IsCode(string text) => text.Length == 3 && text.All(char.IsAsciiLetterUpper);
}
