using MicroinvestConvert.App.Ui;
using MicroinvestConvert.App.Updates;
using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Source;

namespace MicroinvestConvert.App.Forms;

/// <summary>The window: a header, a bar for news, and either the start or the opened file.</summary>
internal sealed class MainForm : AppForm
{
    private readonly AppState _app;
    private readonly UpdateService _updates;
    private readonly Panel _content = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly MessageBar _news = new(Color.FromArgb(255, 244, 214), Theme.WarningInk);
    private readonly StartView _start;
    private WorkView? _work;

    public MainForm(AppState app, UpdateService updates)
    {
        _app = app;
        _updates = updates;

        Text = $"{AppInfo.Name} {AppInfo.VersionText}";
        ClientSize = new Size(1360, 820);
        MinimumSize = new Size(1060, 640);
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;
        KeyPreview = true;

        _start = new StartView(app);
        _start.FileChosen += (path, kind) => OpenFile(path, kind);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(_news, 0, 1);
        root.Controls.Add(_content, 0, 2);
        Controls.Add(root);

        ShowStart();
        EndLayout();
    }

    private Control BuildHeader()
    {
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, Margin = Padding.Empty,
            Padding = new Padding(14, 6, 10, 6), BackColor = Theme.Surface,
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var title = Make.Label(AppInfo.Name, font: Theme.Title);
        title.Margin = new Padding(3, 3, 3, 3);
        var subtitle = Make.Label("AI Accounting → Microinvest Делта Pro", Theme.Muted);
        subtitle.Margin = new Padding(8, 9, 3, 3);

        var settings = new FlatButton("Настройки", ButtonLook.Quiet);
        settings.Click += (_, _) => ShowSettings();
        var deals = new FlatButton("Видове сделки", ButtonLook.Quiet);
        deals.Click += (_, _) => ShowDealTypes();
        var update = new FlatButton("Обновяване", ButtonLook.Quiet);
        update.Click += (_, _) => ShowUpdates();
        foreach (var button in new[] { settings, deals, update }) button.TabStop = false;

        bar.Controls.Add(title, 0, 0);
        bar.Controls.Add(subtitle, 1, 0);
        bar.Controls.Add(Make.Flow(deals, settings, update), 2, 0);
        bar.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawLine(pen, 0, bar.Height - 1, bar.Width, bar.Height - 1);
        };
        return bar;
    }

    // ----- start and work -----

    private void ShowStart()
    {
        _start.RefreshRecent();
        Swap(_start);
        _work?.Dispose();
        _work = null;
        Text = $"{AppInfo.Name} {AppInfo.VersionText}";
    }

    private void Swap(Control view)
    {
        _content.SuspendLayout();
        _content.Controls.Clear();
        _content.Controls.Add(view);
        _content.ResumeLayout();
    }

    /// <summary>Opens a file. <paramref name="expected"/> is the card the user chose, if any.</summary>
    public void OpenFile(string path, OperationKind? expected = null)
    {
        if (!LeaveWork()) return;

        OperationBatch batch;
        Cursor = Cursors.WaitCursor;
        try
        {
            batch = AiReader.Read(path);
        }
        catch (ConvertException ex)
        {
            Cursor = Cursors.Default;
            Dialogs.Warning(this, "Файлът не може да бъде обработен", ex.Message);
            return;
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        if (expected is { } kind && kind != batch.Kind)
        {
            string is_ = batch.Kind == OperationKind.Purchase ? "покупки" : "продажби";
            string wanted = kind == OperationKind.Purchase ? "Покупки" : "Продажби";
            if (!Dialogs.Ask(this, $"Това е експорт на {is_}",
                    $"Избрахте „{wanted}“, но файлът е „{batch.LayoutName}“. Да го отворя ли като {is_}?",
                    $"Отвори като {is_}", "Отказ"))
                return;
        }

        _app.Settings.AddRecent(path);
        _app.Save();

        var work = new WorkView(_app, batch);
        work.BackRequested += () => { if (LeaveWork()) ShowStart(); };
        work.OpenRequested += () => { if (StartView.PickFile(this) is { } other) OpenFile(other); };
        Swap(work);
        _work?.Dispose();
        _work = work;
        Text = $"{Path.GetFileName(path)} – {AppInfo.Name} {AppInfo.VersionText}";
    }

    // Corrections live in memory only, so leaving them behind is asked about.
    private bool LeaveWork()
    {
        if (_work is not { HasUnsavedEdits: true }) return true;
        return Dialogs.Ask(this, "Има незаписани корекции",
            "Корекциите важат само за отворения файл и ще се загубят.", "Продължи без тях", "Остани", warning: true);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && !LeaveWork()) e.Cancel = true;
        base.OnFormClosing(e);
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        e.Effect = DroppedFile(e) != null ? DragDropEffects.Copy : DragDropEffects.None;
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        // After the drop has finished: a dialog inside it would freeze the source window.
        if (DroppedFile(e) is { } path) BeginInvoke(() => OpenFile(path));
    }

    internal static string? DroppedFile(DragEventArgs e) =>
        e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files &&
        Path.GetExtension(files[0]).ToLowerInvariant() is ".xlsx" or ".xls" or ".xlsm"
            ? files[0]
            : null;

    // ----- settings, deal types, updates -----

    private void ShowSettings()
    {
        using var form = new SettingsForm(_app, _work?.Batch);
        if (form.ShowDialog(this) == DialogResult.OK) _work?.SettingsChanged();
    }

    private void ShowDealTypes()
    {
        using var form = new DealTypesForm();
        form.ShowDialog(this);
    }

    private void ShowUpdates()
    {
        if (_work is { HasUnsavedEdits: true } &&
            !Dialogs.Ask(this, "Има незаписани корекции", "При обновяване програмата се рестартира и корекциите ще се загубят.",
                "Продължи", "Отказ", warning: true))
            return;

        using var form = new UpdateForm(_app, _updates);
        if (form.ShowDialog(this) == DialogResult.OK) RestartAfterUpdate();
    }

    private void RestartAfterUpdate()
    {
        _news.Visible = false;
        Program.RestartRequested = true;
        Application.Exit();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);

        if (_app.LoadProblem.Length > 0)
            Dialogs.Warning(this, "Настройките не можаха да бъдат прочетени",
                "Програмата започва със стандартните. Повреденият файл е запазен с разширение .bad.\n\n" + _app.LoadProblem);

        if (!_app.Settings.CheckForUpdates) return;
        try
        {
            var release = await _updates.FindNewerAsync();
            if (release == null || IsDisposed) return;

            var install = new FlatButton("Обнови…", ButtonLook.Primary);
            install.Click += (_, _) => ShowUpdates();
            var later = new FlatButton("По-късно", ButtonLook.Quiet);
            later.Click += (_, _) => _news.Visible = false;
            _news.Show($"Има нова версия {release.Version.ToString(3)}.", install, later);
        }
        catch (Exception ex) when (ex is UpdateException or OperationCanceledException)
        {
            // No internet, no news. The window „Обновяване" says why when asked.
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyData == (Keys.Control | Keys.O))
        {
            e.Handled = true;
            if (StartView.PickFile(this) is { } path) OpenFile(path);
        }
        else if (e.KeyData == (Keys.Control | Keys.S) && _work != null)
        {
            e.Handled = true;
            _work.Save();
        }
    }
}
