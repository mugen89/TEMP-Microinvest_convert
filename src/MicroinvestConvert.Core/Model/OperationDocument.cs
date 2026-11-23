using System.Globalization;
using MicroinvestConvert.Core.Nomenclature;
using MicroinvestConvert.Core.Values;

namespace MicroinvestConvert.Core.Model;

/// <summary>The fields of a document the user can correct.</summary>
public enum DocField
{
    DocType,
    Number,
    Date,
    Period,
    AccountingDate,
    PartnerName,
    Eik,
    VatNumber,
    Currency,
    Payment,
    CounterAccount,
    CashAccount,
    Reason,
    CashReason,
    Note,
    Note2,
    Vies,
}

/// <summary>One document of the file — one row of „Операции" (two when it is paid in cash).</summary>
public sealed class OperationDocument
{
    // ----- identity -----

    public required OperationKind Kind { get; init; }

    /// <summary>„№" of the file (column A).</summary>
    public string Index { get; init; } = "";
    public string SourceDocType { get; init; } = "";
    public int? SourceDocCode { get; init; }

    /// <summary>„ЕИК / ДДС номер" exactly as in the file.</summary>
    public string PartnerId { get; init; } = "";

    // ----- editable -----

    public string DocType { get; set; } = "";
    public string Number { get; set; } = "";
    public DateTime? Date { get; set; }

    /// <summary>First day of the tax period.</summary>
    public DateTime? Period { get; set; }

    /// <summary>Set by hand; null = by the rule.</summary>
    public DateTime? AccountingDateOverride { get; set; }
    public string PartnerName { get; set; } = "";
    public string Eik { get; set; } = "";
    public string VatNumber { get; set; } = "";
    public string Currency { get; set; } = "";
    public PaymentMethod Payment { get; set; }

    private string _counterAccount = "", _cashAccount = "";

    /// <summary>401 / 411 as in the file.</summary>
    public string CounterAccount { get => _counterAccount; set => _counterAccount = AccountText.Normalise(value); }
    public string CashAccount { get => _cashAccount; set => _cashAccount = AccountText.Normalise(value); }
    public string Reason { get; set; } = "";
    public string CashReason { get; set; } = "";
    public string Note { get; set; } = "";
    public string Note2 { get; set; } = "";

    /// <summary>VIES set by hand; null = by the deal type. Sales only.</summary>
    public int? ViesOverride { get; set; }

    /// <summary>Unticked by the user = not written.</summary>
    public bool Export { get; set; } = true;

    public List<LedgerLine> Lines { get; } = [];

    /// <summary>Remarks made while reading the row (kept through every recalculation).</summary>
    public List<Issue> ReadIssues { get; } = [];

    // ----- calculated by DocumentProcessor -----

    /// <summary>Column C, and column P.</summary>
    public DateTime? AccountingDate { get; internal set; }

    /// <summary>The amounts and accounts of columns A and B, in their order.</summary>
    public IReadOnlyList<EntrySegment> Segments { get; internal set; } = [];
    public IReadOnlyList<VatGroup> VatGroups { get; internal set; } = [];
    public List<Issue> Issues { get; } = [];

    /// <summary>The accounts as they go into the file.</summary>
    public string CounterAccountOut { get; internal set; } = "";
    public string CashAccountOut { get; internal set; } = "";

    public decimal TotalBase { get; internal set; }
    public decimal TotalVat { get; internal set; }
    public decimal TotalGross { get; internal set; }

    /// <summary>Left out because of the „без дневник" setting.</summary>
    public bool SkippedBySetting { get; internal set; }

    public bool HasErrors => Issues.Any(i => i.Severity == IssueSeverity.Error);
    public bool HasWarnings => Issues.Any(i => i.Severity == IssueSeverity.Warning);

    /// <summary>The document goes into the file.</summary>
    public bool WillExport => Export && !SkippedBySetting && !HasErrors;

    public bool IsCash => Payment == PaymentMethod.Cash;

    /// <summary>The distinct deal types of the document, in the order of its amounts.</summary>
    public IEnumerable<int> DealTypesUsed => Segments.Select(s => s.DealType).Distinct();

    public string Title => (Number.Length > 0 ? Number : "№ " + Index) + (PartnerName.Length > 0 ? " · " + PartnerName : "");

    // ----- editing -----

    private readonly Dictionary<DocField, string> _original = [];
    private List<LedgerLine> _originalLines = [];

    internal void RememberOriginal()
    {
        foreach (var field in Enum.GetValues<DocField>()) _original[field] = GetText(field);
        foreach (var line in Lines) line.RememberOriginal();
        _originalLines = [.. Lines];
    }

    public bool IsEdited(DocField field) => _original.TryGetValue(field, out var was) && was != GetText(field);

    public bool AnyEdited =>
        Enum.GetValues<DocField>().Any(IsEdited) || Lines.Any(l => l.AnyEdited) || Lines.Count != _originalLines.Count;

    /// <summary>Back to what the file said.</summary>
    public void Revert()
    {
        foreach (var (field, was) in _original) TrySet(field, was, out _);
        RevertLines();
        Export = true;
    }

    /// <summary>The amounts, accounts and deal types as the file had them; the rest stays.</summary>
    public void RevertLines()
    {
        Lines.Clear();
        Lines.AddRange(_originalLines);
        foreach (var line in Lines) line.Revert();
    }

    /// <summary>
    /// Splits a line whose VAT belongs to a part of its base only: the part the VAT is
    /// <paramref name="rate"/> percent of, and the rest without VAT. Null when there is no such split.
    /// </summary>
    public static (decimal Taxed, decimal Untaxed)? SplitOf(LedgerLine line, decimal rate)
    {
        if (line.Vat == 0m || rate <= 0m) return null;
        decimal taxed = Math.Round(line.Vat * 100m / rate, 2, MidpointRounding.AwayFromZero);
        decimal rest = line.Base - taxed;
        return rest != 0m && Math.Sign(rest) == Math.Sign(line.Base) && Math.Sign(taxed) == Math.Sign(line.Base)
            ? (taxed, rest)
            : null;
    }

    /// <summary>Does the split of <see cref="SplitOf"/>; the two parts get the given deal types.</summary>
    public bool Split(LedgerLine line, decimal rate, int taxedDeal, int restDeal)
    {
        int at = Lines.IndexOf(line);
        if (at < 0 || SplitOf(line, rate) is not { } split) return false;

        Lines[at] = line.Part(split.Taxed, line.Vat, rate, taxedDeal, line.SellerTotal);
        Lines.Insert(at + 1, line.Part(split.Untaxed, 0m, 0m, restDeal, 0m));
        return true;
    }

    private const string DateFormat = "dd.MM.yyyy";
    private const string PeriodFormat = "MM.yyyy";

    public string GetText(DocField field) => field switch
    {
        DocField.DocType => DocType,
        DocField.Number => Number,
        DocField.Date => Date?.ToString(DateFormat, CultureInfo.InvariantCulture) ?? "",
        DocField.Period => Period?.ToString(PeriodFormat, CultureInfo.InvariantCulture) ?? "",
        DocField.AccountingDate => AccountingDateOverride?.ToString(DateFormat, CultureInfo.InvariantCulture) ?? "",
        DocField.PartnerName => PartnerName,
        DocField.Eik => Eik,
        DocField.VatNumber => VatNumber,
        DocField.Currency => Currency,
        DocField.Payment => PaymentMethods.Label(Payment),
        DocField.CounterAccount => CounterAccount,
        DocField.CashAccount => CashAccount,
        DocField.Reason => Reason,
        DocField.CashReason => CashReason,
        DocField.Note => Note,
        DocField.Note2 => Note2,
        DocField.Vies => ViesOverride?.ToString(CultureInfo.InvariantCulture) ?? "",
        _ => "",
    };

    /// <summary>Sets a field from typed or pasted text. False with a reason when the text makes no sense.</summary>
    public bool TrySet(DocField field, string? text, out string error)
    {
        error = "";
        string value = CellValues.Clean(text);

        switch (field)
        {
            case DocField.DocType:
                // „Ф-ра", „Фактура" or „Ф-ра – Фактура"
                var type = DocumentTypes.Find(value) ?? DocumentTypes.All.FirstOrDefault(t => t.Label == value);
                if (type == null && value.Length > 0)
                {
                    error = $"„{value}“ не е тип документ от номенклатурата на Делта Pro.";
                    return false;
                }

                DocType = type?.ShortName ?? "";
                return true;

            case DocField.Number:
                Number = value;
                return true;

            case DocField.Date:
                return SetDate(value, d => Date = d, out error);

            case DocField.AccountingDate:
                return SetDate(value, d => AccountingDateOverride = d, out error);

            case DocField.Period:
                if (value.Length == 0)
                {
                    Period = null;
                    return true;
                }

                if (CellValues.Period(value) is not { } period)
                {
                    error = $"„{value}“ не е период. Въведете месец и година, например 06.2026.";
                    return false;
                }

                Period = period;
                return true;

            case DocField.PartnerName:
                PartnerName = value;
                return true;
            case DocField.Eik:
                Eik = value;
                return true;
            case DocField.VatNumber:
                VatNumber = value.ToUpperInvariant();
                return true;
            case DocField.Currency:
                Currency = value.ToUpperInvariant();
                return true;

            case DocField.Payment:
                foreach (var method in PaymentMethods.All)
                    if (string.Equals(PaymentMethods.Label(method), value, StringComparison.OrdinalIgnoreCase))
                    {
                        Payment = method;
                        return true;
                    }

                var guessed = PaymentMethods.FromText(value);
                if (guessed == PaymentMethod.Unknown)
                {
                    error = $"„{value}“ не е начин на плащане (Банка, В брой, Карта).";
                    return false;
                }

                Payment = guessed;
                return true;

            case DocField.CounterAccount:
                CounterAccount = value;
                return true;
            case DocField.CashAccount:
                CashAccount = value;
                return true;
            case DocField.Reason:
                Reason = value;
                return true;
            case DocField.CashReason:
                CashReason = value;
                return true;
            case DocField.Note:
                Note = value;
                return true;
            case DocField.Note2:
                Note2 = value;
                return true;

            case DocField.Vies:
                if (value.Length == 0)
                {
                    ViesOverride = null;
                    return true;
                }

                if (!int.TryParse(value.Split(' ', '-')[0], NumberStyles.None, CultureInfo.InvariantCulture, out int vies) ||
                    !ViesTypes.IsValid(vies))
                {
                    error = $"„{value}“ не е стойност за VIES (1–4).";
                    return false;
                }

                ViesOverride = vies;
                return true;

            default:
                return false;
        }
    }

    private static bool SetDate(string value, Action<DateTime?> set, out string error)
    {
        error = "";
        if (value.Length == 0)
        {
            set(null);
            return true;
        }

        if (CellValues.Date(value) is not { } date)
        {
            error = $"„{value}“ не е дата. Въведете я като 05.06.2026.";
            return false;
        }

        set(date);
        return true;
    }
}

/// <summary>One amount of columns A/B: an account, its amount and its deal type.</summary>
public sealed class EntrySegment
{
    /// <summary>The account as written in column B.</summary>
    public required string Account { get; init; }
    public required string AccountName { get; init; }
    public required bool IsVat { get; init; }
    public required int DealType { get; init; }
    public required int Vies { get; init; }
    public required IReadOnlyList<LedgerLine> Lines { get; init; }

    /// <summary>Before rounding.</summary>
    internal decimal Raw { get; init; }

    /// <summary>In cents, as written in column A.</summary>
    public decimal Amount { get; internal set; }
}

public enum VatStatus
{
    /// <summary>No amounts at all.</summary>
    Empty,

    /// <summary>The VAT is exactly the rate.</summary>
    Exact,

    /// <summary>Off by cents, within the allowed difference.</summary>
    WithinTolerance,

    /// <summary>No VAT.</summary>
    Zero,

    /// <summary>The VAT fits no rate.</summary>
    Unresolved,
}

/// <summary>The VAT control of a part of a document — what the „Проверка на ДДС" window lists.</summary>
public sealed class VatGroup
{
    public required OperationDocument Document { get; init; }
    public required LedgerBucket Bucket { get; init; }
    public required IReadOnlyList<LedgerLine> Lines { get; init; }
    public required VatStatus Status { get; init; }

    public required decimal Base { get; init; }
    public required decimal Vat { get; init; }
    public required decimal? DeclaredRate { get; init; }

    /// <summary>The rate the amounts correspond to; null when none does.</summary>
    public required decimal? Rate { get; init; }

    /// <summary>The VAT this rate gives (the declared or the nearest rate when unresolved).</summary>
    public required decimal Expected { get; init; }

    /// <summary>VAT in the file minus the expected one.</summary>
    public decimal Difference => Vat - Expected;

    /// <summary>VAT as a percentage of the base; null when the base is zero.</summary>
    public decimal? Percent => Base == 0m ? null : Math.Round(Vat / Base * 100m, 2, MidpointRounding.AwayFromZero);

    public bool NeedsLook => Status is VatStatus.WithinTolerance or VatStatus.Unresolved ||
                             (Status == VatStatus.Zero && DeclaredRate > 0m);
}

/// <summary>A whole file.</summary>
public sealed class OperationBatch
{
    public required OperationKind Kind { get; init; }
    public required string SourcePath { get; init; }
    public required string SheetName { get; init; }
    public required string LayoutName { get; init; }
    public string ClientName { get; init; } = "";
    public string ClientEik { get; init; } = "";

    public List<OperationDocument> Documents { get; } = [];

    /// <summary>Rows with content that were not read as data, with the reason.</summary>
    public List<SkippedRow> SkippedRows { get; } = [];
}

public sealed record SkippedRow(int Row, string Reason);
