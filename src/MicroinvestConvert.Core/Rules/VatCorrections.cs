using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Nomenclature;

namespace MicroinvestConvert.Core.Rules;

/// <summary>
/// What the user can decide in „Проверка на ДДС". Each returns false when the amounts do not
/// allow it. The document has to be processed again afterwards.
/// </summary>
public static class VatCorrections
{
    /// <summary>The amounts stay; the deal type is this one.</summary>
    public static bool SetDeal(VatGroup group, int dealType)
    {
        foreach (var line in group.Lines) line.DealTypeOverride = dealType;
        return true;
    }

    /// <summary>Everything stays as the rules have it, and is no longer asked about.</summary>
    public static bool AcceptAsIs(VatGroup group)
    {
        foreach (var line in group.Lines) line.DealTypeOverride = line.AutoDealType;
        return true;
    }

    /// <summary>
    /// The VAT becomes exactly the rate and the gross amount does not change. The correction
    /// goes to the largest line.
    /// </summary>
    public static bool CorrectVat(VatGroup group)
    {
        decimal gross = group.Base + group.Vat;
        if (gross == 0m) return false;

        decimal rate = group.Rate > 0m ? group.Rate.Value : group.DeclaredRate is 9m ? 9m : 20m;
        decimal vat = Math.Round(gross * rate / (100m + rate), 2, MidpointRounding.AwayFromZero);
        decimal change = vat - group.Vat;

        var line = group.Lines.MaxBy(l => Math.Abs(l.Gross))!;
        line.Vat += change;
        line.Base -= change;
        return true;
    }

    /// <summary>
    /// A purchase that is taxed in part: the base the VAT belongs to becomes 2, the rest
    /// (without VAT) becomes 3.
    /// </summary>
    public static bool Split(VatGroup group)
    {
        decimal rate = group.DeclaredRate is 9m ? 9m : 20m;
        bool any = false;
        foreach (var line in group.Lines.ToList())
            any |= group.Document.Split(line, rate, DealTypes.PurchaseFullCredit, DealTypes.PurchaseNoCredit);
        return any;
    }
}
