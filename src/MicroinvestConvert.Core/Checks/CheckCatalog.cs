using MicroinvestConvert.Core.Model;

namespace MicroinvestConvert.Core.Checks;

/// <summary>What a check is about; the order in which the window „Проверки" lists them.</summary>
public enum CheckArea
{
    Document,
    Dates,
    Partner,
    Accounts,
    Amounts,
    Vat,
    DealType,
}

/// <summary>One check: what it is called, what it means, and what can be corrected where it finds something.</summary>
public sealed class CheckInfo
{
    public required IssueCode Code { get; init; }
    public required CheckArea Area { get; init; }
    public required IssueSeverity Severity { get; init; }
    public required string Title { get; init; }
    public required string Help { get; init; }

    /// <summary>The fields of the document that answer the question, editable in the window.</summary>
    public IReadOnlyList<DocField> Fields { get; init; } = [];

    /// <summary>The answer may be another deal type.</summary>
    public bool AsksDealType { get; init; }

    /// <summary>The answer is the account of the rows.</summary>
    public bool AsksAccount { get; init; }

    /// <summary>The modules the check belongs to.</summary>
    public IReadOnlyList<OperationKind> Kinds { get; init; } = OperationKinds.All;

    /// <summary>An error holds the document back, so it cannot be switched off.</summary>
    public bool CanBeOff => Severity != IssueSeverity.Error;
}

/// <summary>
/// The checks of the program. A new one needs a code in <see cref="IssueCode"/>, an entry here
/// and the place in the rules that raises it; the window „Проверки" lists it by itself.
/// </summary>
public static class CheckCatalog
{
    private static readonly OperationKind[] Purchases = [OperationKind.Purchase];
    private static readonly OperationKind[] Sales = [OperationKind.Sale];
    private static readonly OperationKind[] Foreign = [OperationKind.Foreign];
    private static readonly OperationKind[] WithVat = [OperationKind.Purchase, OperationKind.Sale];

    public static readonly IReadOnlyList<CheckInfo> All =
    [
        // ----- document -----
        new()
        {
            Code = IssueCode.MissingDocumentType, Area = CheckArea.Document, Severity = IssueSeverity.Error,
            Title = "Липсва вид на документа", Fields = [DocField.DocType],
            Help = "Видът в кол. B не е познат или липсва. Изберете го от списъка.",
        },
        new()
        {
            Code = IssueCode.DocumentTypeConflict, Area = CheckArea.Document, Severity = IssueSeverity.Warning,
            Title = "Видът и кодът не съвпадат", Fields = [DocField.DocType],
            Help = "Текстът в кол. B и кодът в кол. C сочат различен вид документ. Взет е текстът.",
        },
        new()
        {
            Code = IssueCode.MissingNumber, Area = CheckArea.Document, Severity = IssueSeverity.Error,
            Title = "Липсва номер на документ", Fields = [DocField.Number],
            Help = "Без номер документът не се записва.",
        },
        new()
        {
            Code = IssueCode.PaddedNumber, Area = CheckArea.Document, Severity = IssueSeverity.Info,
            Title = "Номер, допълнен до 10 знака", Fields = [DocField.Number],
            Help = "В клетката номерът е число и Excel е махнал водещите нули. Върнати са.",
        },
        new()
        {
            Code = IssueCode.Duplicate, Area = CheckArea.Document, Severity = IssueSeverity.Warning,
            Title = "Дублиран документ", Fields = [DocField.Number, DocField.Date],
            Help = "Един и същи вид, номер и контрагент се среща повече от веднъж. Махнете отметката „За запис“ на излишния.",
        },
        new()
        {
            Code = IssueCode.MissingReason, Area = CheckArea.Document, Severity = IssueSeverity.Warning,
            Title = "Липсва основание", Fields = [DocField.Reason],
            Help = "Колона K остава празна. Въведете основание.",
        },
        new()
        {
            Code = IssueCode.ReasonFromAccount, Area = CheckArea.Document, Severity = IssueSeverity.Info,
            Title = "Основание от името на сметката", Fields = [DocField.Reason],
            Help = "Във файла няма име на операция, затова основанието е името на сметката.",
        },

        // ----- dates -----
        new()
        {
            Code = IssueCode.MissingDate, Area = CheckArea.Dates, Severity = IssueSeverity.Error,
            Title = "Липсва дата на документа", Fields = [DocField.Date],
            Help = "Без дата документът не се записва.",
        },
        new()
        {
            Code = IssueCode.MissingPeriod, Area = CheckArea.Dates, Severity = IssueSeverity.Warning,
            Title = "Липсва данъчен период", Fields = [DocField.Period, DocField.AccountingDate],
            Help = "Кол. H е празна, затова счетоводната дата е датата на документа.",
        },
        new()
        {
            Code = IssueCode.PeriodBeforeDocument, Area = CheckArea.Dates, Severity = IssueSeverity.Error,
            Title = "Периодът е преди датата на документа", Fields = [DocField.Date, DocField.Period, DocField.AccountingDate],
            Help = "Счетоводната дата не може да е преди документа. Поправете датата или периода, или задайте счетоводна дата.",
        },

        // ----- partner -----
        new()
        {
            Code = IssueCode.MissingPartner, Area = CheckArea.Partner, Severity = IssueSeverity.Warning,
            Title = "Липсва име на контрагент", Fields = [DocField.PartnerName],
            Help = "Колона D остава празна.",
        },
        new()
        {
            Code = IssueCode.EikCheckDigit, Area = CheckArea.Partner, Severity = IssueSeverity.Warning,
            Title = "ЕИК с грешна контролна цифра", Fields = [DocField.PartnerName, DocField.Eik, DocField.VatNumber],
            Kinds = WithVat,
            Help = "Последната цифра на ЕИК е контролна и тук не излиза: или е сгрешена цифра, или номерът е на чужда фирма.",
        },
        new()
        {
            Code = IssueCode.EikLength, Area = CheckArea.Partner, Severity = IssueSeverity.Warning,
            Title = "Номерът не е български ЕИК", Fields = [DocField.PartnerName, DocField.Eik, DocField.VatNumber],
            Kinds = WithVat,
            Help = "ЕИК е с 9 или 13 цифри, ЕГН е с 10. Номер с друга дължина обикновено е на чужда фирма.",
        },
        new()
        {
            Code = IssueCode.ForeignSupplier, Area = CheckArea.Partner, Severity = IssueSeverity.Warning,
            Title = "Чужд доставчик в дневника", AsksDealType = true, Kinds = Purchases,
            Help = "Доставка от чужбина обикновено е с протокол: фактурата е с вид сделка 1, а ДДС се начислява с протокола.",
        },
        new()
        {
            Code = IssueCode.NotForeign, Area = CheckArea.Partner, Severity = IssueSeverity.Warning,
            Title = "Български доставчик", Fields = [DocField.Eik, DocField.VatNumber], AsksDealType = true, Kinds = Foreign,
            Help = "Документът е в експорта „Чуждестранни“, а номерът на доставчика е български.",
        },
        new()
        {
            Code = IssueCode.ViesPartner, Area = CheckArea.Partner, Severity = IssueSeverity.Warning,
            Title = "VIES без ДДС номер от ЕС", Fields = [DocField.VatNumber], AsksDealType = true, Kinds = Sales,
            Help = "Видът сделка влиза във VIES, а клиентът няма ДДС номер от друга държава членка.",
        },

        // ----- accounts -----
        new()
        {
            Code = IssueCode.MissingAccount, Area = CheckArea.Accounts, Severity = IssueSeverity.Error,
            Title = "Липсва счетоводна сметка", AsksAccount = true,
            Help = "Има ред без сметка в кол. K (при „Чуждестранни“ – L).",
        },
        new()
        {
            Code = IssueCode.MissingCounterAccount, Area = CheckArea.Accounts, Severity = IssueSeverity.Error,
            Title = "Липсва сметка на контрагента", Fields = [DocField.CounterAccount],
            Help = "Сметката на доставчика (Кт) или на клиента (Дт) е празна.",
        },
        new()
        {
            Code = IssueCode.MissingCashAccount, Area = CheckArea.Accounts, Severity = IssueSeverity.Error,
            Title = "Плащане в брой без каса", Fields = [DocField.CashAccount, DocField.Payment],
            Help = "За втория ред (РКО/ПКО) е нужна касова сметка. Може да зададете обща в „Настройки → Файл“.",
        },
        new()
        {
            Code = IssueCode.BadAccount, Area = CheckArea.Accounts, Severity = IssueSeverity.Error,
            Title = "Неправилна сметка", Fields = [DocField.CounterAccount, DocField.CashAccount], AsksAccount = true,
            Help = "Сметката има знак, който Делта Pro не приема, или може да означава две неща (411 1, 411.1).",
        },

        // ----- amounts -----
        new()
        {
            Code = IssueCode.ForeignCurrency, Area = CheckArea.Amounts, Severity = IssueSeverity.Error,
            Title = "Сумите не са във валутата на книгите", Fields = [DocField.Currency],
            Help = "Превалутирането е за следваща версия. Поправете сумите и сменете валутата.",
        },
        new()
        {
            Code = IssueCode.AmountsDoNotAdd, Area = CheckArea.Amounts, Severity = IssueSeverity.Warning,
            Title = "Сборът се различава от общата сума", Kinds = WithVat,
            Help = "Сумите по колоните за дневника не дават общата сума на документа.",
        },
        new()
        {
            Code = IssueCode.ZeroDocument, Area = CheckArea.Amounts, Severity = IssueSeverity.Warning,
            Title = "Нулев документ",
            Help = "Всички суми са нула. Проверете дали Делта Pro го приема, или махнете отметката „За запис“.",
        },
        new()
        {
            Code = IssueCode.NegativeDocument, Area = CheckArea.Amounts, Severity = IssueSeverity.Warning,
            Title = "Отрицателна сума без кредитно известие", Fields = [DocField.DocType],
            Help = "Общата сума е с минус, а документът не е кредитно известие.",
        },
        new()
        {
            Code = IssueCode.SignFlipped, Area = CheckArea.Amounts, Severity = IssueSeverity.Info,
            Title = "Кредитно известие с положителни суми", Fields = [DocField.DocType],
            Help = "Сумите се записват с минус.",
        },
        new()
        {
            Code = IssueCode.EuroTotalDiffers, Area = CheckArea.Amounts, Severity = IssueSeverity.Warning,
            Title = "Сумата в евро се различава от фактурата", Kinds = Foreign,
            Help = "Фактурата е в евро, но сумата в кол. AI не е тази в кол. Z.",
        },
        new()
        {
            Code = IssueCode.EuroRate, Area = CheckArea.Amounts, Severity = IssueSeverity.Warning,
            Title = "Необичаен курс към еврото", Kinds = Foreign,
            Help = "Курсът, който излиза от кол. Z и AI, е различен от този на другите редове или документи в същата валута.",
        },
        new()
        {
            Code = IssueCode.ConvertedToEuro, Area = CheckArea.Amounts, Severity = IssueSeverity.Info,
            Title = "Преизчислено в евро от AI Accounting", Kinds = Foreign,
            Help = "Фактурата е във валута; записва се сумата в евро от кол. AI.",
        },
        new()
        {
            Code = IssueCode.SellerVatIncluded, Area = CheckArea.Amounts, Severity = IssueSeverity.Info,
            Title = "ДДС на чуждия доставчик е в стойността", Kinds = Foreign,
            Help = "Във фактурата има ДДС на доставчика. Той не е български и влиза в разхода.",
        },

        // ----- VAT -----
        new()
        {
            Code = IssueCode.VatUnresolved, Area = CheckArea.Vat, Severity = IssueSeverity.Warning,
            Title = "ДДС не отговаря на ставка", AsksDealType = true, Kinds = Purchases,
            Help = "ДДС не е 20% или 9% от основата, затова видът сделка е 4. На страница „ДДС“ може да се приеме, коригира или раздели.",
        },
        new()
        {
            Code = IssueCode.VatZeroButDeclared, Area = CheckArea.Vat, Severity = IssueSeverity.Warning,
            Title = "Обявена ставка, но без ДДС", AsksDealType = true, Kinds = WithVat,
            Help = "Ставката е 20% или 9%, а ДДС е нула.",
        },
        new()
        {
            Code = IssueCode.VatDifferentRate, Area = CheckArea.Vat, Severity = IssueSeverity.Warning,
            Title = "Сумите отговарят на друга ставка", AsksDealType = true, Kinds = Purchases,
            Help = "Обявената ставка не е тази, която излиза от сумите.",
        },
        new()
        {
            Code = IssueCode.VatMismatch, Area = CheckArea.Vat, Severity = IssueSeverity.Warning,
            Title = "ДДС не отговаря на ставката", Kinds = Sales,
            Help = "ДДС се различава от 20% или 9% от основата повече от допустимото.",
        },
        new()
        {
            Code = IssueCode.VatAtZeroDeal, Area = CheckArea.Vat, Severity = IssueSeverity.Warning,
            Title = "ДДС при вид сделка без ДДС", AsksDealType = true, Kinds = Sales,
            Help = "Видът сделка е без ДДС, а документът има ДДС.",
        },
        new()
        {
            Code = IssueCode.VatWithinTolerance, Area = CheckArea.Vat, Severity = IssueSeverity.Info,
            Title = "ДДС с разлика от центове", Kinds = WithVat,
            Help = "Разликата е в допустимата граница – сменя се на страница „ДДС“.",
        },

        // ----- deal type and VIES -----
        new()
        {
            Code = IssueCode.NoDiary, Area = CheckArea.DealType, Severity = IssueSeverity.Info,
            Title = "„Без ДДС дневник“", AsksDealType = true, Kinds = Purchases,
            Help = "Видът сделка на тези документи се избира в „Настройки → Файл“; тук може да се смени за отделен документ.",
        },
        new()
        {
            Code = IssueCode.NoDiaryWithAmounts, Area = CheckArea.DealType, Severity = IssueSeverity.Warning,
            Title = "„Без дневник“, а има суми за дневника", AsksDealType = true, Kinds = Purchases,
            Help = "Отбелязано е „без ДДС дневник“, но има суми в AB–AF. Взето е „без дневник“.",
        },
        new()
        {
            Code = IssueCode.ForeignNoDiary, Area = CheckArea.DealType, Severity = IssueSeverity.Warning,
            Title = "Чужд доставчик „без дневник“ с вид 3", AsksDealType = true, Kinds = Purchases,
            Help = "С вид 3 документът влиза в дневника. Ако за него има протокол, изберете вид 1 – иначе ще е в дневника два пъти.",
        },
        new()
        {
            Code = IssueCode.NotMarkedNoDiary, Area = CheckArea.DealType, Severity = IssueSeverity.Warning,
            Title = "Не е отбелязан „без ДДС дневник“", AsksDealType = true, Kinds = Foreign,
            Help = "Чуждестранните фактури са „без ДДС дневник“ (кол. AH = ДА). Тук не е така – записва се само фактурата, с вид сделка 1.",
        },
        new()
        {
            Code = IssueCode.OutsideLedger, Area = CheckArea.DealType, Severity = IssueSeverity.Warning,
            Title = "Продажба извън дневника", AsksDealType = true, Kinds = Sales,
            Help = "Няма сума в колоните за дневника, затова видът сделка е 1.",
        },
        new()
        {
            Code = IssueCode.Unspecified, Area = CheckArea.DealType, Severity = IssueSeverity.Warning,
            Title = "Няма избрано третиране", AsksDealType = true, Kinds = Purchases,
            Help = "Колоните AB–AG са празни, затова видът сделка е определен само по сумите.",
        },
        new()
        {
            Code = IssueCode.ReverseCharge, Area = CheckArea.DealType, Severity = IssueSeverity.Error,
            Title = "Обратно начисляване по чл. 163а", AsksDealType = true, Kinds = Purchases,
            Help = "Изисква протокол (колони Q–AA), който е за следваща версия. Документът се записва, ако зададете вид сделка.",
        },
        new()
        {
            Code = IssueCode.MixedRow, Area = CheckArea.DealType, Severity = IssueSeverity.Info,
            Title = "Ред със суми в няколко колони", AsksDealType = true, Kinds = WithVat,
            Help = "Редът е разделен и всяка част има своя вид сделка.",
        },
        new()
        {
            Code = IssueCode.JournalCellMismatch, Area = CheckArea.DealType, Severity = IssueSeverity.Warning,
            Title = "Друга клетка в дневника", AsksDealType = true, Kinds = Sales,
            Help = "Кол. AA сочи друга клетка от колоната, в която е сумата. Важи колоната със сумата.",
        },
        new()
        {
            Code = IssueCode.ViesMixed, Area = CheckArea.DealType, Severity = IssueSeverity.Warning,
            Title = "Различен VIES в един документ", Fields = [DocField.Vies], Kinds = Sales,
            Help = "Записва се по една стойност за всяка сума. Проверете как го приема Делта Pro.",
        },
        new()
        {
            Code = IssueCode.Overridden, Area = CheckArea.DealType, Severity = IssueSeverity.Info,
            Title = "Вид сделка, зададен ръчно", AsksDealType = true,
            Help = "Видът сделка не е този от правилата.",
        },
    ];

    private static readonly Dictionary<IssueCode, CheckInfo> ByCode = All.ToDictionary(c => c.Code);

    public static CheckInfo Get(IssueCode code) => ByCode[code];

    /// <summary>The checks of a module, in the order they are listed.</summary>
    public static IEnumerable<CheckInfo> For(OperationKind kind) => All.Where(c => c.Kinds.Contains(kind));

    public static string Title(CheckArea area) => area switch
    {
        CheckArea.Document => "Документ",
        CheckArea.Dates => "Дати",
        CheckArea.Partner => "Контрагент",
        CheckArea.Accounts => "Сметки",
        CheckArea.Amounts => "Суми",
        CheckArea.Vat => "ДДС",
        _ => "Вид сделка",
    };
}
