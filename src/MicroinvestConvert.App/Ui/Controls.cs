using System.Globalization;
using System.Runtime.InteropServices;

namespace MicroinvestConvert.App.Ui;

/// <summary>
/// A window laid out in code. Sizes are written for 96 DPI and scaled once, when the
/// constructor is done — that is what <see cref="EndLayout"/> triggers.
/// </summary>
internal abstract class AppForm : Form
{
    protected AppForm()
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = Theme.Base;
        Icon = AppInfo.LoadIcon();
        BackColor = Theme.Window;
        ForeColor = Theme.Ink;
        ShowInTaskbar = false;   // one program, one button: only the start window has it
    }

    /// <summary>The last line of every constructor.</summary>
    protected void EndLayout()
    {
        ResumeLayout(false);
        PerformLayout();
    }

    // Minimising a window hides the windows it owns, but not the ones those own: „Проверки"
    // would stay on the desktop without its module. The word is passed on.
    protected override void WndProc(ref Message m)
    {
        const int WmShowWindow = 0x0018, ParentClosing = 1, ParentOpening = 3;
        base.WndProc(ref m);
        if (m.Msg != WmShowWindow) return;

        if (m.LParam == ParentClosing) ShowOwnedPopups(Handle, false);
        else if (m.LParam == ParentOpening) ShowOwnedPopups(Handle, true);
    }

    [DllImport("user32.dll")]
    private static extern bool ShowOwnedPopups(nint window, bool show);

    /// <summary>A window over the main one.</summary>
    protected void AsDialog(int width, int height, int minWidth, int minHeight, bool maximize = true)
    {
        ClientSize = new Size(width, height);
        MinimumSize = new Size(minWidth, minHeight);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = maximize;
    }

    /// <summary>
    /// Over the middle of another window, whole on its screen. For windows that stay open beside
    /// it: CenterParent is for dialogs only.
    /// </summary>
    protected void CenterOver(Form other, int step = 0)
    {
        var area = Screen.FromControl(other).WorkingArea;
        MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width), Math.Min(MinimumSize.Height, area.Height));
        Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
        int x = other.Left + (other.Width - Width) / 2 + step, y = other.Top + (other.Height - Height) / 2 + step;
        Location = new Point(
            Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - Width)),
            Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - Height)));
    }
}

/// <summary>A part of a window that is added after the window is shown, so it scales itself.</summary>
internal abstract class AppView : UserControl
{
    protected AppView()
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = Theme.Base;
        BackColor = Theme.Window;
        ForeColor = Theme.Ink;
        Dock = DockStyle.Fill;
    }

    protected void EndLayout()
    {
        ResumeLayout(false);
        PerformLayout();
    }
}

internal enum ButtonLook
{
    Primary,
    Normal,
    Quiet,
}

/// <summary>A flat button in the colours of the current palette.</summary>
internal sealed class FlatButton : Button
{
    private readonly ButtonLook _look;
    private Palette _palette = Palette.Neutral;

    public FlatButton(string text, ButtonLook look = ButtonLook.Normal)
    {
        _look = look;
        Text = text;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        Font = look == ButtonLook.Primary ? Theme.Bold : Theme.Base;
        Padding = look == ButtonLook.Primary ? new Padding(12, 3, 12, 3) : new Padding(5, 0, 5, 0);
        Margin = look == ButtonLook.Primary ? new Padding(3) : new Padding(3, 2, 3, 2);
        Anchor = AnchorStyles.Left;
        Apply();
    }

    public void Use(Palette palette)
    {
        _palette = palette;
        Apply();
    }

    private void Apply()
    {
        switch (_look)
        {
            case ButtonLook.Primary:
                BackColor = Enabled ? _palette.Accent : Color.FromArgb(200, 206, 212);
                ForeColor = Color.White;
                FlatAppearance.BorderSize = 0;
                FlatAppearance.MouseOverBackColor = _palette.AccentDark;
                FlatAppearance.MouseDownBackColor = _palette.AccentDark;
                break;
            case ButtonLook.Normal:
                BackColor = Theme.Surface;
                ForeColor = Enabled ? Theme.Ink : Theme.Muted;
                FlatAppearance.BorderSize = 1;
                FlatAppearance.BorderColor = _palette.SoftBorder;
                FlatAppearance.MouseOverBackColor = _palette.Soft;
                FlatAppearance.MouseDownBackColor = _palette.Header;
                break;
            default:
                BackColor = Color.Transparent;
                ForeColor = Enabled ? _palette.AccentDark : Theme.Muted;
                FlatAppearance.BorderSize = 0;
                FlatAppearance.MouseOverBackColor = _palette.Header;
                FlatAppearance.MouseDownBackColor = _palette.Header;
                break;
        }
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Apply();
    }
}

/// <summary>A bar across the window: a message and its buttons.</summary>
internal sealed class MessageBar : FlowLayoutPanel
{
    private readonly Label _label;

    public MessageBar(Color back, Color ink)
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Dock = DockStyle.Fill;
        WrapContents = false;
        BackColor = back;
        Padding = new Padding(12, 6, 12, 6);
        Margin = Padding.Empty;
        Visible = false;
        _label = Make.Label("", ink: ink, font: Theme.Bold);
        _label.Margin = new Padding(3, 8, 12, 3);
        Controls.Add(_label);
    }

    public void Show(string message, params Control[] actions)
    {
        _label.Text = message;
        while (Controls.Count > 1) Controls.RemoveAt(1);
        Controls.AddRange(actions);
        Visible = true;
    }
}

/// <summary>Short constructors for the pieces the windows are built of.</summary>
internal static class Make
{
    public static Label Label(string text, Color? ink = null, Font? font = null) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = ink ?? Theme.Ink,
        Font = font ?? Theme.Base,
        Margin = new Padding(3, 7, 3, 3),
        UseMnemonic = false,
    };

    /// <summary>A label that wraps inside the width of its cell.</summary>
    public static Label Text(string text, Color? ink = null, int maxWidth = 640) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(maxWidth, 0),
        ForeColor = ink ?? Theme.Muted,
        Font = Theme.Base,
        Margin = new Padding(3, 3, 3, 6),
        UseMnemonic = false,
    };

    public static TextBox TextBox(int width, string placeholder = "") => new()
    {
        Width = width,
        Font = Theme.Base,
        PlaceholderText = placeholder,
        Margin = new Padding(3, 4, 3, 4),
        BorderStyle = BorderStyle.FixedSingle,
    };

    public static ComboBox Combo(int width, params object[] items)
    {
        var combo = new ComboBox
        {
            Width = width,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = Theme.Base,
            FlatStyle = FlatStyle.System,
            Margin = new Padding(3, 4, 3, 4),
        };
        combo.Items.AddRange(items);
        return combo;
    }

    public static CheckBox Check(string text, bool value = false) => new()
    {
        Text = text,
        Checked = value,
        AutoSize = true,
        Font = Theme.Base,
        Margin = new Padding(3, 6, 3, 3),
        UseMnemonic = false,
    };

    public static NumericUpDown Cents(decimal value, decimal max) => new()
    {
        DecimalPlaces = 2,
        Increment = 0.01m,
        Minimum = 0m,
        Maximum = max,
        Value = Math.Clamp(value, 0m, max),
        Width = 70,
        Font = Theme.Base,
        Margin = new Padding(3, 4, 3, 4),
    };

    /// <summary>Controls on one line.</summary>
    public static FlowLayoutPanel Flow(params Control[] controls)
    {
        var flow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Anchor = AnchorStyles.Left,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        flow.Controls.AddRange(controls);
        return flow;
    }

    /// <summary>A one-column table; every row is as high as its content.</summary>
    public static TableLayoutPanel Stack(params Control[] controls)
    {
        var table = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var control in controls)
        {
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(control);
        }

        return table;
    }

    public static Panel Gap(int width) => new() { Width = width, Height = 1, Margin = Padding.Empty };
}

/// <summary>Amounts and dates as a Bulgarian user reads them.</summary>
internal static class Fmt
{
    public static readonly CultureInfo Bulgarian = CultureInfo.GetCultureInfo("bg-BG");

    public static string Money(decimal value) => value.ToString("#,##0.00", Bulgarian);
    public static string Date(DateTime? value) => value?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "";

    /// <summary>„1 документ", „5 документа".</summary>
    public static string Documents(int count) => count == 1 ? "1 документ" : $"{count} документа";
}

/// <summary>Message windows with Bulgarian buttons, whatever the language of Windows.</summary>
internal static class Dialogs
{
    public static void Info(IWin32Window? owner, string heading, string text = "") =>
        Show(owner, heading, text, TaskDialogIcon.Information);

    public static void Warning(IWin32Window? owner, string heading, string text = "") =>
        Show(owner, heading, text, TaskDialogIcon.Warning);

    public static void Error(IWin32Window? owner, string heading, string text = "") =>
        Show(owner, heading, text, TaskDialogIcon.Error);

    private static void Show(IWin32Window? owner, string heading, string text, TaskDialogIcon icon)
    {
        var page = Page(heading, text, icon);
        page.Buttons.Add(new TaskDialogButton("Добре"));
        ShowPage(owner, page);
    }

    /// <summary>True when the first button is chosen.</summary>
    public static bool Ask(IWin32Window? owner, string heading, string text, string yes, string no, bool warning = false)
    {
        var page = Page(heading, text, warning ? TaskDialogIcon.Warning : TaskDialogIcon.None);
        var accept = new TaskDialogButton(yes);
        var refuse = new TaskDialogButton(no);
        page.Buttons.Add(accept);
        page.Buttons.Add(refuse);
        page.DefaultButton = warning ? refuse : accept;
        return ShowPage(owner, page) == accept;
    }

    private static TaskDialogPage Page(string heading, string text, TaskDialogIcon icon) => new()
    {
        Caption = AppInfo.Name,
        Heading = heading,
        Text = text,
        Icon = icon,
        AllowCancel = true,
    };

    private static TaskDialogButton ShowPage(IWin32Window? owner, TaskDialogPage page) =>
        owner != null ? TaskDialog.ShowDialog(owner, page) : TaskDialog.ShowDialog(page);
}
