using MicroinvestConvert.App.Ui;
using MicroinvestConvert.Core.Model;

namespace MicroinvestConvert.App.Forms;

/// <summary>A module on the start window: what it takes, a place to drop the file, and its window.</summary>
internal sealed class ModuleCard : TableLayoutPanel
{
    private readonly Label _state;
    private readonly FlatButton _window = new("Отвори прозореца", ButtonLook.Quiet);

    /// <summary>The window of the module, empty or as it was left.</summary>
    public event Action? OpenRequested;
    public event Action? PickRequested;
    public event Action<IReadOnlyList<string>>? FilesDropped;

    public ModuleCard(OperationKind kind, string source, string rule)
    {
        var palette = Palette.For(kind);
        Dock = DockStyle.Fill;
        Margin = new Padding(6);
        Padding = new Padding(16, 12, 16, 10);
        BackColor = palette.Soft;
        ColumnCount = 1;
        RowCount = 5;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var heading = Make.Label(kind.Title(), palette.AccentDark, Theme.Title);
        heading.Margin = new Padding(3, 0, 3, 2);
        heading.Cursor = Cursors.Hand;
        heading.Click += (_, _) => OpenRequested?.Invoke();
        var what = Wrapping(source, Theme.Ink);
        var how = Wrapping(rule, Theme.Muted);

        var zone = new DropZone(palette, "Пуснете файла тук", "или", large: false) { Margin = new Padding(3, 8, 3, 8) };
        zone.Dropped += files => FilesDropped?.Invoke(files);
        zone.PickRequested += () => PickRequested?.Invoke();

        _window.Use(palette);
        _window.Click += (_, _) => OpenRequested?.Invoke();
        _state = Make.Label("", Theme.Muted, Theme.Small);
        _state.AutoEllipsis = true;
        _state.AutoSize = false;
        _state.Dock = DockStyle.Fill;
        _state.TextAlign = ContentAlignment.MiddleLeft;

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(_state, 0, 0);
        bottom.Controls.Add(_window, 1, 0);

        Controls.Add(heading, 0, 0);
        Controls.Add(what, 0, 1);
        Controls.Add(how, 0, 2);
        Controls.Add(zone, 0, 3);
        Controls.Add(bottom, 0, 4);
        Paint += (_, e) => ControlPaint.DrawBorder(e.Graphics, ClientRectangle, palette.SoftBorder, ButtonBorderStyle.Solid);
    }

    // As wide as the card; its height follows the text.
    private static Label Wrapping(string text, Color ink)
    {
        var label = Make.Label(text, ink);
        label.Margin = new Padding(3, 2, 3, 2);
        label.Dock = DockStyle.Fill;
        return label;
    }

    public void ShowState(ModuleForm? window)
    {
        bool open = window is { Batch: not null };
        _state.Text = open ? Path.GetFileName(window!.Batch!.SourcePath) + (window.HasUnsavedEdits ? "  · незаписани корекции" : "") : "";
        _window.Text = open ? "Покажи прозореца" : "Отвори прозореца";
    }
}
