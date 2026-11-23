namespace MicroinvestConvert.Core.Model;

public enum OperationKind
{
    Purchase,
    Sale,
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

public enum IssueCode
{
    MissingNumber,
    MissingDate,
    MissingPartner,
    MissingDocumentType,
    DocumentTypeConflict,
    PaddedNumber,
    MissingPeriod,
    PeriodBeforeDocument,
    MissingAccount,
    MissingCounterAccount,
    MissingCashAccount,
    BadAccount,
    MissingReason,
    ForeignCurrency,
    ReverseCharge,
    ForeignSupplier,
    NoDiary,
    Unspecified,
    MixedRow,
    VatWithinTolerance,
    VatUnresolved,
    VatZeroButDeclared,
    VatDifferentRate,
    VatMismatch,
    PartialCredit,
    JournalCellMismatch,
    ViesPartner,
    ViesMixed,
    AmountsDoNotAdd,
    ZeroDocument,
    SignFlipped,
    NegativeDocument,
    EikCheckDigit,
    Duplicate,
    Overridden,
}

public sealed record Issue(IssueSeverity Severity, IssueCode Code, string Message);
