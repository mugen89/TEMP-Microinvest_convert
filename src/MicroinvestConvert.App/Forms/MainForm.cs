using MicroinvestConvert.App.Ui;
using MicroinvestConvert.App.Updates;
using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Source;

namespace MicroinvestConvert.App.Forms;

/// <summary>
/// The start window: one card per module, the last files, settings and updates. Every module
/// opens in a window of its own, which belongs to this one – so the program has one button on
/// the taskbar, and minimising or closing this window takes the others with it.
/// </summary>
internal sealed class MainForm : AppForm, IHub
{
    private static readonly (OperationKind Kind, string Source, string Rule)[] Modules =
    [
        (OperationKind.Purchase, "Експорт „Покупки“ от AI Accounting",
            "Вид сделка 2, 3 или 4 – от данъчния кредит и изчисления ДДС."),
        (OperationKind.Sale, "Експорт „Продажби“ от AI Accounting",
            "Вид сделка и VIES – от колоната на дневника."),
        (OperationKind.Foreign, "Експорт „Чуждестранни“ от AI Accounting",
            "Фактури от чужбина в евро, вид сделка 1. Протоколът се въвежда в Делта Pro."),
    ];

    private readonly UpdateService _updates;
    private readonly Dictionary<OperationKind, ModuleForm> _windows = [];
    private readonly Dictionary<OperationKind, ModuleCard> _cards = [];
    private readonly MessageBar _news = new(Color.FromArgb(255, 244, 214), Theme.WarningInk);
    private readonly Label _recentTitle = Make.Label("Последни файлове", font: Theme.Heading);
    private readonly FlowLayoutPanel _recent = new()
    {
        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown,
        WrapContents = false, Margin = Padding.Empty,
    };

    public AppState App { get; }

    public MainForm(AppState app, UpdateService updates)
    {
        App = app;
        _updates = updates;

        Text = $"{AppInfo.Name} {AppInfo.VersionText}";
        ClientSize = new Size(1080, 640);
        MinimumSize = new Size(960, 540);
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        KeyPreview = true;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(_news, 0, 1);
        root.Controls.Add(BuildContent(), 0, 2);
        Controls.Add(root);

        ExcelFiles.AcceptDrops(this, files => OpenFiles(files, null, this));
        RefreshRecent();
        EndLayout();
    }

    // ----- content -----

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
        settings.Click += (_, _) => ShowSettings(this, OnlyClient());
        var deals = new FlatButton("Видове сделки", ButtonLook.Quiet);
        deals.Click += (_, _) => ShowDealTypes(this);
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

    private Control BuildContent()
    {
        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = Modules.Length, RowCount = 1, Margin = Padding.Empty };
        cards.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        for (int i = 0; i < Modules.Length; i++)
        {
            var (kind, source, rule) = Modules[i];
            var card = new ModuleCard(kind, source, rule);
            card.OpenRequested += () => ShowModule(kind);
            card.PickRequested += () => { if (ExcelFiles.Pick(this, source) is { } path) OpenFiles([path], kind, this); };
            card.FilesDropped += files => OpenFiles(files, kind, this);
            _cards[kind] = card;

            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / Modules.Length));
            cards.Controls.Add(card, i, 0);
        }

        var hint = Make.Label("Може да пуснете файла и където и да е върху прозореца – видът му се разпознава сам.", Theme.Muted);
        hint.Margin = new Padding(6, 4, 3, 10);
        _recentTitle.Margin = new Padding(6, 4, 3, 4);

        var page = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty, Padding = new Padding(14, 12, 14, 10),
        };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.Controls.Add(cards, 0, 0);
        page.Controls.Add(hint, 0, 1);
        page.Controls.Add(_recentTitle, 0, 2);
        page.Controls.Add(_recent, 0, 3);
        return page;
    }

    private void RefreshRecent()
    {
        _recent.SuspendLayout();
        foreach (Control old in _recent.Controls.Cast<Control>().ToList()) old.Dispose();

        var existing = App.Settings.RecentFiles.Where(File.Exists).Take(5).ToList();
        foreach (string path in existing)
        {
            string name = Path.GetFileName(path);
            var link = new LinkLabel
            {
                Text = name + "   " + Path.GetDirectoryName(path),
                AutoSize = true, MaximumSize = new Size(LogicalToDeviceUnits(980), 0), AutoEllipsis = true,
                Margin = new Padding(8, 2, 3, 2), Font = Theme.Base, LinkArea = new LinkArea(0, name.Length),
                LinkColor = Theme.EditedInk, ActiveLinkColor = Theme.EditedInk, LinkBehavior = LinkBehavior.HoverUnderline,
                ForeColor = Theme.Muted, UseMnemonic = false,
            };
            link.LinkClicked += (_, _) => OpenFiles([path], null, this);
            _recent.Controls.Add(link);
        }

        _recentTitle.Visible = existing.Count > 0;
        _recent.ResumeLayout();
    }

    // ----- modules -----

    public void ShowModule(OperationKind kind)
    {
        var window = WindowOf(kind);
        if (!window.Visible) window.Show(this);
        window.Activate();
    }

    private ModuleForm WindowOf(OperationKind kind)
    {
        if (_windows.TryGetValue(kind, out var open)) return open;

        var window = new ModuleForm(this, kind, _windows.Count);
        window.FormClosed += (_, _) =>
        {
            _windows.Remove(kind);
            ModuleChanged();
        };
        _windows[kind] = window;
        return window;
    }

    public void ShowStart()
    {
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    public void ModuleChanged()
    {
        if (IsDisposed) return;
        foreach (var (kind, card) in _cards) card.ShowState(_windows.GetValueOrDefault(kind));
    }

    /// <summary>
    /// Opens files, each in the window of its kind. <paramref name="expected"/> is the module the
    /// user pointed at; another kind is opened after a question.
    /// </summary>
    public void OpenFiles(IReadOnlyList<string> paths, OperationKind? expected, Form asker)
    {
        var opened = new HashSet<OperationKind>();
        var problems = new List<string>();

        foreach (string path in paths)
        {
            string name = Path.GetFileName(path);
            OperationBatch batch;
            Cursor.Current = Cursors.WaitCursor;
            try
            {
                batch = AiReader.Read(path);
            }
            catch (ConvertException ex)
            {
                problems.Add(paths.Count > 1 ? $"{name}: {ex.Message}" : ex.Message);
                continue;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }

            string module = batch.Kind.Title();
            if (paths.Count == 1 && expected is { } wanted && wanted != batch.Kind &&
                !Dialogs.Ask(asker, $"Това е файл за „{module}“",
                    $"Избрахте „{wanted.Title()}“, а файлът е „{batch.LayoutName}“. Да го отворя ли в „{module}“?", $"Отвори в „{module}“", "Отказ"))
                return;

            if (!opened.Add(batch.Kind)) problems.Add($"{name}: в „{module}“ вече е отворен първият от пуснатите файлове.");
            else Open(batch, path);
        }

        if (problems.Count > 0)
            Dialogs.Warning(asker, paths.Count > 1 ? "Не всички файлове са отворени" : "Файлът не може да бъде обработен",
                string.Join("\n\n", problems));
    }

    private void Open(OperationBatch batch, string path)
    {
        var window = WindowOf(batch.Kind);
        if (!window.LeaveWork()) return;

        App.Settings.AddRecent(path);
        App.Save();
        RefreshRecent();

        window.Show(batch);
        ShowModule(batch.Kind);
    }

    // ----- settings, deal types, updates -----

    public void ShowSettings(Form asker, OperationBatch? batch)
    {
        using var form = new SettingsForm(App, batch);
        if (form.ShowDialog(asker) == DialogResult.OK) App.RaiseSettingsChanged();
    }

    // The exceptions of a client are there when it is clear which client.
    private OperationBatch? OnlyClient()
    {
        var open = _windows.Values.Select(w => w.Batch).OfType<OperationBatch>().ToList();
        return open.Select(b => b.ClientEik).Distinct().Count() == 1 ? open[0] : null;
    }

    private void ShowDealTypes(Form asker)
    {
        using var form = new DealTypesForm();
        form.ShowDialog(asker);
    }

    private void ShowUpdates()
    {
        if (_windows.Values.Any(w => w.HasUnsavedEdits) &&
            !Dialogs.Ask(this, "Има незаписани корекции", "При обновяване програмата се рестартира и корекциите ще се загубят.",
                "Продължи", "Отказ", warning: true))
            return;

        using var form = new UpdateForm(App, _updates);
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

        if (App.LoadProblem.Length > 0)
            Dialogs.Warning(this, "Настройките не можаха да бъдат прочетени",
                "Програмата започва със стандартните. Повреденият файл е запазен с разширение .bad.\n\n" + App.LoadProblem);

        if (!App.Settings.CheckForUpdates) return;
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
        if (e.KeyData != (Keys.Control | Keys.O)) return;
        e.Handled = true;
        if (ExcelFiles.Pick(this) is { } path) OpenFiles([path], null, this);
    }
}
