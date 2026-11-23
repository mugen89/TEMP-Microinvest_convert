using MicroinvestConvert.Core.Settings;

namespace MicroinvestConvert.Core.Rules;

/// <summary>
/// Accounts as Делта Pro writes them: the synthetic account, then every sub-account
/// after a „/" — 401/1/5.
/// </summary>
public static class AccountCodes
{
    /// <summary>Digits of a synthetic account in the national chart of accounts.</summary>
    private const int SyntheticDigits = 3;

    /// <summary>Делта Pro allows five levels under the synthetic account.</summary>
    private const int MaxLevels = 6;

    /// <summary>
    /// The account of the file as it goes into column B. A mapping chosen by the user wins
    /// (first for the account with its analytic, then for the account alone).
    /// </summary>
    public static string Format(string account, string analytic, ConverterOptions options)
    {
        account = account.Trim();
        analytic = analytic.Trim();
        if (account.Length == 0) return "";

        if (analytic.Length > 0 && options.MappedAccount(account + "/" + analytic) is { } whole) return whole;

        string main = options.MappedAccount(account) ?? Split(account, options);
        return analytic.Length == 0 ? main : main + "/" + analytic;
    }

    private static string Split(string account, ConverterOptions options)
    {
        if (!options.General.SplitLongAccounts) return account;
        if (account.Length <= SyntheticDigits || !account.All(char.IsAsciiDigit)) return account;
        return account[..SyntheticDigits] + "/" + account[SyntheticDigits..];
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
