using MicroinvestConvert.Core.Settings;
using MicroinvestConvert.Core.Values;

namespace MicroinvestConvert.Core.Rules;

/// <summary>
/// Accounts as Делта Pro writes them: the main account, then every sub-account after a „/".
/// The main account is the one of the file, whatever its length: 4111 stays 4111.
/// </summary>
public static class AccountCodes
{
    /// <summary>Делта Pro allows five levels under the synthetic account.</summary>
    private const int MaxLevels = 6;

    /// <summary>
    /// The account of the file as it goes into column B. A mapping chosen by the user wins
    /// (first for the account with its sub-account, then for the account alone).
    /// </summary>
    public static string Format(string account, string subAccount, ConverterOptions options)
    {
        string main = AccountText.Normalise(account), sub = AccountText.Normalise(subAccount);
        if (main.Length == 0) return "";

        if (sub.Length > 0 && options.MappedAccount(AccountText.Join(main, sub)) is { } whole) return whole;
        return AccountText.Join(options.MappedAccount(main) ?? main, sub);
    }

    /// <summary>Why the account cannot be written in column B; null when it can.</summary>
    public static string? Problem(string formatted)
    {
        if (formatted.Length == 0) return "липсва";

        // ';' '-' '%' and '|' are the grammar of columns A and B.
        foreach (char ch in formatted)
            if (!char.IsLetterOrDigit(ch) && ch != '/')
                return $"знакът „{ch}“ не е позволен";

        string[] levels = formatted.Split('/');
        if (levels.Any(l => l.Length == 0)) return "има празно ниво („//“ или „/“ в края)";
        if (levels.Length > MaxLevels) return "има повече от пет аналитични нива";
        return null;
    }
}
