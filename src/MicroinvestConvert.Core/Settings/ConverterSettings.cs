using MicroinvestConvert.Core.Nomenclature;

namespace MicroinvestConvert.Core.Settings;

/// <summary>How an expected VAT amount is rounded to cents.</summary>
public enum VatRounding
{
    /// <summary>0.005 → 0.01 (математическо закръгляне).</summary>
    HalfUp,

    /// <summary>0.005 → 0.00, 0.015 → 0.02 (банково закръгляне).</summary>
    HalfEven,

    /// <summary>0.009 → 0.00 (отрязване).</summary>
    Truncate,
}

/// <summary>What column N looks like when a document has several amounts.</summary>
public enum DealTypeFormat
{
    /// <summary>One value per amount: „2;2". What the previous converter wrote.</summary>
    PerAmount,

    /// <summary>One value when all are the same („2"), otherwise one per amount.</summary>
    SingleWhenEqual,
}

/// <summary>How the VAT of a document is split over the amounts in column A.</summary>
public enum VatLines
{
    /// <summary>One VAT amount per deal type: 602;621;453/1.</summary>
    PerDealType,

    /// <summary>One VAT amount after every account: 602;453/1;621;453/1.</summary>
    PerAccount,
}

/// <summary>What happens with a purchase marked „без ДДС дневник" in the file.</summary>
public enum NoDiaryMode
{
    /// <summary>Вид сделка 1 – вътрешен оборот, not in any ledger.</summary>
    Internal,

    /// <summary>Вид сделка 3 – in the ledger, without tax credit.</summary>
    NoCredit,

    /// <summary>The document is left out of the file.</summary>
    Skip,
}

/// <summary>The settings that apply to every client.</summary>
public sealed class ConverterSettings
{
    public const string DefaultPurchaseVatAccount = "453/1";
    public const string DefaultSaleVatAccount = "453/2";

    public string PurchaseVatAccount { get; set; } = DefaultPurchaseVatAccount;
    public string SaleVatAccount { get; set; } = DefaultSaleVatAccount;

    /// <summary>4111 → 411/1: what follows the third digit becomes a sub-account.</summary>
    public bool SplitLongAccounts { get; set; } = true;

    public VatRounding Rounding { get; set; } = VatRounding.HalfUp;

    /// <summary>Allowed difference between the VAT in the file and the calculated one, for a whole document.</summary>
    public decimal TolerancePerDocument { get; set; } = 0.05m;

    /// <summary>The same for a single line of a document with many lines.</summary>
    public decimal TolerancePerLine { get; set; } = 0.01m;

    /// <summary>Column C takes the document date; the tax period of the file is ignored.</summary>
    public bool AccountingDateEqualsDocumentDate { get; set; }

    /// <summary>A document paid in cash gets a second row (РКО / ПКО).</summary>
    public bool CashRows { get; set; } = true;

    /// <summary>Used when a document is paid in cash and the file has no cash account.</summary>
    public string DefaultCashAccount { get; set; } = "";

    public DealTypeFormat DealTypeFormat { get; set; } = DealTypeFormat.PerAmount;
    public VatLines VatLines { get; set; } = VatLines.PerDealType;
    public NoDiaryMode NoDiary { get; set; } = NoDiaryMode.Internal;

    /// <summary>Account in the file (with its analytic, e.g. 601/5) → account in Делта Pro.</summary>
    public Dictionary<string, string> Accounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public ConverterSettings Clone()
    {
        var copy = (ConverterSettings)MemberwiseClone();
        copy.Accounts = new Dictionary<string, string>(Accounts, StringComparer.OrdinalIgnoreCase);
        return copy;
    }
}

/// <summary>What is different for one client. Empty = as for everybody.</summary>
public sealed class ClientSettings
{
    public string Name { get; set; } = "";
    public string PurchaseVatAccount { get; set; } = "";
    public string SaleVatAccount { get; set; } = "";
    public Dictionary<string, string> Accounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsEmpty =>
        PurchaseVatAccount.Length == 0 && SaleVatAccount.Length == 0 && Accounts.Count == 0;

    public ClientSettings Clone()
    {
        var copy = (ClientSettings)MemberwiseClone();
        copy.Accounts = new Dictionary<string, string>(Accounts, StringComparer.OrdinalIgnoreCase);
        return copy;
    }
}

/// <summary>The settings in force for one file: the general ones with the exceptions of its client on top.</summary>
public sealed class ConverterOptions
{
    public ConverterSettings General { get; }
    public ClientSettings? Client { get; }

    public ConverterOptions(ConverterSettings general, ClientSettings? client = null)
    {
        General = general;
        Client = client;
    }

    public static ConverterOptions Default => new(new ConverterSettings());

    public string PurchaseVatAccount => Pick(Client?.PurchaseVatAccount, General.PurchaseVatAccount);
    public string SaleVatAccount => Pick(Client?.SaleVatAccount, General.SaleVatAccount);

    /// <summary>True when a VAT account in force is not the standard 453/1 or 453/2.</summary>
    public bool HasVatAccountException =>
        PurchaseVatAccount != ConverterSettings.DefaultPurchaseVatAccount ||
        SaleVatAccount != ConverterSettings.DefaultSaleVatAccount;

    public int NoDiaryDealType =>
        General.NoDiary == NoDiaryMode.NoCredit ? DealTypes.PurchaseNoCredit : DealTypes.Internal;

    /// <summary>The account the user mapped <paramref name="key"/> to, or null.</summary>
    public string? MappedAccount(string key)
    {
        if (Client != null && Client.Accounts.TryGetValue(key, out var own) && own.Length > 0) return own;
        return General.Accounts.TryGetValue(key, out var common) && common.Length > 0 ? common : null;
    }

    private static string Pick(string? own, string common) => string.IsNullOrWhiteSpace(own) ? common : own;
}
