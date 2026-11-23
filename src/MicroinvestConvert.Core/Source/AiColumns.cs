namespace MicroinvestConvert.Core.Source;

/// <summary>
/// Columns of the AI Accounting exports, by ABSOLUTE position (0-based). The header
/// texts drift against the data in the right part of the sales export, so nothing is
/// matched by header. A..Y are the same in both exports.
/// </summary>
internal static class AiColumns
{
    public const int Index = 0;              // A   №
    public const int DocType = 1;            // B   Вид Документ
    public const int DocCode = 2;            // C   Код Документ
    public const int DocNumber = 3;          // D   Документ №
    public const int DocDate = 4;            // E   Дата на Документа
    public const int TaxPeriod = 7;          // H   Данъчен Период на Експорта
    public const int PartnerName = 8;        // I   Доставчик / Клиент
    public const int PartnerId = 9;          // J   ЕИК / ДДС Номер
    public const int Account = 10;           // K   Код на Счетоводна сметка
    public const int AccountName = 11;       // L   Име на Счетоводна сметка
    public const int Analytic = 12;          // M   Код на Крайна Аналитичност
    public const int AnalyticName = 13;      // N   Име на Крайна Аналитичност
    public const int Article = 17;           // R   Артикул
    public const int Quantity = 18;          // S   Колич.
    public const int Currency = 19;          // T   Валута
    public const int SellerBase = 21;        // V   Данъчна Основа Продавач
    public const int SellerRate = 22;        // W   % ДДС продавач
    public const int SellerVat = 23;         // X   ДДС Продавач
    public const int SellerTotal = 24;       // Y   Обща Сума Продавач
    public const int Treatment = 25;         // Z   Вид на ДДС (третиране)

    /// <summary>The purchases export („Експорт Покупки", A..AQ).</summary>
    public static class Purchase
    {
        public const int NoCredit = 27;          // AB  Без Данъчен Кредит
        public const int FullBase = 28;          // AC  Пълен Данъчен Кредит – Основа
        public const int FullVat = 29;           // AD  Пълен Данъчен Кредит – ДДС
        public const int PartialBase = 30;       // AE  Частичен Данъчен Кредит – Основа
        public const int PartialVat = 31;        // AF  Частичен Данъчен Кредит – ДДС
        public const int NoDiary = 32;           // AG  без ДДС Дневник
        public const int Payment = 33;           // AH  Начин на Плащане
        public const int CounterAccount = 34;    // AI  сметка за Контрагент
        public const int CashAccount = 35;       // AJ  сметка за плащане в брой
        public const int OperationName = 37;     // AL  Наименование на счетоводна операция
        public const int CashOperationName = 39; // AN  Наименование … за плащане в брой
        public const int Comment = 40;           // AO  Забележка/Коментар
    }

    /// <summary>
    /// The sales export („Експорт Продажби"). The platform has had two layouts: until
    /// July 2026 one „0%" column (A..AR, 44 columns), since then the 0% cases are split
    /// over four columns (A..AU, 47 columns). Everything after AG moves with it.
    /// </summary>
    public sealed class Sales
    {
        public const int JournalCell = 26;       // AA  Клетки в дневник продажби
        public const int Base20 = 27;            // AB  ДО при продажба 20%
        public const int Vat20 = 28;             // AC  ДДС при продажба 20%
        public const int Base9 = 29;             // AD  ДО при продажба 9%
        public const int Vat9 = 30;              // AE  ДДС при продажба 9%
        public const int IntraCommunity = 31;    // AF  ДО при ВОД
        public const int Export = 32;            // AG  ДО при износ - 0%

        public bool IsCurrent { get; private init; }
        public int Articles140 { get; private init; } = -1;   // AH  ДО при международен транспорт - 0%
        public int UntaxedEu { get; private init; } = -1;     // AI  ДО при необлагаеми доставки в ЕС
        public int UntaxedNonEu { get; private init; } = -1;  // AJ  ДО при необлагаеми доставки извън ЕС
        public int Exempt { get; private init; }              // AK  ДО при освободени
        public int Payment { get; private init; }             // AL  Начин на Плащане
        public int CounterAccount { get; private init; }      // AM  Дт сметка (клиент)
        public int CashAccount { get; private init; }         // AN  сметка за плащане в брой
        public int OperationName { get; private init; }       // AP  Наименование на счетоводна операция
        public int CashOperationName { get; private init; }   // AR  Наименование … за плащане в брой
        public int Comment { get; private init; }             // AS  Забележка/Коментар

        public static readonly Sales Current = new()
        {
            IsCurrent = true,
            Articles140 = 33, UntaxedEu = 34, UntaxedNonEu = 35, Exempt = 36,
            Payment = 37, CounterAccount = 38, CashAccount = 39,
            OperationName = 41, CashOperationName = 43, Comment = 44,
        };

        public static readonly Sales BeforeJuly2026 = new()
        {
            IsCurrent = false,
            Exempt = 33,
            Payment = 34, CounterAccount = 35, CashAccount = 36,
            OperationName = 38, CashOperationName = 40, Comment = 41,
        };
    }

    /// <summary>The Excel letter of a 0-based column: 0 → A, 26 → AA.</summary>
    public static string Letter(int column)
    {
        string letters = "";
        for (int n = column + 1; n > 0; n = (n - 1) / 26) letters = (char)('A' + (n - 1) % 26) + letters;
        return letters;
    }
}
