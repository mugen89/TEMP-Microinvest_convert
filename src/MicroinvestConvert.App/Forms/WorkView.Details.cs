using System.Globalization;
using MicroinvestConvert.App.Ui;
using MicroinvestConvert.Core.Exchange;
using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Nomenclature;
using MicroinvestConvert.Core.Output;
using MicroinvestConvert.Core.Rules;

namespace MicroinvestConvert.App.Forms;

// What is inside the chosen document, the preview of the file, and the reading log.
internal sealed partial class WorkView
{
    /// <summary>One row of „Как ще се осчетоводи".</summary>
    private sealed record Posting(string Side, string Account, string Name, decimal Amount, string InCurrency, string Deal, bool IsTotal);

    private const string AutoDeal = "авто";

    private readonly Grid<LedgerLine> _lines = new();
    private readonly Grid<Posting> _postings = new();
    private readonly Grid<Issue> _issues = new();
    private readonly Grid<OperationRow> _preview = new();
    private readonly Label _detailsTitle = Make.Label("", font: Theme.Bold);
    private readonly Label _previewTitle = Make.Label("", Theme.Muted);
    private readonly Dictionary<int, LineField> _fieldOfLineColumn = [];
    private OperationDocument? _shown;
    private int _euroColumn, _inCurrencyColumn;

    private void BuildDetails()
    {
        // ----- the rows of the file -----
        _lines.Use(_palette);
        _lines.SetHeights(25, 28);

        _lines.AddColumn(new() { Header = "Ред", Width = 48, Right = true, Get = l => l.SourceRow.ToString(CultureInfo.InvariantCulture), Tip = "Ред в Excel файла" });
        LineColumn(LineField.Account, "Сметка", 72);
        LineColumn(LineField.Analytic, "Аналит.", 76);
        LineColumn(LineField.Base, HasVat ? "Основа" : "По фактура", HasVat ? 82 : 96, right: true, get: l => Fmt.Money(l.Base));
        LineColumn(LineField.Vat, "ДДС", 72, right: true, get: l => Fmt.Money(l.Vat));
        AddEuroColumn();
        _lines.AddColumn(new() { Header = "Ставка", Width = 76, Get = l => l.RateText, Tip = "% ДДС от файла" });

        var choices = DealTypes.ChoicesFor(IsPurchase ? DealSide.Purchase : DealSide.Sale)
            .Select(d => d.Code.ToString(CultureInfo.InvariantCulture)).ToList();
        _fieldOfLineColumn[_lines.AddColumn(new()
        {
            Header = "Вид", Width = 58, Center = true, Tip = "N · Вид сделка. Изберете друг, за да го зададете ръчно.",
            Items = [AutoDeal, .. choices],
            ItemLabel = v => int.TryParse(v, out int code) ? DealTypes.Get(code).Label : "автоматично, по правилата",
            Get = l => l.DealType.ToString(CultureInfo.InvariantCulture),
            Set = SetDeal,
        })] = LineField.DealType;
        _lines.AddColumn(new() { Header = "Защо", Width = 260, Get = l => l.DealTypeOverride != null ? "зададен ръчно" : l.Why });
        _lines.AddColumn(new() { Header = "Име на сметката", Width = 160, Get = l => l.AnalyticName.Length > 0 ? l.AnalyticName : l.AccountName });
        _lines.AddColumn(new() { Header = "Артикул", Width = 200, Get = l => l.Article });
        _lines.AddColumn(new() { Header = "Колона във файла", Width = 170, Get = l => LedgerBuckets.Title(l.Bucket) });

        _lines.StyleCell = (line, column, style) =>
        {
            if (!_fieldOfLineColumn.TryGetValue(column, out var field)) return;
            if (field == LineField.DealType) style.Font = Theme.Bold;
            if (!line.IsEdited(field) && !(field == LineField.DealType && line.DealTypeOverride != null)) return;
            style.ForeColor = Theme.EditedInk;
            style.Font = Theme.Bold;
        };
        _lines.Edited += _ => { if (_shown != null) Edited([_shown]); };

        // ----- how it will be posted -----
        _postings.Use(_palette);
        _postings.ReadOnly = true;
        _postings.SetHeights(24, 28);
        _postings.AddColumn(new() { Header = "", Width = 34, Center = true, Get = p => p.Side });
        _postings.AddColumn(new() { Header = "Сметка", Width = 92, Get = p => p.Account });
        _postings.AddColumn(new() { Header = "Сума", Width = 88, Right = true, Get = p => Fmt.Money(p.Amount) });
        _inCurrencyColumn = _postings.AddColumn(new()
        {
            Header = "Във валута", Width = 118, Right = true, Get = p => p.InCurrency,
            Tip = "Записва се в кол. A след сумата в евро: евро (валута|курс|фиксинг)",
        });
        _postings.AddColumn(new() { Header = "Вид сделка", Width = 180, Fill = true, Get = p => p.Deal });
        _postings.StyleCell = (p, _, style) =>
        {
            if (p.IsTotal) style.Font = Theme.Bold;
        };

        // ----- problems -----
        _issues.Use(_palette);
        _issues.ReadOnly = true;
        _issues.SetHeights(24, 28);
        _issues.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells;
        _issues.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _issues.AddColumn(new()
        {
            Header = "", Width = 28, Center = true,
            Get = i => i.Reviewed ? "✓" : i.Severity switch { IssueSeverity.Error => "✖", IssueSeverity.Warning => "⚠", _ => "i" },
        });
        _issues.AddColumn(new() { Header = "Забележки по документа", Width = 200, Fill = true, Get = i => i.Message });
        _issues.StyleCell = (issue, _, style) => style.ForeColor = issue.Severity switch
        {
            IssueSeverity.Error => Theme.ErrorInk,
            IssueSeverity.Warning when !issue.Reviewed => Theme.WarningInk,
            _ => Theme.Muted,
        };
    }

    private void LineColumn(LineField field, string header, int width, bool right = false, Func<LedgerLine, object?>? get = null) =>
        _fieldOfLineColumn[_lines.AddColumn(new()
        {
            Header = header, Width = width, Right = right,
            Get = get ?? (l => l.GetText(field)),
            Set = (l, v) => l.TrySet(field, v, out string error) ? null : error,
        })] = field;

    // The amount of a row in another currency. In the foreign export it is in the file, and can be corrected.
    private void AddEuroColumn()
    {
        _euroColumn = _lines.AddColumn(new()
        {
            Header = "В евро", Width = 84, Right = true,
            Tip = HasVat ? "Цялата сума на реда в евро, по курса на документа"
                : "Сумата на реда в евро – от файла (кол. AI) или по курса. Въведете друга, за да важи тя; изтрийте я, за да се превалутира по курса.",
            Get = l => _shown != null && DocumentProcessor.EuroOf(_shown, l) is { } euro ? Fmt.Money(euro) : "",
            Set = HasVat ? null : SetEuro,
        });
        if (!HasVat) _fieldOfLineColumn[_euroColumn] = LineField.Euro;
    }

    private string? SetEuro(LedgerLine line, string value)
    {
        if (_shown == null || _shown.CurrencyOf(line) == Currencies.Euro) return "Редът е в евро – поправете сумата „По фактура“.";
        return line.TrySet(LineField.Euro, value, out string error) ? null : error;
    }

    private static string? SetDeal(LedgerLine line, string value)
    {
        // Choosing what the rules chose is the same as „автоматично".
        bool auto = value == AutoDeal || value == line.AutoDealType.ToString(CultureInfo.InvariantCulture);
        return line.TrySet(LineField.DealType, auto ? "" : value, out string error) ? null : error;
    }

    private Control BuildDetailsPage()
    {
        var right = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 5 };
        right.Panel1.Controls.Add(_postings);
        right.Panel2.Controls.Add(_issues);
        right.HandleCreated += (_, _) => BeginInvoke(() => { if (right.Height > 120) right.SplitterDistance = (int)(right.Height * 0.62); });

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 6 };
        split.Panel1.Controls.Add(_lines);
        split.Panel2.Controls.Add(right);
        split.HandleCreated += (_, _) => BeginInvoke(() => { if (split.Width > 500) split.SplitterDistance = (int)(split.Width * 0.58); });

        _detailsTitle.Margin = new Padding(6, 5, 3, 3);
        var page = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(_detailsTitle, 0, 0);
        page.Controls.Add(split, 0, 1);
        return page;
    }

    private void RefreshDetails()
    {
        _shown = _documents.Current;
        if (_shown == null)
        {
            _detailsTitle.Text = "Изберете документ";
            _lines.SetRows([]);
            _postings.SetRows([]);
            _issues.SetRows([]);
            return;
        }

        _detailsTitle.Text = $"{_shown.DocType} {_shown.Title}";
        bool euro = !HasVat || _shown.InForeignCurrency;
        if (_lines.Columns[_euroColumn].Visible != euro)
        {
            _lines.SetRows([]);   // no current cell while its column may disappear
            _lines.Columns[_euroColumn].Visible = euro;
        }

        var postings = PostingsOf(_shown).ToList();
        bool inCurrency = postings.Exists(p => p.InCurrency.Length > 0);
        if (_postings.Columns[_inCurrencyColumn].Visible != inCurrency)
        {
            _postings.SetRows([]);
            _postings.Columns[_inCurrencyColumn].Visible = inCurrency;
        }

        _lines.SetRows(_shown.Lines);
        _postings.SetRows(postings);
        _issues.SetRows(_shown.Issues.OrderByDescending(i => i.IsOpen).ThenByDescending(i => i.Severity));
    }

    private IEnumerable<Posting> PostingsOf(OperationDocument doc)
    {
        string one = IsPurchase ? "Кт" : "Дт", many = IsPurchase ? "Дт" : "Кт";
        string vies = IsPurchase ? "" : "VIES " + (doc.ViesOverride?.ToString(CultureInfo.InvariantCulture)
                                                   ?? string.Join(";", doc.Segments.Select(s => s.Vies).Distinct()));

        var total = new Posting(one, doc.CounterAccountOut, "", doc.TotalGross, With(doc.TotalInCurrency, doc.CounterCurrency), vies, true);
        if (!IsPurchase) yield return total;
        foreach (var s in doc.Segments)
            yield return new Posting(many, s.Account, s.AccountName, s.Amount, With(s.InCurrency, s.Currency), DealTypes.Get(s.DealType).Label, false);
        if (IsPurchase) yield return total;

        if (!doc.IsCash || !Options.General.CashRows) yield break;
        string type = IsPurchase ? DocumentTypes.CashPayment : DocumentTypes.CashReceipt;
        string paid = With(doc.TotalInCurrency, doc.CounterCurrency.Length > 0 ? doc.CounterCurrency : doc.CashCurrency);
        yield return new Posting("Дт", IsPurchase ? doc.CounterAccountOut : doc.CashAccountOut, "", doc.TotalGross, paid,
            $"{type} · {DealTypes.Get(DealTypes.Internal).Label}", false);
        yield return new Posting("Кт", IsPurchase ? doc.CashAccountOut : doc.CounterAccountOut, "", doc.TotalGross, paid, type, false);

        static string With(decimal? amount, string currency) => amount is { } value && currency.Length > 0 ? $"{Fmt.Money(value)} {currency}" : "";
    }

    // ----- preview -----

    private void BuildPreview()
    {
        _preview.Use(_palette);
        _preview.ReadOnly = true;
        _preview.SetHeights(24, 30);

        int[] widths = [250, 230, 88, 200, 130, 130, 70, 136, 66, 160, 200, 130, 150, 116, 70, 152];
        for (int i = 0; i < OperationsLayout.FirstProtocolColumn; i++)
        {
            int c = i;
            var column = OperationsLayout.Columns[c];
            _preview.AddColumn(new() { Header = $"{column.Letter}  {column.Header}", Width = widths[c], Get = r => r.Cells[c] });
        }

        _preview.Columns[1].Frozen = true;
        _preview.StyleCell = (row, _, style) => { if (row.IsCashRow) style.ForeColor = Theme.Muted; };
    }

    private Control BuildPreviewPage()
    {
        _previewTitle.Margin = new Padding(6, 5, 3, 5);
        var page = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(_previewTitle, 0, 0);
        page.Controls.Add(_preview, 0, 1);
        return page;
    }

    private void RefreshPreview()
    {
        var rows = OperationRows.Build(Batch, Options);
        _previewTitle.Text = $"Точно тези {rows.Count} реда ще бъдат записани. Колони Q–AA (протоколи) остават празни в тази версия.";
        _preview.SetRows(rows);
    }

    // ----- what was read -----

    private Control BuildLogPage()
    {
        var text = new List<string>
        {
            $"Файл: {Batch.SourcePath}",
            $"Лист: {Batch.SheetName}",
            $"Вид: {Batch.LayoutName}",
            $"Фирма: {Batch.ClientName} {Batch.ClientEik}",
            $"Документи: {Batch.Documents.Count}",
            $"Редове: {Batch.Documents.Sum(d => d.Lines.Select(l => l.SourceRow).Distinct().Count())}",
            "",
            Batch.SkippedRows.Count == 0 ? "Няма прескочени редове със съдържание." : "Прескочени редове:",
        };
        text.AddRange(Batch.SkippedRows.Select(s => $"   ред {s.Row}: {s.Reason}"));

        return new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false,
            BorderStyle = BorderStyle.None, BackColor = Theme.Surface, Font = Theme.Mono, Text = string.Join(Environment.NewLine, text),
        };
    }
}
