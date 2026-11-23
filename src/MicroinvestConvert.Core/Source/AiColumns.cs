namespace MicroinvestConvert.Core.Source;

/// <summary>
/// Columns of the AI Accounting exports, by ABSOLUTE position (0-based). The header
/// texts drift against the data in the right part of the sales export, so nothing is
/// matched by header. A..J are the same in every export; the foreign one inserts K and AI.
/// </summary>
internal sealed class AiColumns
{
    public const int Index = 0;              // A   №
    public const int DocType = 1;            // B   Вид Документ
    public const int DocCode = 2;            // C   Код Документ
    public const int DocNumber = 3;          // D   Документ №
    public const int DocDate = 4;            // E   Дата на Документа
    public const int TaxPeriod = 7;          // H   Данъчен Период на Експорта
    public const int PartnerName = 8;        // I   Доставчик / Клиент
    public const int PartnerId = 9;          // J   ЕИК / ДДС Номер (foreign: ЕИК)

    private const int None = -1;

    public string Name { get; private init; } = "";

    // The letters below are of the purchases and sales exports; foreign is one to the right.
    public int VatId { get; private init; } = None;        // K of the foreign export: VAT номер
    public int Account { get; }              // K   Код на Счетоводна сметка
    public int AccountName { get; }          // L   Име на Счетоводна сметка
    public int Analytic { get; }             // M   Код на Крайна Аналитичност
    public int AnalyticName { get; }         // N   Име на Крайна Аналитичност
    public int Article { get; }              // R   Артикул
    public int Currency { get; }             // T   Валута
    public int SellerBase { get; }           // V   Данъчна Основа Продавач
    public int SellerRate { get; }           // W   % ДДС продавач
    public int SellerVat { get; }            // X   ДДС Продавач
    public int SellerTotal { get; }          // Y   Обща Сума Продавач
    public int Treatment { get; }            // Z   Вид на ДДС (третиране)

    // ----- purchases („Експорт Покупки", A..AQ; foreign A..AS) -----
    public int NoCredit { get; private init; } = None;     // AB  Без Данъчен Кредит
    public int FullBase { get; private init; } = None;     // AC  Пълен Данъчен Кредит – Основа
    public int FullVat { get; private init; } = None;      // AD  Пълен Данъчен Кредит – ДДС
    public int PartialBase { get; private init; } = None;  // AE  Частичен Данъчен Кредит – Основа
    public int PartialVat { get; private init; } = None;   // AF  Частичен Данъчен Кредит – ДДС
    public int NoDiary { get; private init; } = None;      // AG  без ДДС Дневник
    public int EuroTotal { get; private init; } = None;    // AI of the foreign export: Обща сума евро

    // ----- sales („Експорт Продажби"). The platform has had two layouts: until July 2026 one
    // „0%" column (A..AR, 44 columns), since then the 0% cases are split over four columns
    // (A..AU, 47 columns). Everything after AG moves with it. -----
    public const int JournalCell = 26;       // AA  Клетки в дневник продажби
    public const int Base20 = 27;            // AB  ДО при продажба 20%
    public const int Vat20 = 28;             // AC  ДДС при продажба 20%
    public const int Base9 = 29;             // AD  ДО при продажба 9%
    public const int Vat9 = 30;              // AE  ДДС при продажба 9%
    public const int IntraCommunity = 31;    // AF  ДО при ВОД
    public const int Export = 32;            // AG  ДО при износ - 0%
    public int Articles140 { get; private init; } = None;  // AH  ДО при международен транспорт - 0%
    public int UntaxedEu { get; private init; } = None;    // AI  ДО при необлагаеми доставки в ЕС
    public int UntaxedNonEu { get; private init; } = None; // AJ  ДО при необлагаеми доставки извън ЕС
    public int Exempt { get; private init; } = None;       // AK  ДО при освободени

    // ----- the end of every export -----
    public int Payment { get; private init; }              // Начин на Плащане
    public int CounterAccount { get; private init; }       // сметка за Контрагент
    public int CashAccount { get; private init; }          // сметка за плащане в брой
    public int OperationName { get; private init; }        // Наименование на счетоводна операция
    public int CashOperationName { get; private init; }    // Наименование … за плащане в брой
    public int Comment { get; private init; }              // Забележка/Коментар към документа

    private AiColumns(int shift)
    {
        Account = 10 + shift;
        AccountName = 11 + shift;
        Analytic = 12 + shift;
        AnalyticName = 13 + shift;
        Article = 17 + shift;
        Currency = 19 + shift;
        SellerBase = 21 + shift;
        SellerRate = 22 + shift;
        SellerVat = 23 + shift;
        SellerTotal = 24 + shift;
        Treatment = 25 + shift;
    }

    public static readonly AiColumns Purchases = new(0)
    {
        Name = "Експорт Покупки",
        NoCredit = 27, FullBase = 28, FullVat = 29, PartialBase = 30, PartialVat = 31, NoDiary = 32,
        Payment = 33, CounterAccount = 34, CashAccount = 35, OperationName = 37, CashOperationName = 39, Comment = 40,
    };

    public static readonly AiColumns Foreign = new(1)
    {
        Name = "Експорт Чуждестранни",
        VatId = 10,
        NoCredit = 28, FullBase = 29, FullVat = 30, PartialBase = 31, PartialVat = 32, NoDiary = 33, EuroTotal = 34,
        Payment = 35, CounterAccount = 36, CashAccount = 37, OperationName = 39, CashOperationName = 41, Comment = 42,
    };

    public static readonly AiColumns Sales = new(0)
    {
        Name = "Експорт Продажби",
        Articles140 = 33, UntaxedEu = 34, UntaxedNonEu = 35, Exempt = 36,
        Payment = 37, CounterAccount = 38, CashAccount = 39, OperationName = 41, CashOperationName = 43, Comment = 44,
    };

    public static readonly AiColumns SalesBeforeJuly2026 = new(0)
    {
        Name = "Експорт Продажби (колони до юли 2026)",
        Exempt = 33,
        Payment = 34, CounterAccount = 35, CashAccount = 36, OperationName = 38, CashOperationName = 40, Comment = 41,
    };
}
