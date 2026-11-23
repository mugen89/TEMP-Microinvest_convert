using System.Globalization;
using MicroinvestConvert.App.Ui;
using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Nomenclature;

namespace MicroinvestConvert.App.Forms;

// The table of documents and the strip that sets one field of many documents at once.
internal sealed partial class WorkView
{
    private readonly Grid<OperationDocument> _documents = new();
    private readonly Dictionary<int, DocField> _fieldOfColumn = [];
    private int _statusColumn, _dealColumn;

    private readonly Label _selected = Make.Label("", font: Theme.Bold);
    private readonly ComboBox _bulkField = Make.Combo(165);
    private readonly ComboBox _bulkValue = Make.Combo(225);
    private readonly FlatButton _bulkSet = new("Задай");
    private List<(string Label, DocField? Field)> _bulkFields = [];

    private void BuildDocuments()
    {
        _documents.Use(_palette);
        _documents.SetHeights(26, 30);

        _documents.AddColumn(new()
        {
            Header = "✓", Width = 34, IsCheck = true, Tip = "Документът се записва във файла",
            Get = d => d.Export, SortKey = d => d.Export,
            Set = (d, v) => { d.Export = v.Length > 0; return null; },
        });
        _statusColumn = _documents.AddColumn(new() { Header = "Състояние", Width = 108, Get = StatusText, SortKey = d => StatusRank(d) });
        _documents.AddColumn(new() { Header = "№", Width = 44, Right = true, Get = d => d.Index, SortKey = d => NumberKey(d.Index), Tip = "№ във файла" });

        Field(DocField.DocType, "Тип", 66, items: ["", .. DocumentTypes.All.Select(t => t.ShortName)],
            label: v => v.Length == 0 ? "(няма)" : DocumentTypes.Find(v)?.Label ?? v, tip: "I · Тип документ");
        Field(DocField.Number, "Документ №", 104, tip: "H · Номер на документа");
        Field(DocField.Date, "Дата", 80, sort: d => d.Date, tip: "J · Дата на документа");
        _documents.AddColumn(new()
        {
            Header = "Сч. дата", Width = 80, Tip = "C · Дата и P · Месец за експорт. Въведете дата, за да я зададете ръчно; изтрийте я, за да важи правилото.",
            Get = d => Fmt.Date(d.AccountingDate), SortKey = d => d.AccountingDate,
            Set = (d, v) => d.TrySet(DocField.AccountingDate, v, out string error) ? null : error,
        });
        _fieldOfColumn[_documents.Columns.Count - 1] = DocField.AccountingDate;
        Field(DocField.PartnerName, IsPurchase ? "Доставчик" : "Клиент", 190, tip: "D · Партньор");
        Field(DocField.Eik, "ЕИК", HasVat ? 92 : 132, tip: "E · Булстат/ЕИК. Петнадесет деветки – няма номер.");
        Field(DocField.VatNumber, "ДДС №", 108, tip: "F · ДДС номер");

        if (HasVat)
        {
            Money("Основа", d => d.TotalBase, "Данъчна основа, както ще се запише");
            Money("ДДС", d => d.TotalVat, "ДДС, както ще се запише");
        }

        Money("Общо", d => d.TotalGross, HasVat ? "Общо по документа" : "Сумата в евро, както ще се запише");
        if (!HasVat)
            _documents.AddColumn(new() { Header = "По фактура", Width = 116, Right = true, Get = InvoiceAmount, Tip = "Сумата във валутата на фактурата" });
        _dealColumn = _documents.AddColumn(new()
        {
            Header = "Вид", Width = 62, Center = true, Tip = "N · Вид сделка. Променя се в редовете на документа или от лентата „Задай“.",
            Get = d => string.Join("; ", d.DealTypesUsed), SortKey = d => string.Join(';', d.DealTypesUsed.Select(n => n.ToString("00", CultureInfo.InvariantCulture))),
        });
        if (!IsPurchase)
        {
            _documents.AddColumn(new()
            {
                Header = "VIES", Width = 54, Center = true, Tip = "O · VIES",
                Items = ["авто", "1", "2", "3", "4"], ItemLabel = v => int.TryParse(v, out int n) ? ViesTypes.Label(n) : "автоматично, според вида сделка",
                Get = d => d.ViesOverride?.ToString(CultureInfo.InvariantCulture) ?? string.Join(";", d.Segments.Select(s => s.Vies).Distinct()),
                Set = (d, v) => d.TrySet(DocField.Vies, v == "авто" ? "" : v, out string error) ? null : error,
            });
            _fieldOfColumn[_documents.Columns.Count - 1] = DocField.Vies;
        }

        Field(DocField.CounterAccount, IsPurchase ? "Кт сметка" : "Дт сметка", 94,
            tip: IsPurchase ? "Сметка на доставчика, както е във файла" : "Сметка на клиента, както е във файла");
        Field(DocField.Payment, "Плащане", 86, items: PaymentMethods.All.Select(PaymentMethods.Label).ToList(),
            label: v => v.Length == 0 ? "(няма)" : v, tip: "„В брой“ добавя втори ред – " + (IsPurchase ? "РКО" : "ПКО"));
        Field(DocField.CashAccount, "Каса", 62, tip: "Касова сметка за плащане в брой");
        Field(DocField.Reason, "Основание", 190, tip: "K · Основание");
        Field(DocField.CashReason, "Основание на плащането", 190, tip: "K · Основание на реда " + (IsPurchase ? "РКО" : "ПКО"));
        Field(DocField.Note, "Забележка", 130, tip: "L · Забележка");
        Field(DocField.Note2, "Втора забележка", 130, tip: "M · Втора забележка");
        Field(DocField.Currency, "Валута", 74);
        Field(DocField.Period, "Период", 78, sort: d => d.Period, tip: "Данъчен период от файла");

        _documents.Columns[3].Frozen = true;
        _documents.StyleCell = StyleDocument;
        _documents.Edited += Edited;
        _documents.SortRequested += RefreshDocuments;
        _documents.CurrentCellChanged += (_, _) => RefreshDetails();
        _documents.SelectionChanged += (_, _) => RefreshBulk();
    }

    private void Field(DocField field, string header, int width, IReadOnlyList<string>? items = null,
        Func<string, string>? label = null, Func<OperationDocument, IComparable?>? sort = null, string tip = "")
    {
        int column = _documents.AddColumn(new()
        {
            Header = header, Width = width, Tip = tip, Items = items, ItemLabel = label, SortKey = sort,
            Get = d => d.GetText(field),
            Set = (d, v) => d.TrySet(field, v, out string error) ? null : error,
        });
        _fieldOfColumn[column] = field;
    }

    private void Money(string header, Func<OperationDocument, decimal> value, string tip) =>
        _documents.AddColumn(new() { Header = header, Width = 88, Right = true, Tip = tip, Get = d => Fmt.Money(value(d)), SortKey = d => value(d) });

    private static string InvoiceAmount(OperationDocument doc) => string.Join("; ", doc.Lines.GroupBy(l => l.InvoiceCurrency)
        .Select(g => $"{Fmt.Money(g.Sum(l => l.InvoiceTotal))} {g.Key}"));

    private static IComparable NumberKey(string index) =>
        decimal.TryParse(index, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal n) ? n : decimal.MaxValue;

    // ----- status -----

    private static int StatusRank(OperationDocument doc) =>
        !doc.Export || doc.SkippedBySetting ? 3 : doc.HasErrors ? 0 : doc.HasWarnings ? 1 : 2;

    private static string StatusText(OperationDocument doc) => StatusRank(doc) switch
    {
        0 => "✖ Грешка",
        1 => "⚠ Провери",
        2 => "✔ Готов",
        _ => "– Изключен",
    };

    private void StyleDocument(OperationDocument doc, int column, DataGridViewCellStyle style)
    {
        int rank = StatusRank(doc);
        if (rank == 0) style.BackColor = Theme.ErrorBack;
        else if (rank == 1) style.BackColor = Theme.WarningBack;
        if (rank == 3) style.ForeColor = Theme.Excluded;

        if (column == _statusColumn)
        {
            style.ForeColor = rank switch { 0 => Theme.ErrorInk, 1 => Theme.WarningInk, 2 => Theme.OkInk, _ => Theme.Excluded };
            style.Font = Theme.Bold;
        }
        else if (_fieldOfColumn.TryGetValue(column, out var field) && doc.IsEdited(field))
        {
            style.ForeColor = Theme.EditedInk;
            style.Font = Theme.Bold;
        }
        else if (column == _dealColumn)
        {
            style.Font = Theme.Bold;
            if (doc.Lines.Any(l => l.DealTypeOverride != null)) style.ForeColor = Theme.EditedInk;
        }
    }

    // ----- page -----

    private Control BuildDocumentsPage()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 6, FixedPanel = FixedPanel.None,
            BackColor = Theme.Window,
        };

        var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        top.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        top.Controls.Add(BuildBulkStrip(), 0, 0);
        top.Controls.Add(_documents, 0, 1);

        split.Panel1.Controls.Add(top);
        split.Panel2.Controls.Add(BuildDetailsPage());
        // The sizes are known only once the window has been laid out.
        split.HandleCreated += (_, _) => BeginInvoke(() =>
        {
            if (split.Height > 300) split.SplitterDistance = (int)(split.Height * 0.58);
        });
        return split;
    }

    private Control BuildBulkStrip()
    {
        _bulkFields =
        [
            ("Основание", DocField.Reason),
            ("Забележка", DocField.Note),
            ("Втора забележка", DocField.Note2),
            ("Вид сделка", null),
            ("Тип документ", DocField.DocType),
            ("Сч. дата", DocField.AccountingDate),
            (IsPurchase ? "Кт сметка" : "Дт сметка", DocField.CounterAccount),
            ("Плащане", DocField.Payment),
            ("Каса", DocField.CashAccount),
            ("Основание на плащането", DocField.CashReason),
            ("Период", DocField.Period),
        ];
        if (!IsPurchase) _bulkFields.Insert(4, ("VIES", DocField.Vies));

        _bulkField.Items.AddRange(_bulkFields.Select(f => (object)f.Label).ToArray());
        _bulkField.SelectedIndexChanged += (_, _) => PrepareBulkValue();
        _bulkField.SelectedIndex = 0;

        _bulkSet.Use(_palette);
        _bulkSet.Click += (_, _) => BulkSet();

        var include = new FlatButton("Включи", ButtonLook.Quiet);
        include.Use(_palette);
        include.Click += (_, _) => BulkExport(true);
        var exclude = new FlatButton("Изключи", ButtonLook.Quiet);
        exclude.Use(_palette);
        exclude.Click += (_, _) => BulkExport(false);
        var revert = new FlatButton("Върни от файла", ButtonLook.Quiet);
        revert.Use(_palette);
        revert.Click += (_, _) => BulkRevert();
        var all = new FlatButton("Избери всички", ButtonLook.Quiet);
        all.Use(_palette);
        all.Click += (_, _) => { _documents.SelectAll(); _documents.Focus(); };

        _selected.Margin = new Padding(6, 8, 6, 3);
        var strip = Make.Flow(_selected, _bulkField, _bulkValue, _bulkSet, Make.Gap(4), include, exclude, revert, all);
        strip.Padding = new Padding(0, 0, 0, 3);
        return strip;
    }

    private void PrepareBulkValue()
    {
        var (_, field) = _bulkFields[_bulkField.SelectedIndex];
        _bulkValue.Items.Clear();
        _bulkValue.Text = "";

        IEnumerable<string>? choices = field switch
        {
            null => new[] { "автоматично" }.Concat(
                DealTypes.ChoicesFor(IsPurchase ? DealSide.Purchase : DealSide.Sale).Select(d => d.Label)),
            DocField.DocType => DocumentTypes.All.Select(t => t.Label),
            DocField.Payment => PaymentMethods.All.Where(m => m != PaymentMethod.Unknown).Select(PaymentMethods.Label),
            DocField.Vies => new[] { "автоматично" }.Concat(ViesTypes.All.Select(v => ViesTypes.Label(v.Code))),
            _ => null,
        };

        _bulkValue.DropDownStyle = choices != null ? ComboBoxStyle.DropDownList : ComboBoxStyle.DropDown;
        if (choices != null)
        {
            _bulkValue.Items.AddRange([.. choices]);
            _bulkValue.SelectedIndex = 0;
        }
        else if (field is { } textField)
        {
            // What is already used in the file, most frequent first.
            _bulkValue.Items.AddRange([.. Batch.Documents.Select(d => d.GetText(textField)).Where(t => t.Length > 0)
                .GroupBy(t => t).OrderByDescending(g => g.Count()).Take(20).Select(g => (object)g.Key)]);
        }
    }

    private void BulkSet()
    {
        var chosen = _documents.SelectedItems();
        if (chosen.Count == 0) return;

        var (label, field) = _bulkFields[_bulkField.SelectedIndex];
        string value = _bulkValue.Text.Trim();
        if (value == "автоматично") value = "";

        if (value.Length == 0 && field is not (null or DocField.Vies or DocField.AccountingDate) &&
            !Dialogs.Ask(this, $"Да изчистя ли „{label}“?", $"Полето ще остане празно в {Fmt.Documents(chosen.Count)}.", "Изчисти", "Отказ", warning: true))
            return;

        var errors = new List<string>();
        foreach (var doc in chosen)
        {
            string error = "";
            bool ok = field is { } f
                ? doc.TrySet(f, value, out error)
                : doc.Lines.TrueForAll(l => l.TrySet(LineField.DealType, value, out error));
            if (!ok && !errors.Contains(error)) errors.Add(error);
        }

        Edited(chosen);
        if (errors.Count > 0) Dialogs.Warning(this, "Стойността не е приета", string.Join("\n", errors));
    }

    private void BulkExport(bool export)
    {
        var chosen = _documents.SelectedItems();
        foreach (var doc in chosen) doc.Export = export;
        if (chosen.Count > 0) Edited(chosen);
    }

    private void BulkRevert()
    {
        var chosen = _documents.SelectedItems().Where(d => d.AnyEdited || !d.Export).ToList();
        if (chosen.Count == 0) return;
        if (!Dialogs.Ask(this, "Да върна ли стойностите от файла?", $"Корекциите в {Fmt.Documents(chosen.Count)} ще се загубят.",
                "Върни от файла", "Отказ", warning: true))
            return;
        foreach (var doc in chosen) doc.Revert();
        Edited(chosen);
    }

    private void RefreshDocuments() => _documents.SetRows(Batch.Documents.Where(Matches));

    private void RefreshBulk()
    {
        int count = _documents.SelectedItems().Count;
        _selected.Text = count == 0 ? "Няма избран документ" : $"За избраните ({count}):";
        _bulkSet.Enabled = count > 0;
    }
}
