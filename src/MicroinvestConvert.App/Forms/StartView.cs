using MicroinvestConvert.App.Ui;
using MicroinvestConvert.Core.Model;

namespace MicroinvestConvert.App.Forms;

/// <summary>The first screen: what to convert, and the last opened files.</summary>
internal sealed class StartView : AppView
{
    private readonly AppState _app;
    private readonly FlowLayoutPanel _recent = new()
    {
        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown,
        WrapContents = false, Margin = Padding.Empty,
    };

    private readonly Label _recentTitle = Make.Label("Последни файлове", font: Theme.Heading);

    /// <summary>A file, and the card it was chosen from (null for a recent one).</summary>
    public event Action<string, OperationKind?>? FileChosen;

    public StartView(AppState app)
    {
        _app = app;
        AutoScroll = true;

        var cards = Make.Flow(
            Card(OperationKind.Purchase, "Покупки", "Експорт „Покупки“ от AI Accounting",
                "Видът сделка се определя от данъчния кредит и от изчисления ДДС: 2, 3 или 4."),
            Card(OperationKind.Sale, "Продажби", "Експорт „Продажби“ от AI Accounting",
                "Видът сделка и VIES се определят от колоната на дневника: от 7 до 17."));
        var hint = Make.Label("Може и да пуснете файла върху прозореца – видът му се разпознава сам.", Theme.Muted);
        hint.Margin = new Padding(8, 6, 3, 18);
        _recentTitle.Margin = new Padding(8, 6, 3, 4);

        var column = Make.Stack(cards, hint, _recentTitle, _recent);
        column.Dock = DockStyle.None;
        column.Anchor = AnchorStyles.None;

        var centre = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        centre.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        centre.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        centre.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        centre.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
        centre.Controls.Add(column, 0, 1);
        Controls.Add(centre);
        EndLayout();
    }

    private Control Card(OperationKind kind, string title, string source, string rule)
    {
        var palette = Palette.For(kind);

        var heading = Make.Label(title, palette.AccentDark, Theme.Title);
        heading.Margin = new Padding(3, 0, 3, 2);
        var what = Make.Text(source, Theme.Ink, 380);
        var how = Make.Text(rule, Theme.Muted, 380);
        how.Margin = new Padding(3, 0, 3, 12);

        var open = new FlatButton("Избери файл…", ButtonLook.Primary);
        open.Use(palette);
        open.Click += (_, _) => { if (PickFile(FindForm()) is { } path) FileChosen?.Invoke(path, kind); };

        var body = Make.Stack(heading, what, how, open);
        body.BackColor = Color.Transparent;
        body.Dock = DockStyle.Fill;

        var card = new Panel
        {
            BackColor = palette.Soft, Margin = new Padding(8), Padding = new Padding(20, 16, 20, 16),
            Size = new Size(440, 190), AllowDrop = true,
        };
        card.Controls.Add(body);
        card.Paint += (_, e) => ControlPaint.DrawBorder(e.Graphics, card.ClientRectangle, palette.SoftBorder, ButtonBorderStyle.Solid);
        card.DragEnter += (_, e) => e.Effect = MainForm.DroppedFile(e) != null ? DragDropEffects.Copy : DragDropEffects.None;
        card.DragDrop += (_, e) =>
        {
            if (MainForm.DroppedFile(e) is { } path) BeginInvoke(() => FileChosen?.Invoke(path, kind));
        };
        return card;
    }

    public void RefreshRecent()
    {
        _recent.SuspendLayout();
        foreach (Control old in _recent.Controls.Cast<Control>().ToList()) old.Dispose();

        var existing = _app.Settings.RecentFiles.Where(File.Exists).ToList();
        foreach (string path in existing)
        {
            var link = new LinkLabel
            {
                Text = Path.GetFileName(path) + "   " + Path.GetDirectoryName(path),
                AutoSize = true, MaximumSize = new Size(900, 0), AutoEllipsis = true,
                Margin = new Padding(10, 3, 3, 3), Font = Theme.Base,
                LinkColor = Theme.EditedInk, ActiveLinkColor = Theme.EditedInk, LinkBehavior = LinkBehavior.HoverUnderline,
                UseMnemonic = false,
            };
            link.LinkArea = new LinkArea(0, Path.GetFileName(path).Length);
            link.DisabledLinkColor = Theme.Muted;
            link.ForeColor = Theme.Muted;
            link.LinkClicked += (_, _) => FileChosen?.Invoke(path, null);
            _recent.Controls.Add(link);
        }

        _recentTitle.Visible = existing.Count > 0;
        _recent.ResumeLayout();
    }

    public static string? PickFile(IWin32Window? owner)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Експорт от AI Accounting",
            Filter = "Excel (*.xlsx; *.xls; *.xlsm)|*.xlsx;*.xls;*.xlsm|Всички файлове|*.*",
        };
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
    }
}
