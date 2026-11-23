using MicroinvestConvert.Core.Checks;

namespace MicroinvestConvert.Core.Model;

public enum OperationKind
{
    Purchase,
    Sale,

    /// <summary>Purchases from abroad: only the invoice is written, its VAT is a protocol in Делта Pro.</summary>
    Foreign,
}

public static class OperationKinds
{
    public static readonly IReadOnlyList<OperationKind> All = [OperationKind.Purchase, OperationKind.Sale, OperationKind.Foreign];

    /// <summary>The name of the module, as the user sees it.</summary>
    public static string Title(this OperationKind kind) => kind switch
    {
        OperationKind.Purchase => "Покупки",
        OperationKind.Sale => "Продажби",
        _ => "Чуждестранни",
    };

    /// <summary>Many debits against one supplier. A sale is one customer against many credits.</summary>
    public static bool IsPurchaseSide(this OperationKind kind) => kind != OperationKind.Sale;
}

public enum PaymentMethod
{
    Unknown,
    Bank,
    Cash,
    Card,
}

public static class PaymentMethods
{
    public static PaymentMethod FromText(string? text)
    {
        string s = (text ?? "").ToLowerInvariant();
        if (s.Contains("брой")) return PaymentMethod.Cash;
        if (s.Contains("карта")) return PaymentMethod.Card;
        if (s.Contains("банк") || s.Contains("сметка") || s.Contains("налож")) return PaymentMethod.Bank;
        return PaymentMethod.Unknown;
    }

    public static string Label(PaymentMethod method) => method switch
    {
        PaymentMethod.Bank => "Банка",
        PaymentMethod.Cash => "В брой",
        PaymentMethod.Card => "Карта",
        _ => "",
    };

    public static readonly IReadOnlyList<PaymentMethod> All =
        [PaymentMethod.Unknown, PaymentMethod.Bank, PaymentMethod.Cash, PaymentMethod.Card];
}

/// <summary>
/// The ledger column(s) the file put an amount in. One source row with values in
/// several of them becomes several lines — every line has exactly one bucket.
/// </summary>
public enum LedgerBucket
{
    // purchases
    PurchaseNoCredit,
    PurchaseFullCredit,
    PurchasePartialCredit,

    /// <summary>„без ДДС дневник" = ДА.</summary>
    PurchaseNoDiary,

    /// <summary>Обратно начисляване по чл. 163а.</summary>
    PurchaseReverseCharge,

    /// <summary>An amount with nothing in the ledger columns.</summary>
    PurchaseUnspecified,

    // sales
    Sale20,
    Sale9,
    SaleIntraCommunity,
    SaleExport,
    SaleArticles140,
    SaleUntaxedEu,
    SaleUntaxedNonEu,
    SaleExempt,

    /// <summary>An amount with nothing in the ledger columns.</summary>
    SaleNoDiary,

    /// <summary>An invoice of the foreign export, in EUR.</summary>
    Foreign,
}

public static class LedgerBuckets
{
    /// <summary>The source column(s) and what they mean, for the user.</summary>
    public static string Title(LedgerBucket bucket) => bucket switch
    {
        LedgerBucket.PurchaseNoCredit => "AB · без ДК",
        LedgerBucket.PurchaseFullCredit => "AC/AD · пълен ДК",
        LedgerBucket.PurchasePartialCredit => "AE/AF · частичен ДК",
        LedgerBucket.PurchaseNoDiary => "AG · без дневник",
        LedgerBucket.PurchaseReverseCharge => "чл. 163а",
        LedgerBucket.PurchaseUnspecified => "няма (AB–AG)",
        LedgerBucket.Sale20 => "AB/AC · 20%",
        LedgerBucket.Sale9 => "AD/AE · 9%",
        LedgerBucket.SaleIntraCommunity => "AF · ВОД",
        LedgerBucket.SaleExport => "AG · износ 0%",
        LedgerBucket.SaleArticles140 => "AH · межд. транспорт",
        LedgerBucket.SaleUntaxedEu => "AI · необл. в ЕС",
        LedgerBucket.SaleUntaxedNonEu => "AJ · необл. извън ЕС",
        LedgerBucket.SaleExempt => "освободена",
        LedgerBucket.SaleNoDiary => "няма",
        LedgerBucket.Foreign => "AI · сума в евро",
        _ => bucket.ToString(),
    };

    /// <summary>The column of дневник продажби the bucket feeds; 0 when none.</summary>
    public static int SalesLedgerColumn(LedgerBucket bucket) => bucket switch
    {
        LedgerBucket.Sale20 => 11,
        LedgerBucket.Sale9 => 17,
        LedgerBucket.SaleExport => 19,
        LedgerBucket.SaleIntraCommunity => 20,
        LedgerBucket.SaleArticles140 => 21,
        LedgerBucket.SaleUntaxedEu => 22,
        LedgerBucket.SaleUntaxedNonEu => 23,
        LedgerBucket.SaleExempt => 24,
        _ => 0,
    };
}

public enum IssueSeverity
{
    /// <summary>Worth knowing; nothing to do.</summary>
    Info,

    /// <summary>The document is written, but somebody should look at it.</summary>
    Warning,

    /// <summary>The document is not written until this is fixed.</summary>
    Error,
}

/// <summary>Every check of the program. What each one means is in <see cref="CheckCatalog"/>.</summary>
public enum IssueCode
{
    // document
    MissingDocumentType,
    DocumentTypeConflict,
    MissingNumber,
    PaddedNumber,
    Duplicate,
    MissingReason,
    ReasonFromAccount,

    // dates
    MissingDate,
    MissingPeriod,
    PeriodBeforeDocument,

    // partner
    MissingPartner,
    EikCheckDigit,
    EikLength,
    ForeignSupplier,
    NotForeign,
    ViesPartner,

    // accounts
    MissingAccount,
    MissingCounterAccount,
    MissingCashAccount,
    BadAccount,

    // amounts
    ForeignCurrency,
    AmountsDoNotAdd,
    ZeroDocument,
    NegativeDocument,
    SignFlipped,
    EuroTotalDiffers,
    EuroRate,
    ConvertedToEuro,
    SellerVatIncluded,

    // VAT
    VatUnresolved,
    VatZeroButDeclared,
    VatDifferentRate,
    VatMismatch,
    VatAtZeroDeal,
    VatWithinTolerance,

    // deal type and VIES
    NoDiary,
    NoDiaryWithAmounts,
    ForeignNoDiary,
    NotMarkedNoDiary,
    OutsideLedger,
    Unspecified,
    ReverseCharge,
    MixedRow,
    JournalCellMismatch,
    ViesMixed,
    Overridden,
}

/// <summary>What a check found in a document.</summary>
public sealed record Issue(IssueCode Code, string Message)
{
    public IssueSeverity Severity => CheckCatalog.Get(Code).Severity;

    /// <summary>The user looked at it and ticked it off.</summary>
    public bool Reviewed { get; init; }

    /// <summary>Waits for somebody: an error, or a warning nobody has ticked off.</summary>
    public bool IsOpen => Severity == IssueSeverity.Error || (Severity == IssueSeverity.Warning && !Reviewed);
}
