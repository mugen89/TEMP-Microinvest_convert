using MicroinvestConvert.App.Forms.Checks;
using MicroinvestConvert.App.Ui;
using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Nomenclature;
using MicroinvestConvert.Core.Output;
using MicroinvestConvert.Core.Rules;
using MicroinvestConvert.Core.Settings;
using MicroinvestConvert.Core.Source;

namespace MicroinvestConvert.App.Forms;

/// <summary>The opened file: its documents, what is in each of them, and what will be written.</summary>
internal sealed partial class WorkView : AppView, ICheckHost
{
    private enum Filter
    {
        All,
        Problems,
        Errors,
        Edited,
        Excluded,
        Currency,
    }

    private static readonly (Filter Filter, string Label)[] Filters =
    [
        (Filter.All, "Всички документи"),
        (Filter.Problems, "С грешки и предупреждения"),
        (Filter.Errors, "Само с грешки"),
        (Filter.Edited, "Коригирани"),
        (Filter.Excluded, "Няма да се запишат"),
        (Filter.Currency, "Във валута"),
    ];

    private readonly AppState _app;
    private readonly Palette _palette;
    private DocumentProcessor _processor;
    private bool _editsSinceSave;
    private ChecksForm? _checks;

    public OperationBatch Batch { get; }

    /// <summary>Corrections that are not in a written file yet.</summary>
    public bool HasUnsavedEdits => _editsSinceSave;

    /// <summary>After anything was calculated again, corrected or written.</summary>
    public event Action? Changed;

    public ConverterOptions Options => _processor.Options;
    AppState ICheckHost.App => _app;

    /// <summary>Purchases, at home and from abroad: many debits against one supplier.</summary>
    private bool IsPurchase => Batch.Kind.IsPurchaseSide();
    private bool HasVat => Batch.Kind != OperationKind.Foreign;

    // ----- controls -----

    private readonly CheckBox _dateRule = Make.Check("Сч. дата = дата на документа");
    private readonly FlatButton _allChecks = new("Проверки");
    private readonly FlatButton _vatCheck = new("ДДС");
    private readonly FlatButton _accounts = new("Сметки");
    private readonly FlatButton _rates = new("Курсове");
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
    private readonly System.Windows.Forms.Timer _afterEdit = new() { Interval = 300 };

    public WorkView(AppState app, OperationBatch batch)
    {
        _app = app;
        Batch = batch;
        _palette = Palette.For(batch.Kind);
        _processor = NewProcessor();
        _processor.Process(batch);

        BuildDocuments();
        BuildDetails();
        BuildPreview();

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(BuildToolbar(), 0, 0);
        root.Controls.Add(BuildTabs(), 0, 1);
        root.Controls.Add(BuildFooter(), 0, 2);
        Controls.Add(root);

        RefreshAll();
        app.SettingsChanged += SettingsChanged;
        app.Rates.Arrived += RatesArrived;
        app.Rates.Changed += RefreshRates;
        _afterEdit.Tick += (_, _) => RatesArrived();
        AskForRates();
        EndLayout();
    }

    private DocumentProcessor NewProcessor() => new(_app.OptionsFor(Batch), _app.Rates.Table);

    // ----- toolbar, footer -----

    private Control BuildToolbar()
    {
        _dateRule.Checked = _app.Settings.General.AccountingDateEqualsDocumentDate;
        _dateRule.CheckedChanged += (_, _) =>
        {
            if (_app.Settings.General.AccountingDateEqualsDocumentDate == _dateRule.Checked) return;
            _app.Settings.General.AccountingDateEqualsDocumentDate = _dateRule.Checked;
            _app.Save();
            _app.RaiseSettingsChanged();
        };
        _tips.SetToolTip(_dateRule,
            "Изключено: счетоводната дата (кол. C) е първият ден на данъчния период, но не преди датата на документа.\n" +
            "Включено: винаги е датата на документа. Месецът за експорт (кол. P) е равен на нея.");

        _allChecks.Use(_palette);
        _allChecks.Click += (_, _) => ShowChecks();
        _tips.SetToolTip(_allChecks, "Отделен прозорец: всяка проверка, какво е намерила и къде се поправя.");
        _vatCheck.Use(_palette);
        _vatCheck.Click += (_, _) => ShowChecks(VatPage.PageTitle);
        _vatCheck.Visible = HasVat;
        _accounts.Use(_palette);
        _accounts.Click += (_, _) => ShowChecks(AccountsPage.PageTitle);
        _rates.Use(_palette);
        _rates.Click += (_, _) => ShowRates();
        _tips.SetToolTip(_rates, "Валутни курсове: за кои дни ги има и изтегляне.");

        _filter.Items.AddRange(Filters.Select(f => (object)f.Label).ToArray());
        _filter.SelectedIndex = 0;
        _filter.SelectedIndexChanged += (_, _) => RefreshDocuments();
        _search.TextChanged += (_, _) => RefreshDocuments();

        var left = Make.Flow(_dateRule, Make.Gap(10), _allChecks, _vatCheck, _accounts, _rates, _exception);
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
        _openAfter.Margin = new Padding(3, 10, 8, 3);
        foreach (var label in new[] { _counts, _errorCount, _warningCount, _totals }) label.Margin = new Padding(3, 9, 8, 3);

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

    // Changed in any window: the settings, „Проверки", the same box of another module.
    private void SettingsChanged()
    {
        _dateRule.Checked = _app.Settings.General.AccountingDateEqualsDocumentDate;
        _openAfter.Checked = _app.Settings.OpenAfterSave;
        _processor = NewProcessor();
        _processor.Process(Batch);
        RefreshAll();
        AskForRates();
    }

    void ICheckHost.SettingsChanged() => _app.RaiseSettingsChanged();

    /// <summary>Documents were corrected, here or in „Проверки".</summary>
    public void Edited(IReadOnlyList<OperationDocument> documents)
    {
        _editsSinceSave = true;
        Ticked(documents);
    }

    /// <summary>The same without a correction to save.</summary>
    public void Ticked(IReadOnlyList<OperationDocument> documents)
    {
        _processor.Process(Batch, documents);
        RefreshInPlace();
        AskForRates();
    }

    // The rows stay where they are, also when they leave the filter, and so do the chosen ones.
    private void RefreshInPlace()
    {
        ShowCurrencyColumns();
        _documents.Invalidate();
        RefreshDetails();
        RefreshSummary();
        if (_tabs.SelectedIndex == 1) RefreshPreview();
        Changed?.Invoke();
    }

    private void RefreshAll()
    {
        ShowCurrencyColumns();
        RefreshDocuments();
        RefreshDetails();
        RefreshSummary();
        if (_tabs.SelectedIndex == 1) RefreshPreview();
        Changed?.Invoke();
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
        _totals.Text = (HasVat
                           ? $"основа {Fmt.Money(written.Sum(d => d.TotalBase))}   ДДС {Fmt.Money(written.Sum(d => d.TotalVat))}   "
                           : "") + $"общо {Fmt.Money(written.Sum(d => d.TotalGross))}";
        _save.Enabled = written.Count > 0;

        int open = docs.Count(d => d is { Export: true, SkippedBySetting: false } && d.Issues.Any(i => i.IsOpen));
        _allChecks.Text = open > 0 ? $"Проверки ({open})" : "Проверки";
        _allChecks.Font = open > 0 ? Theme.Bold : Theme.Base;

        RefreshRates();
        int vat = VatPage.OpenQuestions(Batch);
        _vatCheck.Text = vat > 0 ? $"ДДС ({vat})" : "ДДС";
        _vatCheck.Font = vat > 0 ? Theme.Bold : Theme.Base;

        // The VAT accounts are never shown — unless they are not the usual ones.
        string account = IsPurchase ? Options.PurchaseVatAccount : Options.SaleVatAccount;
        string usual = IsPurchase ? ConverterSettings.DefaultPurchaseVatAccount : ConverterSettings.DefaultSaleVatAccount;
        _exception.Text = account != usual ? $"ДДС сметка: {account}" : "";
        _exception.Visible = account != usual && HasVat;
    }

    // ----- exchange rates -----

    /// <summary>The days whose rates the documents need and are not there, the newest last.</summary>
    private List<DateTime> UnknownRateDays() => Batch.Documents.Select(d => d.UnknownRateDay).OfType<DateTime>().Distinct().Order().ToList();

    // They come by themselves, when that is allowed, and everything is calculated again.
    private void AskForRates() => _app.Rates.Need(UnknownRateDays());

    // From the background, at any moment: what is being typed is let to be finished.
    private void RatesArrived()
    {
        _afterEdit.Enabled = IsTyping(this) || (_checks != null && IsTyping(_checks));
        if (_afterEdit.Enabled) return;

        _processor.Process(Batch);
        RefreshInPlace();
        AskForRates();
    }

    private static bool IsTyping(Control parent) => parent.Controls.Cast<Control>().Any(c =>
        c is DataGridView { IsCurrentCellInEditMode: true, EditingControl: TextBox or ComboBox { DroppedDown: true } } || IsTyping(c));

    private void ShowRates()
    {
        EndEdits();
        using var form = new RatesForm(_app, UnknownRateDays());
        form.ShowDialog(FindForm());
    }

    private void RefreshRates()
    {
        if (IsDisposed) return;
        int waiting = Batch.Documents.Count(d => d.Issues.Any(i => i.Code == IssueCode.MissingRate));
        _rates.Visible = Batch.Documents.Any(d => d.InForeignCurrency);
        _rates.Text = waiting == 0 ? "Курсове" : _app.Rates.IsRunning ? "Курсове…" : $"Курсове ({waiting})";
        _rates.Font = waiting > 0 ? Theme.Bold : Theme.Base;
    }

    private bool Matches(OperationDocument doc)
    {
        bool passes = _filter.SelectedIndex < 0 ? true : Filters[_filter.SelectedIndex].Filter switch
        {
            Filter.Problems => doc.HasErrors || doc.HasWarnings,
            Filter.Errors => doc.HasErrors,
            Filter.Edited => doc.AnyEdited,
            Filter.Excluded => !doc.WillExport,
            Filter.Currency => doc.InForeignCurrency,
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

    // ----- „Проверки" -----

    private void ShowChecks(string page = "")
    {
        EndEdits();
        if (_checks == null)
        {
            _checks = new ChecksForm(this);
            _checks.FormClosed += (_, _) => _checks = null;
            if (page.Length > 0) _checks.ShowPage(page);
            _checks.Show(FindForm());
            return;
        }

        if (page.Length > 0) _checks.ShowPage(page);
        _checks.Activate();
    }

    public void CloseChecks() => _checks?.Close();

    /// <summary>Goes to a document, whatever the filter hides.</summary>
    public void Reveal(OperationDocument document)
    {
        _tabs.SelectedIndex = 0;
        if (!_documents.GoTo(document))
        {
            _search.Clear();
            _filter.SelectedIndex = 0;
            _documents.GoTo(document);
        }

        FindForm()?.Activate();
        _documents.Focus();
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
        Changed?.Invoke();
        ReportSaved(path, written.Count, rows.Count);
    }

    private bool ConfirmOpenQuestions(List<OperationDocument> written)
    {
        int four = written.Count(d => d.Issues.Any(i => i is { Code: IssueCode.VatUnresolved, Reviewed: false }));
        int other = written.Count(d => d.HasWarnings) - four;
        if (four == 0 && other == 0) return true;

        string text = "";
        if (four > 0)
            text += $"• {Fmt.Documents(four)} с вид сделка 4, защото ДДС не отговаря на 20% или 9%.\n";
        if (other > 0)
            text += $"• {Fmt.Documents(other)} с други предупреждения.\n";
        if (Dialogs.Ask(this, "Има документи за преглед", text + "\nДа запиша ли файла така?", "Запиши", "Прегледай", warning: true)) return true;
        ShowChecks();
        return false;
    }

    private void ReportSaved(string path, int documents, int rows)
    {
        int left = Batch.Documents.Count - documents;
        string text = $"{Fmt.Documents(documents)}, {rows} реда. Файлът е прочетен обратно и съвпада.\n\n{path}";
        if (left > 0) text += $"\n\nНе са записани: {Fmt.Documents(left)} – вижте филтъра „Няма да се запишат“.";
        Dialogs.Info(this, "Файлът е записан", text);

        if (_openAfter.Checked) ExcelFiles.Open(this, path);
    }

    private void EndEdits()
    {
        _documents.EndEdit();
        _lines.EndEdit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _app.SettingsChanged -= SettingsChanged;
            _app.Rates.Arrived -= RatesArrived;
            _app.Rates.Changed -= RefreshRates;
            CloseChecks();
            _afterEdit.Dispose();
            _tips.Dispose();
        }

        base.Dispose(disposing);
    }
}
