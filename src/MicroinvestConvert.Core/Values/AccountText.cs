namespace MicroinvestConvert.Core.Values;

/// <summary>
/// An account (or a sub-account) as people type it. Only what cannot mean anything else is
/// cleaned: spaces around a „/", doubled „/", a „/" at either end. The digits are never
/// touched — 4111 is the account 4111, not 411/1 — and anything doubtful (a space between
/// digits, another separator) stays, so that it is reported and not guessed.
/// </summary>
public static class AccountText
{
    public const char Separator = '/';

    public static string Normalise(string? raw)
    {
        string text = CellValues.Norm(raw);
        if (!text.Contains(Separator)) return text;

        var levels = text.Split(Separator).Select(l => l.Trim()).Where(l => l.Length > 0);
        return string.Join(Separator, levels);
    }

    /// <summary>
    /// The main account and its sub-account: 4111 and 1 → 4111/1; 411/1 and 1 → 411/1/1; 4111
    /// and nothing → 4111.
    /// </summary>
    public static string Join(string? account, string? subAccount)
    {
        string main = Normalise(account), sub = Normalise(subAccount);
        return main.Length == 0 || sub.Length == 0 ? main : main + Separator + sub;
    }
}
