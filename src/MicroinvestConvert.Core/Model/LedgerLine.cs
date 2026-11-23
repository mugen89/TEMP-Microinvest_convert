using System.Globalization;
using MicroinvestConvert.Core.Nomenclature;
using MicroinvestConvert.Core.Values;

namespace MicroinvestConvert.Core.Model;

public enum LineField
{
    Account,
    Analytic,
    Base,
    Vat,
    DealType,
}

/// <summary>
/// One amount of a document: a source row, or the part of it that belongs to one
/// ledger bucket. The user corrects these; everything else is calculated from them.
/// </summary>
public sealed class LedgerLine
{
    // ----- from the file -----

    /// <summary>1-based row of the sheet.</summary>
    public int SourceRow { get; init; }
    public LedgerBucket Bucket { get; init; }

    private string _account = "", _analytic = "";

    /// <summary>The main account („Код на счетоводна сметка"), with its „/" cleaned.</summary>
    public string Account { get => _account; set => _account = AccountText.Normalise(value); }
    public string AccountName { get; init; } = "";

    /// <summary>The sub-account („Код на крайна аналитичност").</summary>
    public string Analytic { get => _analytic; set => _analytic = AccountText.Normalise(value); }
    public string AnalyticName { get; init; } = "";
    public string Article { get; init; } = "";

    /// <summary>Base of the ledger bucket; for „без кредит" and „без дневник" — the gross amount.</summary>
    public decimal Base { get; set; }
    public decimal Vat { get; set; }

    /// <summary>Text of „% ДДС продавач" as in the file.</summary>
    public string RateText { get; init; } = "";

    /// <summary>The rate the file declares: 20, 9, 0; null when it says nothing.</summary>
    public decimal? DeclaredRate { get; init; }

    /// <summary>Text of „Вид на ДДС (третиране)".</summary>
    public string Treatment { get; init; } = "";

    /// <summary>Sales: „Клетки в дневник продажби", e.g. „11,12".</summary>
    public string JournalCell { get; init; } = "";

    /// <summary>„Обща сума продавач" of the source row, for the control of the totals.</summary>
    public decimal SellerTotal { get; init; }

    /// <summary>„ДДС продавач" of the source row; 0 when the row was split.</summary>
    public decimal SellerVat { get; init; }

    /// <summary>The currency of a foreign invoice; empty for the other exports.</summary>
    public string InvoiceCurrency { get; init; } = "";

    /// <summary>The total of a foreign invoice in its own currency.</summary>
    public decimal InvoiceTotal { get; init; }

    // ----- chosen by the user -----

    /// <summary>The deal type set by hand; null = by the rules.</summary>
    public int? DealTypeOverride { get; set; }

    // ----- calculated -----

    public int AutoDealType { get; internal set; }

    /// <summary>Why the rules chose <see cref="AutoDealType"/>.</summary>
    public string Why { get; internal set; } = "";

    public int DealType => DealTypeOverride ?? AutoDealType;
    public decimal Gross => Base + Vat;

    /// <summary>The account with its sub-account, as in the file: 601/5.</summary>
    public string AccountKey => AccountText.Join(Account, Analytic);

    // ----- editing -----

    private readonly Dictionary<LineField, string> _original = [];

    internal void RememberOriginal()
    {
        foreach (var field in Enum.GetValues<LineField>()) _original[field] = GetText(field);
    }

    public bool IsEdited(LineField field) => _original.TryGetValue(field, out var was) && was != GetText(field);

    public bool AnyEdited => Enum.GetValues<LineField>().Any(IsEdited);

    public void Revert()
    {
        foreach (var (field, was) in _original) TrySet(field, was, out _);
    }

    /// <summary>A part of this line, as a line of its own.</summary>
    internal LedgerLine Part(decimal @base, decimal vat, decimal rate, int deal, decimal sellerTotal) => new()
    {
        SourceRow = SourceRow,
        Bucket = Bucket,
        Account = Account,
        AccountName = AccountName,
        Analytic = Analytic,
        AnalyticName = AnalyticName,
        Article = Article,
        Base = @base,
        Vat = vat,
        RateText = RateText,
        DeclaredRate = rate,
        Treatment = Treatment,
        JournalCell = JournalCell,
        SellerTotal = sellerTotal,
        DealTypeOverride = deal,
    };

    /// <summary>The stored value, culture-independent.</summary>
    public string GetText(LineField field) => field switch
    {
        LineField.Account => Account,
        LineField.Analytic => Analytic,
        LineField.Base => Base.ToString("0.##########", CultureInfo.InvariantCulture),
        LineField.Vat => Vat.ToString("0.##########", CultureInfo.InvariantCulture),
        LineField.DealType => DealTypeOverride?.ToString(CultureInfo.InvariantCulture) ?? "",
        _ => "",
    };

    public bool TrySet(LineField field, string? text, out string error)
    {
        error = "";
        string value = CellValues.Clean(text);

        switch (field)
        {
            case LineField.Account:
                Account = value;
                return true;
            case LineField.Analytic:
                Analytic = value;
                return true;
            case LineField.Base:
            case LineField.Vat:
                decimal amount = 0m;
                if (value.Length > 0)
                {
                    if (CellValues.Number(value) is not { } n)
                    {
                        error = $"„{value}“ не е сума.";
                        return false;
                    }

                    amount = n;
                }

                if (field == LineField.Base) Base = amount;
                else Vat = amount;
                return true;
            case LineField.DealType:
                if (value.Length == 0)
                {
                    DealTypeOverride = null;
                    return true;
                }

                // „7" or „7 - Облагаеми доставки с 20% ДДС"
                string number = value.Split(' ', '-')[0];
                if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out int code) || !DealTypes.IsValid(code))
                {
                    error = $"„{value}“ не е вид сделка (1–18).";
                    return false;
                }

                DealTypeOverride = code;
                return true;
            default:
                return false;
        }
    }
}
