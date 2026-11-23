using System.Diagnostics;
using MicroinvestConvert.App.Ui;
using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Nomenclature;
using MicroinvestConvert.Core.Output;
using MicroinvestConvert.Core.Rules;
using MicroinvestConvert.Core.Settings;
using MicroinvestConvert.Core.Source;

namespace MicroinvestConvert.App.Forms;

/// <summary>The opened file: its documents, what is in each of them, and what will be written.</summary>
internal sealed partial class WorkView : AppView
{
    private enum Filter
    {
        All,
        Problems,
        Errors,
        Edited,
        Excluded,
    }

    private static readonly (Filter Filter, string Label)[] Filters =
    [
        (Filter.All, "Всички документи"),
        (Filter.Problems, "С грешки и предупреждения"),
        (Filter.Errors, "Само с грешки"),
        (Filter.Edited, "Коригирани"),
        (Filter.Excluded, "Няма да се запишат"),
    ];

    private readonly AppState _app;
    private readonly Palette _palette;
    private DocumentProcessor _processor;
    private bool _editsSinceSave;

    public OperationBatch Batch { get; }

    /// <summary>Corrections that are not in a written file yet.</summary>
    public bool HasUnsavedEdits => _editsSinceSave;

    public event Action? BackRequested;
    public event Action? OpenRequested;

    private ConverterOptions Options => _processor.Options;
    private bool IsPurchase => Batch.Kind == OperationKind.Purchase;

    // ----- controls -----

    private readonly CheckBox _dateRule = Make.Check("Сч. дата = дата на документа");
    private readonly FlatButton _vatCheck = new("Проверка на ДДС");
    private readonly FlatButton _accounts = new("Сметки…");
    private readonly Label _exception = Make.Label("", Theme.WarningInk, Theme.Bold);
    private readonly ComboBox _filter = Make.Combo(200);
    private readonly TextBox _search = Make.TextBox(160, "Търсене");
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly Label _counts = Make.Label("", font: Theme.Bold);
    private readonly Label _errorCount = Make.Label("", Theme.ErrorInk, Theme.Bold);
    private readonly Label _warningCount = Make.Label("", Theme.WarningInk, Theme.Bold);
    private readonly Label _totals = Make.Label("");
    private readonly CheckBox _openAfter = Make.Check("Отвори след запис");
    private readonly FlatButton _save = new("Запиши за Делта Pro…", ButtonLook.Primary);
    private readonly ToolTip _tips = new() { AutoPopDelay = 15000 };

    public WorkView(AppState app, OperationBatch batch)
    {
        _app = app;
        Batch = batch;
        _palette = Palette.For(batch.Kind);
        _processor = new DocumentProcessor(app.Settings.OptionsFor(batch.ClientEik));
        _processor.Process(batch);

        BuildDocuments();
        BuildDetails();
        BuildPreview();

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(BuildTitle(), 0, 0);
        root.Controls.Add(BuildToolbar(), 0, 1);
        root.Controls.Add(BuildTabs(), 0, 2);
        root.Controls.Add(BuildFooter(), 0, 3);
        Controls.Add(root);

        RefreshAll();
        EndLayout();
    }

    // ----- title, toolbar, footer -----

    private Control BuildTitle()
    {
        string kind = IsPurchase ? "Покупки" : "Продажби";
        string client = Batch.ClientName.Length > 0 ? Batch.ClientName : "Фирма без име";
        if (Batch.ClientEik.Length > 0) client += $" ({Batch.ClientEik})";

        var back = new FlatButton("← Начало", ButtonLook.Quiet);
        back.Use(_palette);
        back.Click += (_, _) => BackRequested?.Invoke();
        var open = new FlatButton("Отвори друг файл…", ButtonLook.Quiet);
        open.Use(_palette);
        open.Click += (_, _) => OpenRequested?.Invoke();

        var title = Make.Label($"{kind}  ·  {client}", _palette.AccentDark, Theme.Heading);
        title.Margin = new Padding(6, 7, 3, 3);
        var file = Make.Label($"{Path.GetFileName(Batch.SourcePath)}  ·  {Batch.LayoutName}", Theme.Muted, Theme.Small);
        file.Margin = new Padding(8, 9, 3, 3);

        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, Margin = Padding.Empty,
            Padding = new Padding(8, 3, 8, 3), BackColor = _palette.Soft,
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.Controls.Add(back, 0, 0);
        bar.Controls.Add(title, 1, 0);
        bar.Controls.Add(file, 2, 0);
        bar.Controls.Add(open, 3, 0);
        return bar;
    }

    private Control BuildToolbar()
    {
        _dateRule.Checked = _app.Settings.General.AccountingDateEqualsDocumentDate;
        _dateRule.CheckedChanged += (_, _) =>
        {
            _app.Settings.General.AccountingDateEqualsDocumentDate = _dateRule.Checked;
            _app.Save();
            Reprocess();
        };
        _tips.SetToolTip(_dateRule,
            "Изключено: счетоводната дата (кол. C) е първият ден на данъчния период, но не преди датата на документа.\n" +
            "Включено: винаги е датата на документа. Месецът за експорт (кол. P) е равен на нея.");

        _vatCheck.Use(_palette);
        _vatCheck.Click += (_, _) => ShowVatCheck();
        _accounts.Use(_palette);
        _accounts.Click += (_, _) => ShowAccounts();

        _filter.Items.AddRange(Filters.Select(f => (object)f.Label).ToArray());
        _filter.SelectedIndex = 0;
        _filter.SelectedIndexChanged += (_, _) => RefreshDocuments();
        _search.TextChanged += (_, _) => RefreshDocuments();

        var left = Make.Flow(_dateRule, Make.Gap(10), _vatCheck, _accounts, _exception);
        var right = Make.Flow(_filter, _search);

        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty, Padding = new Padding(10, 4, 10, 4),
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.Controls.Add(left, 0, 0);
        bar.Controls.Add(right, 1, 0);
        return bar;
    }

    private Control BuildTabs()
    {
        var documents = new TabPage("Документи") { BackColor = Theme.Window, Padding = new Padding(4) };
        documents.Controls.Add(BuildDocumentsPage());
        var preview = new TabPage("Преглед на файла") { BackColor = Theme.Window, Padding = new Padding(4) };
        preview.Controls.Add(BuildPreviewPage());
        var log = new TabPage("Прочетено от файла") { BackColor = Theme.Window, Padding = new Padding(4) };
        log.Controls.Add(BuildLogPage());

        _tabs.TabPages.AddRange([documents, preview, log]);
        _tabs.SelectedIndexChanged += (_, _) => { if (_tabs.SelectedIndex == 1) RefreshPreview(); };
        return _tabs;
    }

    private Control BuildFooter()
    {
        _save.Use(_palette);
        _save.Click += (_, _) => Save();
        _openAfter.Checked = _app.Settings.OpenAfterSave;
        _openAfter.CheckedChanged += (_, _) =>
        {
            _app.Settings.OpenAfterSave = _openAfter.Checked;
            _app.Save();
        };
        _openAfter.Margin = new Padding(3, 10, 10, 3);
        foreach (var label in new[] { _counts, _errorCount, _warningCount, _totals }) label.Margin = new Padding(3, 9, 14, 3);

        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, Margin = Padding.Empty,
            Padding = new Padding(12, 6, 10, 8), BackColor = Theme.Surface,
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.Controls.Add(Make.Flow(_counts, _errorCount, _warningCount, _totals), 0, 0);
        bar.Controls.Add(_openAfter, 1, 0);
        bar.Controls.Add(_save, 2, 0);
        bar.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawLine(pen, 0, 0, bar.Width, 0);
        };
        return bar;
    }

    // ----- recalculation and refresh -----

    /// <summary>After the settings window.</summary>
    public void SettingsChanged()
    {
        _dateRule.Checked = _app.Settings.General.AccountingDateEqualsDocumentDate;
        Reprocess();
    }

    private void Reprocess()
    {
        _processor = new DocumentProcessor(_app.Settings.OptionsFor(Batch.ClientEik));
        _processor.Process(Batch);
        RefreshAll();
    }

    private void DocumentsEdited(IReadOnlyList<OperationDocument> documents)
    {
        foreach (var doc in documents) _processor.Process(Batch, doc);
        _editsSinceSave = true;
        _documents.Invalidate();
        RefreshDetails();
        RefreshSummary();
        if (_tabs.SelectedIndex == 1) RefreshPreview();
    }

    private void RefreshAll()
    {
        RefreshDocuments();
        RefreshDetails();
        RefreshSummary();
        if (_tabs.SelectedIndex == 1) RefreshPreview();
    }

    private void RefreshSummary()
    {
        var docs = Batch.Documents;
        var written = docs.Where(d => d.WillExport).ToList();
        int errors = docs.Count(d => d is { HasErrors: true, Export: true, SkippedBySetting: false });
        int warnings = written.Count(d => d.HasWarnings);
        int left = docs.Count - written.Count - errors;

        _counts.Text = $"За запис: {written.Count} от {docs.Count}" + (left > 0 ? $"  (изключени: {left})" : "");
        _errorCount.Text = errors > 0 ? $"✖ с грешки: {errors}" : "";
        _errorCount.Visible = errors > 0;
        _warningCount.Text = warnings > 0 ? $"⚠ за преглед: {warnings}" : "";
        _warningCount.Visible = warnings > 0;
        _totals.Text = $"основа {Fmt.Money(written.Sum(d => d.TotalBase))}    ДДС {Fmt.Money(written.Sum(d => d.TotalVat))}" +
                       $"    общо {Fmt.Money(written.Sum(d => d.TotalGross))}";
        _save.Enabled = written.Count > 0;

        int vat = VatCheckForm.OpenQuestions(Batch);
        _vatCheck.Text = vat > 0 ? $"Проверка на ДДС ({vat})" : "Проверка на ДДС";
        _vatCheck.Font = vat > 0 ? Theme.Bold : Theme.Base;

        // The VAT accounts are never shown — unless they are not the usual ones.
        string account = IsPurchase ? Options.PurchaseVatAccount : Options.SaleVatAccount;
        string usual = IsPurchase ? ConverterSettings.DefaultPurchaseVatAccount : ConverterSettings.DefaultSaleVatAccount;
        _exception.Text = account != usual ? $"ДДС сметка: {account}" : "";
        _exception.Visible = account != usual;
    }

    private bool Matches(OperationDocument doc)
    {
        bool passes = _filter.SelectedIndex < 0 ? true : Filters[_filter.SelectedIndex].Filter switch
        {
            Filter.Problems => doc.HasErrors || doc.HasWarnings,
            Filter.Errors => doc.HasErrors,
            Filter.Edited => doc.AnyEdited,
            Filter.Excluded => !doc.WillExport,
            _ => true,
        };
        if (!passes) return false;

        string needle = _search.Text.Trim();
        if (needle.Length == 0) return true;
        return new[] { doc.Number, doc.PartnerName, doc.Eik, doc.VatNumber, doc.Reason, doc.Note, doc.DocType, Fmt.Date(doc.Date) }
            .Concat(doc.Lines.Select(l => l.AccountKey))
            .Concat(doc.Issues.Select(i => i.Message))
            .Any(t => t.Contains(needle, StringComparison.CurrentCultureIgnoreCase));
    }

    // ----- the other windows -----

    private void ShowVatCheck()
    {
        using var form = new VatCheckForm(_app, Batch, () =>
        {
            _processor = new DocumentProcessor(_app.Settings.OptionsFor(Batch.ClientEik));
            _processor.Process(Batch);
        });
        form.ShowDialog(this);
        if (form.EditsMade) _editsSinceSave = true;
        RefreshAll();
    }

    private void ShowAccounts()
    {
        using var form = new AccountsForm(_app, Batch);
        if (form.ShowDialog(this) == DialogResult.OK) Reprocess();
    }

    // ----- writing the file -----

    public void Save()
    {
        EndEdits();
        var written = Batch.Documents.Where(d => d.WillExport).ToList();
        if (written.Count == 0) return;
        if (!ConfirmOpenQuestions(written)) return;

        using var dialog = new SaveFileDialog
        {
            Title = "Файл за Microinvest Делта Pro",
            Filter = "Excel 97-2003 (*.xls)|*.xls",
            DefaultExt = "xls",
            AddExtension = true,
            InitialDirectory = Path.GetDirectoryName(Batch.SourcePath),
            FileName = Path.GetFileNameWithoutExtension(Batch.SourcePath) + "_Microinvest.xls",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        // Делта Pro reads .xls only, whatever was typed.
        string path = Path.ChangeExtension(dialog.FileName, DeltaWorkbook.Extension);
        if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(Batch.SourcePath), StringComparison.OrdinalIgnoreCase))
        {
            Dialogs.Warning(this, "Изберете друго име", "Това е самият файл от AI Accounting.");
            return;
        }

        var rows = OperationRows.Build(Batch, Options);
        Cursor = Cursors.WaitCursor;
        try
        {
            DeltaWorkbook.Write(rows, path);
        }
        catch (ConvertException ex)
        {
            Cursor = Cursors.Default;
            Dialogs.Error(this, "Записът не успя", ex.Message);
            return;
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        _editsSinceSave = false;
        ReportSaved(path, written.Count, rows.Count);
    }

    private bool ConfirmOpenQuestions(List<OperationDocument> written)
    {
        int four = written.Count(d => d.Issues.Any(i => i.Code == IssueCode.VatUnresolved));
        int other = written.Count(d => d.HasWarnings) - four;
        if (four == 0 && other == 0) return true;

        string text = "";
        if (four > 0)
            text += $"• {Fmt.Documents(four)} с вид сделка 4, защото ДДС не отговаря на 20% или 9%.\n";
        if (other > 0)
            text += $"• {Fmt.Documents(other)} с други предупреждения.\n";
        return Dialogs.Ask(this, "Има документи за преглед", text + "\nДа запиша ли файла така?", "Запиши", "Прегледай", warning: true);
    }

    private void ReportSaved(string path, int documents, int rows)
    {
        int left = Batch.Documents.Count - documents;
        string text = $"{Fmt.Documents(documents)}, {rows} реда. Файлът е прочетен обратно и съвпада.\n\n{path}";
        if (left > 0) text += $"\n\nНе са записани: {Fmt.Documents(left)} – вижте филтъра „Няма да се запишат“.";
        Dialogs.Info(this, "Файлът е записан", text);

        if (!_openAfter.Checked) return;
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Dialogs.Warning(this, "Файлът е записан, но не можа да бъде отворен", ex.Message);
        }
    }

    private void EndEdits()
    {
        _documents.EndEdit();
        _lines.EndEdit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tips.Dispose();
        base.Dispose(disposing);
    }
}
