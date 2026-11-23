using System.Globalization;

namespace MicroinvestConvert.Core.Nomenclature;

/// <summary>One entry of the Делта Pro nomenclature „Тип документ" (column I).</summary>
/// <param name="Name">Full name.</param>
/// <param name="ShortName">What is written in column I.</param>
public sealed record DocumentType(string Name, string ShortName)
{
    public string Label => $"{ShortName} – {Name}";
}

/// <summary>
/// Column I. The whole nomenclature of Model.xls, in its order. The first three
/// are recognised from the file; all of them can be chosen by hand.
/// </summary>
public static class DocumentTypes
{
    public const string Invoice = "Ф-ра";
    public const string DebitNote = "ДИ";
    public const string CreditNote = "КИ";
    public const string CashReceipt = "ПКО";
    public const string CashPayment = "РКО";

    public static readonly IReadOnlyList<DocumentType> All =
    [
        new("Фактура", "Ф-ра"),
        new("Дебитно известие", "ДИ"),
        new("Кредитно известие", "КИ"),
        new("Митническа декларация", "МД"),
        new("Протокол или друг документ", "Прот"),
        new("Отчет за извършени продажби", "ОП"),
        new("Отчет за извършени продажби при специален ред на облагане", "ОПС"),
        new("Приходен касов ордер", "ПКО"),
        new("Разходен касов ордер", "РКО"),
        new("Мемориален ордер", "МО"),
        new("Банково извлечение", "БИ"),
        new("Платежно нареждане", "ПН"),
        new("Протокол по ЗДДС", "ПЗДДС"),
        new("Инвентаризационен опис", "ИнвО"),
        new("Авансов отчет", "АО"),
        new("Вносна бележка", "ВнБ"),
        new("Бюджетно платежно нареждане", "БПлН"),
        new("Бордеро за обмяна на валута", "БОВал"),
        new("Складова/Стокова разписка", "СклР"),
        new("Искане за отпускане на материали", "ИОтпМ"),
        new("Известие за лихва", "ИЛ"),
        new("Застрахователна полица", "ЗП"),
        new("Разплащателен чек", "РЧ"),
        new("Акредитив", "Акр"),
        new("Фактура - Касова отчетност", "Ф-ра(КО)"),
        new("Дебитно известие - Касова отчетност", "ДИ(КО)"),
        new("Кредитно известие - Касова отчетност", "КИ(КО)"),
        new("Протокол при плащане - Касова отчетност", "Прот(КО)"),
        new("Протокол за данъчен кредит - Касова отчетност", "Прот(КОдк)"),
        new("Протокол за изискуем данък - за нерегистрирано лице - КО", "ПротИДнрл"),
        new("Протокол за изискуем данък - за регистрирано лице - КО", "ПротИДрл"),
        new("Акцизен документ", "АкцДок"),
        new("Квитанция", "Квит"),
        new("Протокол за безвъзмездно предоставяне на хранителни стоки", "ПХрС"),
        new("Командировъчен документ", "КмдДок"),
        new("Отчет за извършените продажби на хляб", "ОИПХ"),
        new("Отчет за извършените продажби на брашно", "ОИПБ"),
        new("Кредитно известие по чл. 126б, ал. 1 от ЗДДС", "КИ126б"),
        new("Протокол по чл. 126б, ал. 2 и 7 от ЗДДС", "Прот126б"),
    ];

    private static readonly CultureInfo Bulgarian = CultureInfo.GetCultureInfo("bg-BG");

    public static bool IsValid(string? shortName) =>
        !string.IsNullOrWhiteSpace(shortName) && Find(shortName) != null;

    /// <summary>Matches a short or a full name, whatever the letter case.</summary>
    public static DocumentType? Find(string? text)
    {
        string key = Key(text);
        if (key.Length == 0) return null;

        foreach (var type in All)
            if (Key(type.ShortName) == key || Key(type.Name) == key) return type;
        return null;
    }

    /// <summary>
    /// The type of a source document: by its text („Фактура", „КИ", „ДИ"…), and when
    /// the text says nothing — by the code (1 фактура, 2 ДИ, 3 КИ). Empty when unknown.
    /// </summary>
    public static string FromSource(string? text, int? code)
    {
        var byText = Find(text);
        if (byText != null) return byText.ShortName;

        return code switch
        {
            1 => Invoice,
            2 => DebitNote,
            3 => CreditNote,
            _ => "",
        };
    }

    public static bool IsCreditNote(string? shortName) => shortName is "КИ" or "КИ(КО)" or "КИ126б";

    private static string Key(string? text) => (text ?? "").Trim().ToLower(Bulgarian);
}
