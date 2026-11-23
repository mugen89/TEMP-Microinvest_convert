using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MicroinvestConvert.App;
using MicroinvestConvert.App.Forms;
using MicroinvestConvert.App.Forms.Checks;
using MicroinvestConvert.App.Ui;
using MicroinvestConvert.App.Updates;
using MicroinvestConvert.Core.Model;
using static MicroinvestConvert.Tests.TestSheet;

namespace MicroinvestConvert.Tests;

/// <summary>The start window and the windows of the modules, as Windows sees them.</summary>
public sealed class WindowTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "mic-win-" + Guid.NewGuid());

    public WindowTests()
    {
        Directory.CreateDirectory(_folder);
        ExcelFiles.Elevated = false;   // whatever the tests run as
    }

    public void Dispose() => Directory.Delete(_folder, true);

    private string PurchaseFile => new TestSheet(Layout.Purchases).Purchase(1d, "0000000001", 100, 20).Purchase(2d, "2", 480, 90).Save(_folder, "покупки.xls");
    private string SaleFile => new TestSheet(Layout.Sales).Sale(1d, "0000000001", 100, 20, ("AB", 100d), ("AC", 20d)).Save(_folder, "продажби.xls");
    private string ForeignFile => new TestSheet(Layout.Foreign).Invoice(1d, "INV-1", 100, "USD", 86).Save(_folder, "чуждестранни.xls");

    // ----- one program, one button on the taskbar -----

    [Fact]
    public void Every_kind_opens_in_its_own_window_and_the_taskbar_has_one_button() => OnScreen(main =>
    {
        main.OpenFiles([PurchaseFile, SaleFile, ForeignFile], null, main);
        Pump();

        var modules = Modules(main);
        Assert.Equal([OperationKind.Purchase, OperationKind.Sale, OperationKind.Foreign], modules.Select(m => m.Kind));
        Assert.All(modules, m => Assert.True(m.Visible && m.Batch != null));

        Click(modules[0], "Проверки (1)");
        Assert.Equal(5, VisibleWindows().Count);   // start, three modules, „Проверки"
        Assert.Equal([main.Handle], VisibleWindows().Where(OnTaskbar));
    });

    [Fact]
    public void Minimising_a_module_minimises_the_program_and_it_comes_back_whole() => OnScreen(main =>
    {
        main.OpenFiles([PurchaseFile], null, main);
        Pump();
        var module = Modules(main).Single();
        Click(module, "Проверки (1)");
        var checks = Open<ChecksForm>().Single();

        SendMessage(module.Handle, 0x0112, 0xF020, 0);   // the „–" button
        Pump();
        Assert.Equal(FormWindowState.Minimized, main.WindowState);
        Assert.Equal(FormWindowState.Normal, module.WindowState);
        Assert.False(IsWindowVisible(module.Handle));
        Assert.False(IsWindowVisible(checks.Handle));

        main.WindowState = FormWindowState.Normal;
        Pump();
        Assert.True(IsWindowVisible(module.Handle));
        Assert.True(IsWindowVisible(checks.Handle));
    });

    [Fact]
    public void Back_to_the_start_keeps_the_file_and_its_corrections() => OnScreen(main =>
    {
        main.OpenFiles([PurchaseFile], null, main);
        Pump();
        var module = Modules(main).Single();
        module.Batch!.Documents[0].TrySet(DocField.Note, "бележка", out _);
        All(module).OfType<WorkView>().Single().Edited([module.Batch.Documents[0]]);

        Click(module, "Проверки (1)");
        Click(module, "← Начало");
        Assert.False(module.Visible);
        Assert.Empty(Open<ChecksForm>());
        Assert.Contains("незаписани корекции", State(main, OperationKind.Purchase));

        Click(main, "Покажи прозореца");
        Assert.True(module.Visible);
        Assert.Same(module, Modules(main).Single());
        Assert.Equal("бележка", module.Batch.Documents[0].Note);
        module.Batch.Documents[0].Revert();
    });

    [Fact]
    public void Closing_a_module_frees_its_card() => OnScreen(main =>
    {
        main.OpenFiles([ForeignFile], null, main);
        Pump();
        Assert.Equal("чуждестранни.xls", State(main, OperationKind.Foreign));
        Modules(main).Single().Close();
        Pump();
        Assert.Empty(Modules(main));
        Assert.Equal("", State(main, OperationKind.Foreign));
    });

    [Fact]
    public void A_module_opens_the_window_of_another_one() => OnScreen(main =>
    {
        main.ShowModule(OperationKind.Purchase);
        Pump();
        var purchases = Modules(main).Single();
        Assert.Null(purchases.Batch);                    // empty: a place to drop a file
        Assert.Single(All(purchases).OfType<DropZone>());

        Click(purchases, "Чуждестранни");
        Assert.Equal([OperationKind.Purchase, OperationKind.Foreign], Modules(main).Select(m => m.Kind));
    });

    [Fact]
    public void Another_file_of_the_module_takes_the_place_of_the_first() => OnScreen(main =>
    {
        main.OpenFiles([PurchaseFile], null, main);
        Pump();
        var module = Modules(main).Single();
        Click(module, "Проверки (1)");

        string other = new TestSheet(Layout.Purchases).Purchase(1d, "0000000099", 10, 2).Save(_folder, "други покупки.xls");
        main.OpenFiles([other], OperationKind.Purchase, module);
        Pump();
        Assert.Same(module, Modules(main).Single());
        Assert.Equal("0000000099", Assert.Single(module.Batch!.Documents).Number);
        Assert.Contains("други покупки.xls", module.Text);
        Assert.Empty(Open<ChecksForm>());   // it was about the other file
        Assert.Equal(other, main.App.Settings.RecentFiles[0]);
    });

    [Fact]
    public void Settings_reach_every_open_window() => OnScreen(main =>
    {
        main.OpenFiles([PurchaseFile, ForeignFile], null, main);
        Pump();
        var purchases = Modules(main).Single(m => m.Kind == OperationKind.Purchase);
        Assert.Equal("100;20", purchases.Batch!.Documents[0].Amounts());
        main.App.Settings.General.PurchaseVatAccount = "453/9";
        main.App.RaiseSettingsChanged();
        Assert.EndsWith("453/9", purchases.Batch.Documents[0].Accounts());
    });

    [Fact]
    public void A_setting_changed_in_one_module_is_in_force_in_the_others() => OnScreen(main =>
    {
        main.OpenFiles([PurchaseFile, SaleFile], null, main);
        Pump();
        var purchases = Modules(main).Single(m => m.Kind == OperationKind.Purchase);
        var sales = Modules(main).Single(m => m.Kind == OperationKind.Sale);

        CheckBox Rule(ModuleForm module) => All(module).OfType<CheckBox>().Single(c => c.Text == "Сч. дата = дата на документа");
        Rule(purchases).Checked = true;
        Pump();
        Assert.True(Rule(sales).Checked);
        Assert.True(main.App.Settings.General.AccountingDateEqualsDocumentDate);

        // The tolerance of „ДДС", shown in both windows.
        Click(purchases, "ДДС (1)");
        Click(sales, "ДДС");
        var pages = Open<ChecksForm>().SelectMany(All).OfType<VatPage>().ToList();
        var boxes = pages.Select(p => All(p).OfType<NumericUpDown>().First()).ToList();
        Assert.Equal(2, boxes.Count);
        boxes[0].Value = 0.33m;
        Pump();
        Assert.Equal(0.33m, boxes[1].Value);
        Assert.Equal(0.33m, main.App.Settings.General.TolerancePerDocument);

        sales.Close();   // its file does not answer any more
        main.App.RaiseSettingsChanged();
    });

    // ----- dropping files -----

    [Fact]
    public void A_file_dropped_on_the_start_window_goes_to_its_module() => OnScreen(main =>
    {
        Drop(main, SaleFile);
        Assert.Equal(OperationKind.Sale, Modules(main).Single().Kind);
    });

    [Fact]
    public void A_file_dropped_on_a_card_opens_its_module() => OnScreen(main =>
    {
        var card = All(main).OfType<ModuleCard>().ElementAt(2);
        Drop(All(card).OfType<DropZone>().Single(), ForeignFile);
        Assert.Equal(OperationKind.Foreign, Modules(main).Single().Kind);
    });

    [Fact]
    public void A_file_dropped_on_an_empty_module_or_over_an_open_one_is_opened() => OnScreen(main =>
    {
        main.ShowModule(OperationKind.Purchase);
        Pump();
        var module = Modules(main).Single();
        Drop(All(module).OfType<DropZone>().Single(), PurchaseFile);
        Assert.Equal(2, module.Batch!.Documents.Count);

        Drop(module, new TestSheet(Layout.Purchases).Purchase(1d, "0000000099", 10, 2).Save(_folder, "още.xls"));
        Assert.Equal("0000000099", Assert.Single(module.Batch!.Documents).Number);
    });

    [Fact]
    public void Several_files_are_dropped_at_once() => OnScreen(main =>
    {
        Drop(main, PurchaseFile, ForeignFile, SaleFile);
        Assert.Equal(3, Modules(main).Count);
    });

    [Theory]
    [InlineData("покупки.xlsx", true)]
    [InlineData("покупки.XLS", true)]
    [InlineData("покупки.xlsm", true)]
    [InlineData("покупки.pdf", false)]
    [InlineData("папка", false)]
    public void Only_excel_files_are_taken(string name, bool taken)
    {
        var data = new DataObject(DataFormats.FileDrop, new[] { Path.Combine(_folder, name) });
        Assert.Equal(taken ? 1 : 0, ExcelFiles.Of(data).Count);
    }

    [Fact]
    public void Text_dragged_over_the_window_is_not_a_file()
    {
        Assert.Empty(ExcelFiles.Of(new DataObject("нещо.xlsx")));
        Assert.Empty(ExcelFiles.Of(null));
    }

    /// <summary>
    /// Windows gives a drag to the window under the mouse or to the nearest one above it that takes
    /// drops: every part of the windows has to have such a window.
    /// </summary>
    [Fact]
    public void Every_part_of_the_windows_takes_drops_from_windows_itself() => OnScreen(main =>
    {
        main.OpenFiles([PurchaseFile], null, main);
        main.ShowModule(OperationKind.Sale);
        Pump();

        var controls = new Form[] { main }.Concat(Modules(main)).SelectMany(f => All(f).Prepend(f)).Where(c => c.IsHandleCreated && c.Visible).ToList();
        Assert.True(controls.Count > 100, controls.Count.ToString());
        Assert.All(controls, c =>
        {
            for (Control? at = c; at != null; at = at.Parent)
                if (GetProp(at.Handle, "OleDropTargetInterface") != 0) return;
            Assert.Fail(c.GetType().Name + " " + c.Text);
        });
    });

    /// <summary>Started as administrator: Explorer drops the older way, with a message.</summary>
    [Fact]
    public void A_program_started_as_administrator_takes_files_too()
    {
        ExcelFiles.Elevated = true;
        OnScreen(main =>
        {
            SendMessage(main.Handle, 0x0233, Files(ForeignFile, SaleFile, Path.Combine(_folder, "друго.pdf")), 0);
            Pump();
            Assert.Equal([OperationKind.Sale, OperationKind.Foreign], Modules(main).Select(m => m.Kind).Order());

            var sales = Modules(main).Single(m => m.Kind == OperationKind.Sale);
            string other = new TestSheet(Layout.Sales).Sale(1d, "0000000099", 10, 2, ("AB", 10d), ("AC", 2d)).Save(_folder, "други продажби.xls");
            SendMessage(sales.Handle, 0x0233, Files(other), 0);
            Pump();
            Assert.Equal("0000000099", Assert.Single(sales.Batch!.Documents).Number);
        });
    }

    // What WM_DROPFILES carries: a header of 20 bytes, the paths, each with its end, and one more.
    private static nint Files(params string[] paths)
    {
        byte[] names = System.Text.Encoding.Unicode.GetBytes(string.Join('\0', paths) + "\0\0");
        nint memory = Marshal.AllocHGlobal(20 + names.Length);
        Marshal.Copy(new byte[20], 0, memory, 20);
        Marshal.WriteInt32(memory, 0, 20);   // where the names start
        Marshal.WriteInt32(memory, 16, 1);   // they are Unicode
        Marshal.Copy(names, 0, memory + 20, names.Length);
        return memory;                       // freed by the one who takes it
    }

    // ----- operating -----

    private void OnScreen(Action<MainForm> act) => OnThread(() =>
    {
        var app = new AppState(Path.Combine(_folder, "settings", "settings.json"));
        app.Settings.CheckForUpdates = false;
        using var updates = new UpdateService(AppInfo.Version);
        using var main = new MainForm(app, updates);
        main.StartPosition = FormStartPosition.Manual;
        main.Location = new Point(-6000, 0);
        main.Show();
        Pump();
        try
        {
            act(main);
        }
        finally
        {
            // Nobody answers questions here.
            foreach (var module in Modules(main)) module.Dispose();
        }
    });

    // Of this test: others run at the same time, each on its own thread.
    private static IEnumerable<T> Open<T>() where T : Form => Windows().Select(Control.FromHandle).OfType<T>();

    private static List<ModuleForm> Modules(MainForm main) => [.. main.OwnedForms.OfType<ModuleForm>()
        .Concat(Open<ModuleForm>()).Distinct().Where(m => !m.IsDisposed)];

    private static string State(MainForm main, OperationKind kind)
    {
        var card = All(main).OfType<ModuleCard>().ElementAt(OperationKinds.All.ToList().IndexOf(kind));
        return ((Label)typeof(ModuleCard).GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(card)!).Text;
    }

    /// <summary>What Windows does when the mouse button is let go over a control.</summary>
    private static void Drop(Control target, params string[] files)
    {
        var e = new DragEventArgs(new DataObject(DataFormats.FileDrop, files), 0, 0, 0, DragDropEffects.Copy, DragDropEffects.None);
        Raise(target, "OnDragEnter", e);
        Assert.Equal(DragDropEffects.Copy, e.Effect);
        Raise(target, "OnDragDrop", e);
        Pump();
    }

    private static void Raise(Control target, string method, DragEventArgs e) =>
        typeof(Control).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, [e]);

    private static void Click(Control parent, string text)
    {
        var button = All(parent).OfType<Button>().Single(b => b.Text == text);
        Assert.True(button.Enabled, text);
        button.PerformClick();
        Pump();
    }

    private static IEnumerable<Control> All(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(c => All(c).Prepend(c));

    private static void Pump()
    {
        for (int i = 0; i < 8; i++) Application.DoEvents();
    }

    private static void OnThread(Action act)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                act();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "прозорецът чака отговор");
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    // ----- Windows -----

    private static List<nint> Windows()
    {
        var windows = new List<nint>();
        EnumThreadWindows(GetCurrentThreadId(), (window, _) =>
        {
            if (Control.FromHandle(window) is Form) windows.Add(window);
            return true;
        }, 0);
        return windows;
    }

    private static List<nint> VisibleWindows() => Windows().Where(IsWindowVisible).ToList();

    // The rule of the taskbar: forced by a style, or a window without an owner that is not a tool.
    private static bool OnTaskbar(nint window)
    {
        const int exStyle = -20;
        const long appWindow = 0x40000, toolWindow = 0x80;
        const uint owner = 4;
        long style = GetWindowLongPtr(window, exStyle);
        return (style & appWindow) != 0 || (GetWindow(window, owner) == 0 && (style & toolWindow) == 0);
    }

    private delegate bool EnumWindow(nint window, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumThreadWindows(uint thread, EnumWindow callback, nint parameter);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern long GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "GetPropW", CharSet = CharSet.Unicode)]
    private static extern nint GetProp(nint window, string name);
}
