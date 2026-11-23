using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using MicroinvestConvert.App;
using MicroinvestConvert.App.Forms;
using MicroinvestConvert.App.Ui;
using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Rules;
using static MicroinvestConvert.Tests.TestSheet;

namespace MicroinvestConvert.Tests;

/// <summary>The windows, operated the way a user does: cells, lists, buttons.</summary>
public sealed class InteractionTests : IDisposable
{
    private readonly string _settings = Path.Combine(Path.GetTempPath(), "mic-ui-" + Guid.NewGuid(), "settings.json");

    public void Dispose()
    {
        string folder = Path.GetDirectoryName(_settings)!;
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }

    private static OperationBatch Purchases() => new TestSheet(Layout.Purchases)
        .Purchase(1d, "0000000001", 100, 20)
        .Purchase(2d, "0000000002", 480, 90)
        .Purchase(3d, "0000000003", 50, 10, ("T", "USD"))
        .Read();

    // ----- the table of documents -----

    [Fact]
    public void A_typed_cell_changes_the_document_and_its_status() => OnScreen(Purchases(), (view, batch) =>
    {
        var grid = Field<Grid<OperationDocument>>(view, "_documents");
        Type(grid, 4, 0, "0000000777");
        Assert.Equal("0000000777", batch.Documents[0].Number);
        Assert.True(view.HasUnsavedEdits);

        Assert.False(batch.Documents[2].WillExport);
        Type(grid, ColumnOf(grid, "Валута"), 2, "EUR");
        Assert.True(batch.Documents[2].WillExport);
        Assert.Equal("За запис: 3 от 3", Field<Label>(view, "_counts").Text);
    });

    [Fact]
    public void A_choice_from_a_list_takes_effect_at_once() => OnScreen(Purchases(), (view, batch) =>
    {
        var grid = Field<Grid<OperationDocument>>(view, "_documents");
        Choose(grid, ColumnOf(grid, "Тип"), 0, "КИ");
        Assert.Equal("КИ", batch.Documents[0].DocType);
        Assert.Equal(-120m, batch.Documents[0].TotalGross);

        Choose(grid, ColumnOf(grid, "Плащане"), 0, "В брой");
        Assert.True(batch.Documents[0].IsCash);
        Assert.True(batch.Documents[0].Has(IssueCode.MissingCashAccount));
        Type(grid, ColumnOf(grid, "Каса"), 0, "501");
        Assert.True(batch.Documents[0].WillExport);
    });

    [Fact]
    public void A_tick_takes_the_document_out_of_the_file() => OnScreen(Purchases(), (view, batch) =>
    {
        var grid = Field<Grid<OperationDocument>>(view, "_documents");
        grid.CurrentCell = grid[0, 0];
        grid.BeginEdit(false);
        ((DataGridViewCheckBoxCell)grid.CurrentCell).EditingCellFormattedValue = false;
        grid.NotifyCurrentCellDirty(true);
        Pump();
        Assert.False(batch.Documents[0].Export);
        Assert.Equal("За запис: 1 от 3  (изключени: 1)", Field<Label>(view, "_counts").Text);
    });

    [Fact]
    public void The_accounting_date_can_be_set_and_given_back_to_the_rule() => OnScreen(Purchases(), (view, batch) =>
    {
        var grid = Field<Grid<OperationDocument>>(view, "_documents");
        int column = ColumnOf(grid, "Сч. дата");
        Type(grid, column, 0, "30.06.2026");
        Assert.Equal(new DateTime(2026, 6, 30), batch.Documents[0].AccountingDate);
        Type(grid, column, 0, "");
        Assert.Equal(new DateTime(2026, 6, 10), batch.Documents[0].AccountingDate);
    });

    [Fact]
    public void One_pasted_value_fills_the_selection_and_a_block_goes_down() => OnScreen(Purchases(), (view, batch) =>
    {
        var grid = Field<Grid<OperationDocument>>(view, "_documents");
        int reason = ColumnOf(grid, "Основание"), note = ColumnOf(grid, "Забележка");

        grid.ClearSelection();
        for (int r = 0; r < 3; r++) grid[reason, r].Selected = true;
        grid.PasteText("общо основание");
        Pump();
        Assert.All(batch.Documents, d => Assert.Equal("общо основание", d.Reason));

        grid.ClearSelection();
        grid[note, 0].Selected = true;
        grid.PasteText("първа\tвтора\r\nтрета\tчетвърта\r\n");
        Pump();
        Assert.Equal(("първа", "втора"), (batch.Documents[0].Note, batch.Documents[0].Note2));
        Assert.Equal(("трета", "четвърта"), (batch.Documents[1].Note, batch.Documents[1].Note2));
    });

    [Fact]
    public void The_strip_sets_one_field_of_all_chosen_documents() => OnScreen(Purchases(), (view, batch) =>
    {
        var grid = Field<Grid<OperationDocument>>(view, "_documents");
        grid.SelectAll();
        Pump();
        Assert.Equal("За избраните (3):", Field<Label>(view, "_selected").Text);

        Bulk(view, "Забележка", "за всички");
        Assert.All(batch.Documents, d => Assert.Equal("за всички", d.Note));

        Bulk(view, "Вид сделка", "3 - Получени доставки по чл.82 и без право на ДК");
        Assert.All(batch.Documents, d => Assert.Equal("3", d.Deals()));
        Bulk(view, "Вид сделка", "автоматично");
        Assert.Equal("4;4", batch.Documents[1].Deals());

        Bulk(view, "Тип документ", "Квит – Квитанция");
        Assert.All(batch.Documents, d => Assert.Equal("Квит", d.DocType));

        Click(view, "Изключи");
        Assert.All(batch.Documents, d => Assert.False(d.Export));
        Click(view, "Включи");
        Assert.All(batch.Documents, d => Assert.True(d.Export));
    });

    [Fact]
    public void The_filter_and_the_search_narrow_the_table() => OnScreen(Purchases(), (view, _) =>
    {
        var grid = Field<Grid<OperationDocument>>(view, "_documents");
        var filter = Field<ComboBox>(view, "_filter");
        filter.SelectedIndex = 1;   // problems
        Assert.Equal(["0000000002", "0000000003"], grid.Items.Select(d => d.Number));
        filter.SelectedIndex = 2;   // errors
        Assert.Equal(["0000000003"], grid.Items.Select(d => d.Number));
        filter.SelectedIndex = 0;
        Field<TextBox>(view, "_search").Text = "0000000002";
        Assert.Equal(["0000000002"], grid.Items.Select(d => d.Number));
    });

    // ----- the rows of a document -----

    [Fact]
    public void The_deal_type_of_a_row_is_chosen_from_its_list() => OnScreen(Purchases(), (view, batch) =>
    {
        var documents = Field<Grid<OperationDocument>>(view, "_documents");
        documents.CurrentCell = documents[4, 1];
        Pump();
        var lines = Field<Grid<LedgerLine>>(view, "_lines");
        int column = ColumnOf(lines, "Вид");

        Choose(lines, column, 0, "2");
        Assert.Equal(2, batch.Documents[1].Lines[0].DealTypeOverride);
        Assert.Equal("2;2", batch.Documents[1].Deals());

        Choose(lines, column, 0, "авто");
        Assert.Null(batch.Documents[1].Lines[0].DealTypeOverride);
        Assert.Equal("4;4", batch.Documents[1].Deals());
    });

    [Fact]
    public void Amounts_and_accounts_of_a_row_can_be_corrected() => OnScreen(Purchases(), (view, batch) =>
    {
        var documents = Field<Grid<OperationDocument>>(view, "_documents");
        documents.CurrentCell = documents[4, 1];
        Pump();
        var lines = Field<Grid<LedgerLine>>(view, "_lines");

        Type(lines, ColumnOf(lines, "Основа"), 0, "450,00");
        Assert.Equal(("450;90", "2;2"), (batch.Documents[1].Amounts(), batch.Documents[1].Deals()));
        Type(lines, ColumnOf(lines, "Сметка"), 0, "602");
        Type(lines, ColumnOf(lines, "Аналит."), 0, "7");
        Assert.Equal("602/7;453/1", batch.Documents[1].Accounts());

        var postings = Field<Control>(view, "_postings");
        Assert.Equal(3, ((DataGridView)postings).RowCount);
    });

    // ----- the other windows -----

    [Fact]
    public void The_vat_window_splits_accepts_and_gives_back() => OnThread(() =>
    {
        var app = NewApp();
        var batch = Purchases();
        void Reprocess() => new DocumentProcessor(app.Settings.OptionsFor(batch.ClientEik)).Process(batch);
        Reprocess();
        Assert.Equal(1, VatCheckForm.OpenQuestions(batch));

        using var form = new VatCheckForm(app, batch, Reprocess);
        Show(form);
        var grid = Field<Grid<VatGroup>>(form, "_grid");
        Assert.Equal("0000000002", Assert.Single(grid.Items).Document.Number);

        Click(form, "Раздели: със и без ДДС");
        Assert.Equal(("450;90;30", "2;2;3"), (batch.Documents[1].Amounts(), batch.Documents[1].Deals()));
        Assert.Equal(0, VatCheckForm.OpenQuestions(batch));
        Assert.True(form.EditsMade);

        Click(form, "Върни от файла");
        Assert.Equal("4;4", batch.Documents[1].Deals());
        Click(form, "Приеми – вид 2");
        Assert.Equal(("480;90", "2;2"), (batch.Documents[1].Amounts(), batch.Documents[1].Deals()));
        Click(form, "Върни от файла");

        Field<NumericUpDown>(form, "_perDocument").Value = 6.00m;   // 96 − 90 = 6
        Pump();
        Assert.Equal("2;2", batch.Documents[1].Deals());
        Assert.Equal(6.00m, new AppState(_settings).Settings.General.TolerancePerDocument);
    });

    [Fact]
    public void The_accounts_window_remembers_a_mapping_for_the_client() => OnThread(() =>
    {
        var app = NewApp();
        var batch = Purchases();
        using var form = new AccountsForm(app, batch);
        Show(form);
        var grid = (DataGridView)Field<Control>(form, "_grid");
        int row = Enumerable.Range(0, grid.RowCount).Single(r => Equals(grid[0, r].Value, "401"));
        Type(grid, 4, row, "401/1");
        Click(form, "Запомни");

        var saved = new AppState(_settings).Settings;
        Assert.Equal("401/1", saved.Clients[batch.ClientEik].Accounts["401"]);
        Assert.Empty(saved.General.Accounts);
        new DocumentProcessor(saved.OptionsFor(batch.ClientEik)).Process(batch);
        Assert.Equal("401/1", batch.Documents[0].CounterAccountOut);
    });

    [Fact]
    public void The_accounts_window_can_map_for_everybody_and_back() => OnThread(() =>
    {
        var app = NewApp();
        var batch = Purchases();
        using (var form = new AccountsForm(app, batch))
        {
            Show(form);
            Field<RadioButton>(form, "_everybody").Checked = true;
            var grid = (DataGridView)Field<Control>(form, "_grid");
            Type(grid, 4, Enumerable.Range(0, grid.RowCount).Single(r => Equals(grid[0, r].Value, "601/5")), "609/9");
            Click(form, "Запомни");
        }

        Assert.Equal("609/9", app.Settings.General.Accounts["601/5"]);

        using (var form = new AccountsForm(app, batch))
        {
            Show(form);
            Field<RadioButton>(form, "_everybody").Checked = true;
            var grid = (DataGridView)Field<Control>(form, "_grid");
            Type(grid, 4, Enumerable.Range(0, grid.RowCount).Single(r => Equals(grid[0, r].Value, "601/5")), "");
            Click(form, "Запомни");
        }

        Assert.Empty(app.Settings.General.Accounts);
    });

    [Fact]
    public void The_settings_window_keeps_an_exception_for_one_client() => OnThread(() =>
    {
        var app = NewApp();
        var batch = Purchases();
        using (var form = new SettingsForm(app, batch))
        {
            Show(form);
            Field<TextBox>(form, "_clientPurchaseVat").Text = "453/1/9";
            Field<ComboBox>(form, "_dealFormat").SelectedIndex = 1;
            Field<ComboBox>(form, "_noDiary").SelectedIndex = 2;
            Click(form, "Запази");
        }

        var saved = new AppState(_settings).Settings;
        Assert.Equal("453/1/9", saved.OptionsFor(batch.ClientEik).PurchaseVatAccount);
        Assert.Equal("453/1", saved.OptionsFor("111111111").PurchaseVatAccount);
        Assert.Equal(Core.Settings.DealTypeFormat.SingleWhenEqual, saved.General.DealTypeFormat);
        Assert.Equal(Core.Settings.NoDiaryMode.Skip, saved.General.NoDiary);

        OnScreenNow(saved, batch, view => Assert.Equal("ДДС сметка: 453/1/9", Field<Label>(view, "_exception").Text));
    });

    [Fact]
    public void Opening_the_settings_and_saving_changes_nothing() => OnThread(() =>
    {
        var app = NewApp();
        app.Settings.General.Accounts["401"] = "401/1";
        app.Settings.ClientFor("987654326", "Тестова Фирма ЕООД")!.Accounts["601/5"] = "601/9";
        app.Save();
        string before = File.ReadAllText(_settings);

        using var form = new SettingsForm(app, Purchases());
        Show(form);
        Click(form, "Запази");
        Assert.Equal(before, File.ReadAllText(_settings));
    });

    // ----- operating -----

    private AppState NewApp()
    {
        var app = new AppState(_settings);
        app.Settings.CheckForUpdates = false;
        return app;
    }

    private void OnScreen(OperationBatch batch, Action<WorkView, OperationBatch> act) => OnThread(() =>
    {
        using var form = new Form { ClientSize = new Size(1300, 800) };
        using var view = new WorkView(NewApp(), batch);
        form.Controls.Add(view);
        Show(form);
        act(view, batch);
    });

    private void OnScreenNow(Core.Settings.SettingsFile settings, OperationBatch batch, Action<WorkView> act)
    {
        var app = new AppState(_settings);
        Assert.Equal(settings.Clients.Count, app.Settings.Clients.Count);
        using var form = new Form { ClientSize = new Size(1300, 800) };
        using var view = new WorkView(app, batch);
        form.Controls.Add(view);
        Show(form);
        act(view);
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

    private static void Show(Form form)
    {
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-6000, 0);
        form.ShowInTaskbar = false;
        form.Show();
        Pump();
    }

    private static void Pump()
    {
        for (int i = 0; i < 5; i++) Application.DoEvents();
    }

    private static int ColumnOf(DataGridView grid, string header) =>
        grid.Columns.Cast<DataGridViewColumn>().Single(c => c.HeaderText == header).Index;

    private static void Type(DataGridView grid, int column, int row, string text)
    {
        grid.CurrentCell = grid[column, row];
        Assert.True(grid.BeginEdit(true));
        grid.EditingControl!.Text = text;
        grid.NotifyCurrentCellDirty(true);
        Assert.True(grid.EndEdit());
        Pump();
    }

    private static void Choose(DataGridView grid, int column, int row, string item)
    {
        grid.CurrentCell = grid[column, row];
        Assert.True(grid.BeginEdit(false));
        var combo = Assert.IsAssignableFrom<ComboBox>(grid.EditingControl);
        Assert.Contains(item, combo.Items.Cast<object>().Select(o => o.ToString()));
        combo.SelectedItem = item;
        Pump();
        grid.EndEdit();
        Pump();
    }

    private static void Bulk(WorkView view, string field, string value)
    {
        Field<ComboBox>(view, "_bulkField").SelectedItem = field;
        var box = Field<ComboBox>(view, "_bulkValue");
        if (box.DropDownStyle == ComboBoxStyle.DropDownList)
        {
            Assert.Contains(value, box.Items.Cast<object>().Select(o => o.ToString()));
            box.SelectedItem = value;
        }
        else
        {
            box.Text = value;
        }

        Click(view, "Задай");
    }

    private static void Click(Control parent, string text)
    {
        var button = All(parent).OfType<Button>().Single(b => b.Text == text);
        Assert.True(button.Enabled, text);
        button.PerformClick();
        Pump();
    }

    private static IEnumerable<Control> All(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(c => All(c).Prepend(c));

    private static T Field<T>(object owner, string name) where T : class =>
        Assert.IsAssignableFrom<T>(owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner));
}
