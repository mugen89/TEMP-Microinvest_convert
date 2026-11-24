using System.Globalization;
using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Nomenclature;
using MicroinvestConvert.Core.Settings;
using MicroinvestConvert.Core.Values;
using L = MicroinvestConvert.Core.Output.OperationsLayout;

namespace MicroinvestConvert.Core.Output;

/// <summary>One row of „Операции" — exactly the texts that are written.</summary>
public sealed class OperationRow
{
    public required OperationDocument Document { get; init; }

    /// <summary>The second row of a document paid in cash.</summary>
    public required bool IsCashRow { get; init; }

    public string[] Cells { get; } = new string[L.Columns.Count];

    public OperationRow() => Array.Fill(Cells, "");
}

public static class OperationRows
{
    /// <summary>The rows of every document that goes into the file, in the order of the source.</summary>
    public static List<OperationRow> Build(OperationBatch batch, ConverterOptions options)
    {
        var rows = new List<OperationRow>();
        foreach (var doc in batch.Documents)
            if (doc.WillExport) rows.AddRange(Build(doc, options));
        return rows;
    }

    /// <summary>The one or two rows of a document.</summary>
    public static IEnumerable<OperationRow> Build(OperationDocument doc, ConverterOptions options)
    {
        yield return MainRow(doc, options);
        if (doc.IsCash && options.General.CashRows) yield return CashRow(doc);
    }

    private static OperationRow MainRow(OperationDocument doc, ConverterOptions options)
    {
        var row = NewRow(doc, isCashRow: false);
        string accounts = string.Join(';', doc.Segments.Select(s => s.Account));

        row.Cells[L.Amounts] = string.Join(';', doc.Segments.Select(s => CurrencyNotation.Text(doc, s.Amount, s.InCurrency, s.Currency, s.Lines)));
        row.Cells[L.Accounts] = doc.Kind.IsPurchaseSide()
            ? accounts + "-" + doc.CounterAccountOut     // many debits – one credit
            : doc.CounterAccountOut + "-" + accounts;    // one debit – many credits
        row.Cells[L.Number] = doc.Number;
        row.Cells[L.DocType] = doc.DocType;
        row.Cells[L.Reason] = doc.Reason;
        row.Cells[L.DealType] = DealTypeCell(doc, options);
        row.Cells[L.Vies] = ViesCell(doc);
        return row;
    }

    // РКО / ПКО is a label of the nomenclature — the sign of the amount carries the direction,
    // so the payment of a credit note is the same label with a minus.
    private static OperationRow CashRow(OperationDocument doc)
    {
        bool purchase = doc.Kind.IsPurchaseSide();
        string type = purchase ? DocumentTypes.CashPayment : DocumentTypes.CashReceipt;

        var row = NewRow(doc, isCashRow: true);
        row.Cells[L.Amounts] = CurrencyNotation.Text(doc, doc.TotalGross, doc.TotalInCurrency,
            doc.CounterCurrency.Length > 0 ? doc.CounterCurrency : doc.CashCurrency, doc.Lines);
        row.Cells[L.Accounts] = purchase
            ? doc.CounterAccountOut + "-" + doc.CashAccountOut
            : doc.CashAccountOut + "-" + doc.CounterAccountOut;
        row.Cells[L.Number] = type + "-" + doc.Number;
        row.Cells[L.DocType] = type;
        row.Cells[L.Reason] = doc.CashReason.Length > 0
            ? doc.CashReason
            : $"Плащане по {(doc.DocType == DocumentTypes.Invoice ? "ф-ра" : doc.DocType)} {doc.Number}";
        row.Cells[L.DealType] = Number(DealTypes.Internal);
        row.Cells[L.Vies] = purchase ? "" : Number(ViesTypes.NotDeclared);
        return row;
    }

    private static OperationRow NewRow(OperationDocument doc, bool isCashRow)
    {
        var row = new OperationRow { Document = doc, IsCashRow = isCashRow };
        row.Cells[L.Date] = Date(doc.AccountingDate);
        row.Cells[L.Partner] = doc.PartnerName;
        row.Cells[L.Eik] = doc.Eik;
        row.Cells[L.VatNumber] = doc.VatNumber;
        row.Cells[L.DocDate] = Date(doc.Date);
        row.Cells[L.Note] = doc.Note;
        row.Cells[L.Note2] = doc.Note2;
        row.Cells[L.ExportMonth] = Date(doc.AccountingDate);   // P = C
        return row;
    }

    private static string DealTypeCell(OperationDocument doc, ConverterOptions options) =>
        Sequence(doc.Segments.Select(s => s.DealType), options.General.DealTypeFormat == DealTypeFormat.SingleWhenEqual);

    private static string ViesCell(OperationDocument doc)
    {
        if (doc.Kind.IsPurchaseSide()) return "";
        if (doc.ViesOverride is { } chosen) return Number(chosen);
        return Sequence(doc.Segments.Select(s => s.Vies), singleWhenEqual: true);
    }

    private static string Sequence(IEnumerable<int> values, bool singleWhenEqual)
    {
        var list = values.ToList();
        return singleWhenEqual && list.Distinct().Count() == 1
            ? Number(list[0])
            : string.Join(';', list.Select(Number));
    }

    /// <summary>Always with a decimal dot and without grouping, whatever the machine.</summary>
    public static string Amount(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Date(DateTime? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
}
