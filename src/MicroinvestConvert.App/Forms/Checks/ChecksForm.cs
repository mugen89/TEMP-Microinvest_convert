using MicroinvestConvert.App.Ui;
using MicroinvestConvert.Core.Model;

namespace MicroinvestConvert.App.Forms.Checks;

/// <summary>
/// „Проверки": a window beside the opened file, with a page for everything that can be checked
/// and corrected. Both stay open; what is changed in one shows in the other.
/// </summary>
internal sealed class ChecksForm : AppForm
{
    private readonly ICheckHost _host;
    private readonly List<CheckPage> _pages;
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill, Margin = new Padding(6, 6, 6, 0), Padding = new Point(12, 5) };
    private readonly Label _summary = Make.Label("", font: Theme.Bold);

    public ChecksForm(ICheckHost host)
    {
        _host = host;
        Text = $"Проверки – {host.Batch.Kind.Title()} – {Path.GetFileName(host.Batch.SourcePath)}";
        AsDialog(1380, 720, 1060, 480);
        StartPosition = FormStartPosition.Manual;

        _pages = CheckPages.Create(host);
        foreach (var page in _pages)
        {
            var tab = new TabPage(page.Title) { BackColor = Theme.Window, Padding = new Padding(4) };
            tab.Controls.Add(page);
            _tabs.TabPages.Add(tab);
        }

        _tabs.SelectedIndexChanged += (_, _) => RefreshPage();

        var close = new FlatButton("Затвори", ButtonLook.Primary);
        close.Use(Palette.For(host.Batch.Kind));
        close.Click += (_, _) => Close();
        CancelButton = close;

        _summary.Margin = new Padding(8, 9, 12, 3);
        var bar = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Padding = new Padding(8, 4, 8, 6), BackColor = Theme.Surface };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.Controls.Add(_summary, 0, 0);
        bar.Controls.Add(close, 1, 0);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(_tabs, 0, 0);
        root.Controls.Add(bar, 0, 1);
        Controls.Add(root);

        host.Changed += RefreshPage;
        EndLayout();
    }

    /// <summary>Goes to a page by its name.</summary>
    public void ShowPage(string title)
    {
        int at = _pages.FindIndex(p => p.Title == title);
        if (at < 0) return;
        _tabs.SelectedIndex = at;
        RefreshPage();   // before the window is shown the tabs say nothing
    }

    private void RefreshPage()
    {
        if (IsDisposed) return;

        // Only the visible page works; the others do when they are turned to. Until the window is
        // shown no tab is chosen, and the first one will be.
        _pages[Math.Max(0, _tabs.SelectedIndex)].RefreshRows();

        for (int i = 0; i < _pages.Count; i++)
        {
            int open = _pages[i].Open;
            _tabs.TabPages[i].Text = open > 0 ? $"{_pages[i].Title} ({open})" : _pages[i].Title;
        }

        var waiting = _host.Batch.Documents.Where(d => d is { Export: true, SkippedBySetting: false }).ToList();
        int errors = waiting.Count(d => d.HasErrors), warnings = waiting.Count(d => !d.HasErrors && d.HasWarnings);
        _summary.Text = errors + warnings == 0
            ? "Няма нищо за проверка"
            : (errors > 0 ? $"✖ не се записват: {errors}   " : "") + (warnings > 0 ? $"⚠ за преглед: {warnings}" : "");
        _summary.ForeColor = errors > 0 ? Theme.ErrorInk : warnings > 0 ? Theme.WarningInk : Theme.OkInk;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        RefreshPage();
        if (Owner is { } window) CenterOver(window);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _host.Changed -= RefreshPage;
        base.Dispose(disposing);
    }
}
