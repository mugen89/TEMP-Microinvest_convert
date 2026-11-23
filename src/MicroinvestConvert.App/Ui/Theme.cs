using MicroinvestConvert.Core.Model;

namespace MicroinvestConvert.App.Ui;

/// <summary>Colours of one kind of documents: green for purchases, blue for sales.</summary>
internal sealed record Palette(Color Accent, Color AccentDark, Color Soft, Color SoftBorder, Color Header)
{
    public static readonly Palette Purchases = new(
        Color.FromArgb(38, 138, 92), Color.FromArgb(27, 105, 69), Color.FromArgb(232, 245, 238),
        Color.FromArgb(176, 216, 194), Color.FromArgb(220, 239, 228));

    public static readonly Palette Sales = new(
        Color.FromArgb(28, 111, 184), Color.FromArgb(20, 84, 141), Color.FromArgb(230, 242, 252),
        Color.FromArgb(168, 205, 235), Color.FromArgb(214, 232, 247));

    public static readonly Palette Neutral = new(
        Color.FromArgb(45, 90, 120), Color.FromArgb(33, 68, 92), Color.FromArgb(240, 244, 248),
        Color.FromArgb(205, 215, 225), Color.FromArgb(228, 235, 241));

    public static Palette For(OperationKind kind) => kind == OperationKind.Purchase ? Purchases : Sales;
}

internal static class Theme
{
    public static readonly Color Ink = Color.FromArgb(31, 41, 55);
    public static readonly Color Muted = Color.FromArgb(100, 112, 128);
    public static readonly Color Border = Color.FromArgb(210, 218, 226);
    public static readonly Color Window = Color.FromArgb(246, 248, 250);
    public static readonly Color Surface = Color.White;
    public static readonly Color AltRow = Color.FromArgb(249, 251, 252);

    public static readonly Color ErrorBack = Color.FromArgb(253, 232, 232);
    public static readonly Color ErrorInk = Color.FromArgb(176, 32, 32);
    public static readonly Color WarningBack = Color.FromArgb(255, 246, 221);
    public static readonly Color WarningInk = Color.FromArgb(150, 92, 0);
    public static readonly Color OkInk = Color.FromArgb(28, 122, 74);
    public static readonly Color EditedInk = Color.FromArgb(0, 84, 166);
    public static readonly Color Excluded = Color.FromArgb(150, 158, 168);

    public static readonly Font Base = new("Segoe UI", 9.5f);
    public static readonly Font Bold = new("Segoe UI", 9.5f, FontStyle.Bold);
    public static readonly Font Small = new("Segoe UI", 8.75f);
    public static readonly Font Title = new("Segoe UI Semibold", 13f);
    public static readonly Font Heading = new("Segoe UI Semibold", 11f);
    public static readonly Font Mono = new("Consolas", 9.5f);
}
