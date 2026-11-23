using MicroinvestConvert.App.Ui;
using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Settings;

namespace MicroinvestConvert.App.Forms.Checks;

/// <summary>The opened file, as the window „Проверки" needs it.</summary>
internal interface ICheckHost
{
    AppState App { get; }
    OperationBatch Batch { get; }
    ConverterOptions Options { get; }

    /// <summary>Documents were corrected: they are calculated again and shown everywhere.</summary>
    void Edited(IReadOnlyList<OperationDocument> documents);

    /// <summary>A finding was ticked off; nothing that is written has changed.</summary>
    void Ticked(IReadOnlyList<OperationDocument> documents);

    /// <summary>A setting was changed: everything is calculated again.</summary>
    void SettingsChanged();

    /// <summary>Goes to a document in the window of the module.</summary>
    void Reveal(OperationDocument document);

    /// <summary>After anything was calculated again.</summary>
    event Action? Changed;
}

/// <summary>
/// One page of „Проверки": a table of something to look at and correct. A new page is a class
/// derived from this one and a line in <see cref="CheckPages"/>.
/// </summary>
internal abstract class CheckPage : AppView
{
    protected ICheckHost Host { get; }
    protected Palette Palette { get; }
    protected OperationBatch Batch => Host.Batch;

    protected CheckPage(ICheckHost host)
    {
        Host = host;
        Palette = Palette.For(host.Batch.Kind);
    }

    /// <summary>The name of the tab, also how the toolbar asks for the page.</summary>
    public abstract string Title { get; }

    /// <summary>How many questions wait here; shown on the tab.</summary>
    public virtual int Open => 0;

    public abstract void RefreshRows();

    // ----- pieces the pages are built of -----

    protected FlatButton MakeButton(string text, Action act, string tip = "", ButtonLook look = ButtonLook.Normal)
    {
        var button = new FlatButton(text, look);
        button.Use(Palette);
        button.Click += (_, _) => act();
        if (tip.Length > 0) new ToolTip { AutoPopDelay = 15000 }.SetToolTip(button, tip);
        return button;
    }

    /// <summary>Rows one under the other; the one with the number takes the rest of the height.</summary>
    protected static TableLayoutPanel Rows(int filling, params Control[] controls)
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = controls.Length, Margin = Padding.Empty };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < controls.Length; i++)
        {
            table.RowStyles.Add(i == filling ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
            table.Controls.Add(controls[i], 0, i);
        }

        return table;
    }

    protected static Label Note(string text)
    {
        var note = Make.Text(text, Theme.Muted, 1150);
        note.Margin = new Padding(8, 6, 8, 4);
        return note;
    }
}

/// <summary>The pages of the window, in their order.</summary>
internal static class CheckPages
{
    private static readonly (Func<ICheckHost, CheckPage> Create, Func<OperationKind, bool> Belongs)[] All =
    [
        (h => new AllChecksPage(h), _ => true),
        (h => new VatPage(h), kind => kind != OperationKind.Foreign),   // foreign invoices have no VAT to check
        (h => new DealsPage(h), _ => true),
        (h => new AccountsPage(h), _ => true),
        (h => new PartnersPage(h), _ => true),
    ];

    public static List<CheckPage> Create(ICheckHost host) =>
        All.Where(p => p.Belongs(host.Batch.Kind)).Select(p => p.Create(host)).ToList();
}
