namespace MicroinvestConvert.Core.Output;

/// <summary>One column of the „Операции" sheet.</summary>
/// <param name="Letter">Excel letter.</param>
/// <param name="Header">Title on the first row of „Операции".</param>
/// <param name="Setting">Name of the column in „Настройка Операции".</param>
public sealed record OperationsColumn(string Letter, string Header, string Setting);

/// <summary>
/// The import layout of Делта Pro, as in the vendor's Model.xls: the data sheet and the
/// sheet that tells the program which column is what.
/// </summary>
public static class OperationsLayout
{
    public const string DataSheet = "Операции";
    public const string SettingsSheet = "Настройка Операции";

    /// <summary>1-based row of the first operation.</summary>
    public const int FirstRow = 2;

    public static readonly IReadOnlyList<OperationsColumn> Columns =
    [
        new("A", "Сума*", "Колона за суми"),
        new("B", "Сметки", "Колона за сметки"),
        new("C", "Дата", "Колона за дата"),
        new("D", "Партньор", "Колона на партньор"),
        new("E", "Булстат/ЕИК", "Колона за Булстат\\ЕИК"),
        new("F", "ДДС Номер", "Колона за ДДС номер"),
        new("G", "IBAN", "IBAN"),
        new("H", "Документ", "Колона за номер на док."),
        new("I", "Тип", "Колона за вид док."),
        new("J", "Дата на документа", "Колона за дата на док."),
        new("K", "Основание", "Колона за основание"),
        new("L", "Забележка", "Колона за забележка"),
        new("M", "Втора забележка", "Колона за втора забележка"),
        new("N", "Вид сделка", "Колона за вид сделка"),
        new("O", "VIES", "Колона за VIES"),
        new("P", "Месец за експорт", "Колона за месец за експорт"),
        new("Q", "Тип операция", "Тип операция"),
        new("R", "Вид на стоката/операцията", "Вид на стоката/операцията"),
        new("S", "Документ №", "Документ №"),
        new("T", "Дата", "Дата"),
        new("U", "Стойност на ДДС", "Стойност на ДДС"),
        new("V", "Описание на доставката", "Описание на доставката"),
        new("W", "Основание", "Основание"),
        new("X", "Забележка", "Забележка"),
        new("Y", "Операция по ДДС", "Операция по ДДС"),
        new("Z", "Месец за експорт", "Месец за експорт"),
        new("AA", "Сметка за разход", "Сметка за разход"),
    ];

    // Positions in OperationRow.Cells.
    public const int Amounts = 0;
    public const int Accounts = 1;
    public const int Date = 2;
    public const int Partner = 3;
    public const int Eik = 4;
    public const int VatNumber = 5;
    public const int Number = 7;
    public const int DocType = 8;
    public const int DocDate = 9;
    public const int Reason = 10;
    public const int Note = 11;
    public const int Note2 = 12;
    public const int DealType = 13;
    public const int Vies = 14;
    public const int ExportMonth = 15;

    /// <summary>Columns Q..AA belong to protocols and stay empty in this version.</summary>
    public const int FirstProtocolColumn = 16;
}
