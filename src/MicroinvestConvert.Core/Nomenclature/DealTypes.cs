namespace MicroinvestConvert.Core.Nomenclature;

/// <summary>Which ledger a deal type belongs to.</summary>
public enum DealSide
{
    /// <summary>Not in any VAT ledger.</summary>
    None,
    Purchase,
    Sale,
}

/// <summary>
/// One entry of the Делта Pro nomenclature „Видове сделки" (column N).
/// </summary>
/// <param name="Code">The number written in column N.</param>
/// <param name="Name">The name exactly as in Model.xls.</param>
/// <param name="Side">Purchases ledger, sales ledger or none.</param>
/// <param name="LedgerColumns">The ledger columns the amounts land in, e.g. „11; 12".</param>
/// <param name="Vies">The VIES value (column O) that goes with it. Sales only.</param>
public sealed record DealType(int Code, string Name, DealSide Side, string LedgerColumns, int Vies)
{
    public string Label => $"{Code} - {Name}";

    public string LedgerText => Side switch
    {
        DealSide.Purchase => "Дневник покупки, к. " + LedgerColumns,
        DealSide.Sale => "Дневник продажби, к. " + LedgerColumns,
        _ => "не влиза в дневниците",
    };
}

/// <summary>
/// Column N. The mapping an accountant confirms is documented in Deal_Type.md;
/// a unit test keeps the two in step.
/// </summary>
public static class DealTypes
{
    public const int Internal = 1;
    public const int PurchaseFullCredit = 2;
    public const int PurchaseNoCredit = 3;
    public const int PurchasePartialCredit = 4;
    public const int PurchaseAnnualCorrection = 5;
    public const int PurchaseTriangular = 6;
    public const int Sale20 = 7;
    public const int SaleIntraCommunityAcquisition = 8;
    public const int SaleArticle82 = 9;
    public const int SaleOtherTax = 10;
    public const int Sale9 = 11;
    public const int Sale0 = 12;
    public const int SaleIntraCommunitySupply = 13;
    public const int SaleArticles140 = 14;
    public const int SaleServicesArticle21 = 15;
    public const int SaleDistance = 16;
    public const int SaleExempt = 17;
    public const int SaleTriangular = 18;

    public static readonly IReadOnlyList<DealType> All =
    [
        new(Internal, "Вътрешен оборот", DealSide.None, "", ViesTypes.NotDeclared),
        new(PurchaseFullCredit, "Получени доставки, ВОП с право на ПДК", DealSide.Purchase, "10; 11", ViesTypes.NotDeclared),
        new(PurchaseNoCredit, "Получени доставки по чл.82 и без право на ДК", DealSide.Purchase, "9", ViesTypes.NotDeclared),
        new(PurchasePartialCredit, "Получени доставки, ВОП с право на ЧДК", DealSide.Purchase, "12; 13", ViesTypes.NotDeclared),
        new(PurchaseAnnualCorrection, "Годишна корекция, чл. 73, ал. 8 и чл. 147, ал. 3", DealSide.Purchase, "14", ViesTypes.NotDeclared),
        new(PurchaseTriangular, "Придобиване при тристранни операции", DealSide.Purchase, "15", ViesTypes.NotDeclared),
        new(Sale20, "Облагаеми доставки с 20% ДДС", DealSide.Sale, "11; 12", ViesTypes.NotDeclared),
        new(SaleIntraCommunityAcquisition, "Вътреобщностни придобивания", DealSide.Sale, "13; 15", ViesTypes.NotDeclared),
        new(SaleArticle82, "Доставки по чл. 82, ал. 2-5", DealSide.Sale, "14; 15", ViesTypes.NotDeclared),
        new(SaleOtherTax, "Начислен данък, предвиден в други случаи", DealSide.Sale, "16", ViesTypes.NotDeclared),
        new(Sale9, "Облагаеми доставки с 9% ДДС", DealSide.Sale, "17; 18", ViesTypes.NotDeclared),
        new(Sale0, "Облагаеми доставки с 0% ДДС", DealSide.Sale, "19", ViesTypes.NotDeclared),
        new(SaleIntraCommunitySupply, "Вътреобщностни доставки", DealSide.Sale, "20", ViesTypes.IntraCommunitySupply),
        new(SaleArticles140, "Доставки по чл. 140, 146 и 173", DealSide.Sale, "21", ViesTypes.NotDeclared),
        new(SaleServicesArticle21, "Доставки на услуги, чл. 21, ал.2 и чл. 22-24", DealSide.Sale, "22", ViesTypes.ServicesArticle21),
        new(SaleDistance, "Дистанционни продажби по чл. 69, ал. 2", DealSide.Sale, "23", ViesTypes.NotDeclared),
        new(SaleExempt, "Освободени доставки и освободени ВОП", DealSide.Sale, "24", ViesTypes.NotDeclared),
        new(SaleTriangular, "Доставки в тристранни операции", DealSide.Sale, "25", ViesTypes.TriangularIntermediary),
    ];

    private static readonly Dictionary<int, DealType> ByCode = All.ToDictionary(d => d.Code);

    public static bool IsValid(int code) => ByCode.ContainsKey(code);

    public static DealType Get(int code) =>
        ByCode.TryGetValue(code, out var deal)
            ? deal
            : throw new ArgumentOutOfRangeException(nameof(code), code, "Непознат вид сделка.");

    /// <summary>The deal types a user may choose for a document of the given side.</summary>
    public static IEnumerable<DealType> ChoicesFor(DealSide side) =>
        All.Where(d => d.Side == side || d.Side == DealSide.None);
}

/// <summary>Column O. Filled for sales only.</summary>
public static class ViesTypes
{
    public const int NotDeclared = 1;
    public const int IntraCommunitySupply = 2;
    public const int TriangularIntermediary = 3;
    public const int ServicesArticle21 = 4;

    public static readonly IReadOnlyList<(int Code, string Name)> All =
    [
        (NotDeclared, "Не участва в декларацията"),
        (IntraCommunitySupply, "Вътреобщностни доставки"),
        (TriangularIntermediary, "Посредник в тристранна операция"),
        (ServicesArticle21, "Доставки по чл. 21, ал. 3 и чл. 22-24"),
    ];

    public static bool IsValid(int code) => code is >= NotDeclared and <= ServicesArticle21;

    public static string Label(int code)
    {
        foreach (var (c, name) in All)
            if (c == code) return $"{c} - {name}";
        return code.ToString();
    }
}
