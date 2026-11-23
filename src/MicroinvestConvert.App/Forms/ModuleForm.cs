using MicroinvestConvert.App.Ui;
using MicroinvestConvert.Core.Model;

namespace MicroinvestConvert.App.Forms;

/// <summary>What the window of a module asks of the start window.</summary>
internal interface IHub
{
    AppState App { get; }
    void OpenFiles(IReadOnlyList<string> paths, OperationKind? expected, Form asker);
    void ShowModule(OperationKind kind);
    void ShowStart();
    void ShowSettings(Form asker, OperationBatch? batch);

    /// <summary>A file was opened, saved or corrected: the cards say so.</summary>
    void ModuleChanged();
}

/// <summary>The window of one module: its strip on top, and the opened file or a place to drop one.</summary>
internal sealed class ModuleForm : AppForm
{
    private readonly IHub _hub;
    private readonly Palette _palette;
    private readonly int _step;
    private readonly Panel _content = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly Label _title = Make.Label("", font: Theme.Heading);
    private readonly Label _file = Make.Label("", Theme.Muted, Theme.Small);
    private readonly DropHint _hint;
    private readonly Control _empty;
    private WorkView? _work;

    public OperationKind Kind { get; }
    public OperationBatch? Batch => _work?.Batch;

    /// <summary>Corrections that are not in a written file yet.</summary>
    public bool HasUnsavedEdits => _work is { HasUnsavedEdits: true };

    public ModuleForm(IHub hub, OperationKind kind, int step)
    {
        _hub = hub;
        _step = step;
        Kind = kind;
        _palette = Palette.For(kind);

        ClientSize = new Size(1360, 800);
        MinimumSize = new Size(1140, 620);
        StartPosition = FormStartPosition.Manual;
        KeyPreview = true;

        _hint = new DropHint(_palette, "Пуснете файла, за да го отворя");
        _empty = BuildEmpty();

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(BuildStrip(), 0, 0);
        root.Controls.Add(_content, 0, 1);
        Controls.Add(_hint);
        Controls.Add(root);

        _content.Controls.Add(_empty);
        ExcelFiles.AcceptDrops(this, files => _hub.OpenFiles(files, Kind, this), on => _hint.Show(on && _work != null));
        RefreshTitle();
        EndLayout();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (Owner is { } start) CenterOver(start, _step * LogicalToDeviceUnits(26));
    }

    // ----- strip and empty window -----

    private Control BuildStrip()
    {
        FlatButton Quiet(string text, Action act, string tip = "")
        {
            var button = new FlatButton(text, ButtonLook.Quiet) { TabStop = false };
            button.Use(_palette);
            button.Click += (_, _) => act();
            if (tip.Length > 0) new ToolTip { AutoPopDelay = 15000 }.SetToolTip(button, tip);
            return button;
        }

        var back = Quiet("← Начало", () =>
        {
            _work?.CloseChecks();
            Hide();
            _hub.ModuleChanged();
            _hub.ShowStart();
        }, "Към началния прозорец. Файлът и корекциите остават тук.");

        _title.ForeColor = _palette.AccentDark;
        _title.Margin = new Padding(6, 7, 3, 3);
        _file.Margin = new Padding(8, 0, 3, 0);
        _file.AutoSize = false;
        _file.AutoEllipsis = true;
        _file.Dock = DockStyle.Fill;
        _file.TextAlign = ContentAlignment.MiddleLeft;

        var right = new List<Control>();
        foreach (var other in OperationKinds.All.Where(k => k != Kind))
            right.Add(Quiet(other.Title(), () => _hub.ShowModule(other), $"Прозорецът „{other.Title()}“"));
        right.Add(Make.Gap(8));
        right.Add(Quiet("Настройки", () => _hub.ShowSettings(this, Batch)));
        right.Add(Quiet("Отвори файл…", PickFile, "Ctrl+O"));

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
        bar.Controls.Add(_title, 1, 0);
        bar.Controls.Add(_file, 2, 0);
        bar.Controls.Add(Make.Flow([.. right]), 3, 0);
        return bar;
    }

    private Control BuildEmpty()
    {
        string hint = Kind switch
        {
            OperationKind.Purchase => "Експорт „Покупки“ от AI Accounting (.xlsx)",
            OperationKind.Sale => "Експорт „Продажби“ от AI Accounting (.xlsx)",
            _ => "Експорт „Чуждестранни“ от AI Accounting (.xlsx) – фактурите се записват в евро, с вид сделка 1",
        };

        var zone = new DropZone(_palette, "Пуснете файла тук", hint, large: true);
        zone.Dropped += files => _hub.OpenFiles(files, Kind, this);
        zone.PickRequested += PickFile;

        var page = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, BackColor = Theme.Window };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
        page.Controls.Add(zone, 1, 1);
        return page;
    }

    private void RefreshTitle()
    {
        string title = Kind.Title();
        _file.Text = "";
        Text = $"{title} – {AppInfo.Name} {AppInfo.VersionText}";

        if (Batch is { } batch)
        {
            string client = batch.ClientName.Length > 0 ? batch.ClientName : "Фирма без име";
            if (batch.ClientEik.Length > 0) client += $" ({batch.ClientEik})";
            title += "  ·  " + client;
            _file.Text = $"{Path.GetFileName(batch.SourcePath)}  ·  {batch.LayoutName}";
            Text = $"{Kind.Title()} – {Path.GetFileName(batch.SourcePath)} – {AppInfo.Name} {AppInfo.VersionText}";
        }

        _title.Text = title;
    }

    // ----- the file -----

    private void PickFile()
    {
        if (ExcelFiles.Pick(this) is { } path) _hub.OpenFiles([path], Kind, this);
    }

    public void Show(OperationBatch batch)
    {
        var work = new WorkView(_hub.App, batch);
        work.Changed += _hub.ModuleChanged;

        _content.SuspendLayout();
        _content.Controls.Clear();
        _work?.Dispose();
        _work = work;
        _content.Controls.Add(work);
        _content.ResumeLayout();

        RefreshTitle();
        _hub.ModuleChanged();
    }

    /// <summary>Corrections live in memory only, so leaving them behind is asked about.</summary>
    public bool LeaveWork()
    {
        if (!HasUnsavedEdits) return true;
        return Dialogs.Ask(this, $"Има незаписани корекции в „{Kind.Title()}“",
            "Корекциите важат само за отворения файл и ще се загубят.", "Продължи без тях", "Остани", warning: true);
    }

    // ----- the window -----

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Also when the start window is being closed, which closes this one. Before an update it
        // has been asked already.
        bool asked = e.CloseReason is not (CloseReason.UserClosing or CloseReason.FormOwnerClosing) || Program.RestartRequested;
        if (!asked && !LeaveWork()) e.Cancel = true;
        base.OnFormClosing(e);
    }

    // Without a button on the taskbar a minimised window would be a stub above it: the whole
    // program is minimised instead.
    protected override void WndProc(ref Message m)
    {
        const int WmSysCommand = 0x0112, ScMinimize = 0xF020;
        if (m.Msg == WmSysCommand && (m.WParam.ToInt64() & 0xFFF0) == ScMinimize && Owner is { } start)
        {
            start.WindowState = FormWindowState.Minimized;
            return;
        }

        base.WndProc(ref m);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyData == (Keys.Control | Keys.O))
        {
            e.Handled = true;
            PickFile();
        }
        else if (e.KeyData == (Keys.Control | Keys.S) && _work != null)
        {
            e.Handled = true;
            _work.Save();
        }
    }
}
