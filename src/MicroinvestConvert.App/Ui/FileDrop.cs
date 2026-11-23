using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;

namespace MicroinvestConvert.App.Ui;

/// <summary>Excel files: chosen in a window, or dropped with the mouse.</summary>
internal static class ExcelFiles
{
    private static readonly string[] Extensions = [".xlsx", ".xls", ".xlsm"];

    /// <summary>Started as administrator. Set by the tests, which run both ways.</summary>
    internal static bool Elevated { get; set; } = Environment.IsPrivilegedProcess;

    public static bool IsExcel(string path) => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>The Excel files of a drag; none when it carries something else.</summary>
    public static IReadOnlyList<string> Of(IDataObject? data) =>
        data?.GetData(DataFormats.FileDrop) is string[] paths ? paths.Where(IsExcel).ToList() : [];

    public static string? Pick(IWin32Window? owner, string title = "Експорт от AI Accounting")
    {
        using var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "Excel (*.xlsx; *.xls; *.xlsm)|*.xlsx;*.xls;*.xlsm|Всички файлове|*.*",
        };
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
    }

    /// <summary>
    /// Takes files dropped on a control and on everything inside it.
    /// <paramref name="hover"/> is told when a drag that would be accepted comes and goes.
    /// </summary>
    public static void AcceptDrops(Control control, Action<IReadOnlyList<string>> dropped, Action<bool>? hover = null)
    {
        if (Elevated)
        {
            // Windows does not let Explorer drop on a program started as administrator the usual way.
            if (control is Form form) _ = new ElevatedDrop(form, dropped);
            return;
        }

        control.AllowDrop = true;
        control.DragEnter += (_, e) =>
        {
            bool accepted = Of(e.Data).Count > 0;
            e.Effect = accepted ? DragDropEffects.Copy : DragDropEffects.None;
            hover?.Invoke(accepted);
        };
        control.DragLeave += (_, _) => hover?.Invoke(false);
        control.DragDrop += (_, e) =>
        {
            hover?.Invoke(false);
            var files = Of(e.Data);
            // After the drop has finished: a dialog inside it would freeze the window dragged from.
            if (files.Count > 0) control.BeginInvoke(() => dropped(files));
        };
    }

    /// <summary>The older way of dropping files, which an elevated window may allow.</summary>
    private sealed class ElevatedDrop : NativeWindow
    {
        private const int WmDropFiles = 0x0233, WmCopyData = 0x004A, WmCopyGlobalData = 0x0049;
        private const uint Allow = 1;

        private readonly Form _form;
        private readonly Action<IReadOnlyList<string>> _dropped;

        public ElevatedDrop(Form form, Action<IReadOnlyList<string>> dropped)
        {
            _form = form;
            _dropped = dropped;
            if (form.IsHandleCreated) Attach();
            form.HandleCreated += (_, _) => Attach();
            form.HandleDestroyed += (_, _) => ReleaseHandle();
        }

        private void Attach()
        {
            AssignHandle(_form.Handle);
            foreach (uint message in new uint[] { WmDropFiles, WmCopyData, WmCopyGlobalData })
                ChangeWindowMessageFilterEx(_form.Handle, message, Allow, 0);
            DragAcceptFiles(_form.Handle, true);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg != WmDropFiles)
            {
                base.WndProc(ref m);
                return;
            }

            var paths = new List<string>();
            for (uint i = 0, count = DragQueryFile(m.WParam, uint.MaxValue, null, 0); i < count; i++)
            {
                var path = new StringBuilder((int)DragQueryFile(m.WParam, i, null, 0) + 1);
                DragQueryFile(m.WParam, i, path, path.Capacity);
                if (IsExcel(path.ToString())) paths.Add(path.ToString());
            }

            DragFinish(m.WParam);
            if (paths.Count > 0) _form.BeginInvoke(() => _dropped(paths));
        }

        [DllImport("shell32.dll")]
        private static extern void DragAcceptFiles(nint window, bool accept);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "DragQueryFileW")]
        private static extern uint DragQueryFile(nint drop, uint index, StringBuilder? path, int size);

        [DllImport("shell32.dll")]
        private static extern void DragFinish(nint drop);

        [DllImport("user32.dll")]
        private static extern bool ChangeWindowMessageFilterEx(nint window, uint message, uint action, nint info);
    }
}

/// <summary>A place to drop a file on, with the button for those who would rather choose it.</summary>
internal sealed class DropZone : TableLayoutPanel
{
    private readonly Palette _palette;
    private bool _hover;

    /// <summary>Files dropped here.</summary>
    public event Action<IReadOnlyList<string>>? Dropped;

    /// <summary>The button.</summary>
    public event Action? PickRequested;

    public DropZone(Palette palette, string title, string hint, bool large)
    {
        _palette = palette;
        DoubleBuffered = true;
        ResizeRedraw = true;
        ColumnCount = 1;
        Dock = DockStyle.Fill;
        BackColor = Theme.Surface;
        Padding = new Padding(10);
        Margin = new Padding(3);
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var arrow = new Label
        {
            Text = "⭳", AutoSize = true, Anchor = AnchorStyles.None, ForeColor = palette.Accent, BackColor = Color.Transparent,
            Font = new Font("Segoe UI Symbol", large ? 34f : 19f), Margin = Padding.Empty, UseMnemonic = false,
        };
        var heading = Make.Label(title, palette.AccentDark, large ? Theme.Title : Theme.Heading);
        var text = Make.Label(hint, Theme.Muted);
        var pick = new FlatButton("Избери файл…", ButtonLook.Primary);
        pick.Use(palette);
        pick.Click += (_, _) => PickRequested?.Invoke();

        Control[] parts = [arrow, heading, text, pick];
        RowCount = parts.Length + 2;
        RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        for (int i = 0; i < parts.Length; i++)
        {
            RowStyles.Add(new RowStyle(SizeType.AutoSize));
            parts[i].Anchor = AnchorStyles.None;
            parts[i].BackColor = parts[i] is FlatButton ? parts[i].BackColor : Color.Transparent;
            if (parts[i] is Label) parts[i].Margin = new Padding(3, 0, 3, large ? 8 : 3);
            Controls.Add(parts[i], 0, i + 1);
        }

        RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        ExcelFiles.AcceptDrops(this, files => Dropped?.Invoke(files), Highlight);
    }

    /// <summary>A file is being held over this place.</summary>
    public void Highlight(bool on)
    {
        if (_hover == on) return;
        _hover = on;
        BackColor = on ? _palette.Header : Theme.Surface;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float width = DeviceDpi / 96f * (_hover ? 2f : 1.4f);
        using var pen = new Pen(_hover ? _palette.Accent : _palette.SoftBorder, width) { DashStyle = _hover ? DashStyle.Solid : DashStyle.Dash };
        float edge = width;
        e.Graphics.DrawRectangle(pen, edge, edge, ClientSize.Width - 2 * edge - 1, ClientSize.Height - 2 * edge - 1);
    }
}

/// <summary>Laid over a window while a file is held above it: where it will go.</summary>
internal sealed class DropHint : Label
{
    public DropHint(Palette palette, string text)
    {
        Text = text;
        AutoSize = false;
        Size = new Size(460, 110);
        TextAlign = ContentAlignment.MiddleCenter;
        Font = Theme.Title;
        ForeColor = palette.AccentDark;
        BackColor = palette.Header;
        BorderStyle = BorderStyle.FixedSingle;
        UseMnemonic = false;
        Visible = false;
    }

    public void Show(bool on)
    {
        if (on && Parent is { } window)
        {
            Location = new Point((window.ClientSize.Width - Width) / 2, (window.ClientSize.Height - Height) / 2);
            BringToFront();
        }

        Visible = on;
    }
}
