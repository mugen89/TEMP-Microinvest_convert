using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using MicroinvestConvert.App;
using MicroinvestConvert.App.Forms;
using MicroinvestConvert.App.Forms.Checks;
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

    // ----- „Проверки" -----

    [Fact]
    public void The_vat_page_splits_accepts_and_gives_back() => OnScreen(Purchases(), (view, batch) =>
    {
        Assert.Equal(1, VatPage.OpenQuestions(batch));
        Assert.Equal("ДДС (1)", Field<Button>(view, "_vatCheck").Text);
        var page = OpenPage<VatPage>(view, "ДДС (1)");
        var grid = Field<Grid<VatGroup>>(page, "_grid");
        Assert.Equal("0000000002", Assert.Single(grid.Items).Document.Number);
        Assert.False(view.HasUnsavedEdits);

        Click(page, "Раздели: със и без ДДС");
        Assert.Equal(("450;90;30", "2;2;3"), (batch.Documents[1].Amounts(), batch.Documents[1].Deals()));
        Assert.Equal(0, VatPage.OpenQuestions(batch));
        Assert.True(view.HasUnsavedEdits);
        Assert.Equal("ДДС", Field<Button>(view, "_vatCheck").Text);   // the other window knows

        Click(page, "Върни от файла");
        Assert.Equal("4;4", batch.Documents[1].Deals());
        Click(page, "Приеми – вид 2");
        Assert.Equal(("480;90", "2;2"), (batch.Documents[1].Amounts(), batch.Documents[1].Deals()));
        Click(page, "Върни от файла");

        Field<NumericUpDown>(page, "_perDocument").Value = 6.00m;   // 96 − 90 = 6
        Pump();
        Assert.Equal("2;2", batch.Documents[1].Deals());
        Assert.Equal(6.00m, new AppState(_settings).Settings.General.TolerancePerDocument);
    });

    [Fact]
    public void The_accounts_page_remembers_a_mapping_for_the_client() => OnScreen(Purchases(), (view, batch) =>
    {
        var page = OpenPage<AccountsPage>(view, "Сметки");
        var grid = (DataGridView)Field<Control>(page, "_grid");
        Assert.False(ButtonOf(page, "Запомни").Enabled);
        Type(grid, 4, RowOf(grid, 0, "401"), "401/1");
        Assert.Equal("401", batch.Documents[0].CounterAccountOut);   // not before the button
        Click(page, "Запомни");

        Assert.Equal("401/1", batch.Documents[0].CounterAccountOut);
        var saved = new AppState(_settings);
        Assert.Equal("401/1", saved.Clients.Find(batch.ClientEik, "")!.Accounts["401"]);
        Assert.Empty(saved.Settings.General.Accounts);
    });

    [Fact]
    public void The_accounts_page_can_map_for_everybody_and_back() => OnScreen(Purchases(), (view, batch) =>
    {
        var page = OpenPage<AccountsPage>(view, "Сметки");
        var grid = (DataGridView)Field<Control>(page, "_grid");
        Field<RadioButton>(page, "_everybody").Checked = true;
        Type(grid, 4, RowOf(grid, 0, "601/5"), "609/9");
        Click(page, "Запомни");
        Assert.Equal("609/9", new AppState(_settings).Settings.General.Accounts["601/5"]);
        Assert.StartsWith("609/9", batch.Documents[0].Accounts());

        Type(grid, 4, RowOf(grid, 0, "601/5"), "");
        Click(page, "Запомни");
        Assert.Empty(new AppState(_settings).Settings.General.Accounts);
        Assert.StartsWith("601/5", batch.Documents[0].Accounts());
    });

    [Fact]
    public void What_is_typed_on_the_accounts_page_survives_a_refresh_and_can_be_dropped() => OnScreen(Purchases(), (view, batch) =>
    {
        var page = OpenPage<AccountsPage>(view, "Сметки");
        var grid = (DataGridView)Field<Control>(page, "_grid");
        Type(grid, 4, RowOf(grid, 0, "401"), "401/7");

        Type(Field<Grid<OperationDocument>>(view, "_documents"), 4, 0, "0000000777");   // a correction in the other window
        Assert.Equal("401/7", grid[4, RowOf(grid, 0, "401")].Value);
        Click(page, "Отказ");
        Assert.Equal("401", grid[4, RowOf(grid, 0, "401")].Value);
    });

    [Fact]
    public void Every_check_is_listed_and_the_ones_that_wait_come_first() => OnScreen(Purchases(), (view, batch) =>
    {
        Assert.Equal("Проверки (2)", Field<Button>(view, "_allChecks").Text);
        var page = OpenPage<AllChecksPage>(view, "Проверки (2)");
        var checks = (DataGridView)Field<Control>(page, "_checks");
        Assert.Equal(Core.Checks.CheckCatalog.For(OperationKind.Purchase).Count(), checks.RowCount);
        Assert.Equal("Няма курс към еврото", checks[2, 0].Value);                   // an error
        Assert.Equal("ДДС не отговаря на ставка", checks[2, 1].Value);              // a warning

        Field<CheckBox>(page, "_onlyFound").Checked = true;
        Pump();
        Assert.Equal(["Няма курс към еврото", "ДДС не отговаря на ставка"],
            Enumerable.Range(0, 2).Select(r => (string)checks[2, r].Value!));
        Assert.True(checks.RowCount < 6);
    });

    [Fact]
    public void A_finding_is_answered_in_its_own_field() => OnScreen(Purchases(), (view, batch) =>
    {
        var page = OpenPage<AllChecksPage>(view, "Проверки (2)");
        var findings = (DataGridView)Field<Control>(page, "_findings");
        Choose(page, IssueCode.MissingRate);
        Assert.Equal(1, findings.RowCount);
        int currency = Enumerable.Range(0, findings.ColumnCount).Single(c => findings.Columns[c].HeaderText == "✎ Валута");
        Assert.True(findings.Columns[currency].Visible);
        Assert.False(findings.Columns[currency].ReadOnly);
        Assert.False(findings.Columns.Cast<DataGridViewColumn>().Single(c => c.HeaderText == "Основание").Visible);

        Type(findings, currency, 0, "eur");
        Assert.True(batch.Documents[2].WillExport);
        Assert.True(view.HasUnsavedEdits);
        Assert.Equal(0, findings.RowCount);
        Assert.Equal("Проверки (1)", Field<Button>(view, "_allChecks").Text);
    });

    [Fact]
    public void A_warning_is_ticked_off_and_comes_back_when_it_changes() => OnScreen(Purchases(), (view, batch) =>
    {
        var page = OpenPage<AllChecksPage>(view, "Проверки (2)");
        var findings = (DataGridView)Field<Control>(page, "_findings");
        Choose(page, IssueCode.VatUnresolved);
        Assert.True(batch.Documents[1].HasWarnings);

        findings.SelectAll();
        Click(page, "Отбележи като прегледани");
        Assert.False(batch.Documents[1].HasWarnings);
        Assert.Equal(0, VatPage.OpenQuestions(batch));
        Assert.False(view.HasUnsavedEdits);         // nothing that is written has changed
        Assert.Equal("4;4", batch.Documents[1].Deals());
        Assert.Equal(1, findings.RowCount);         // still listed, ticked

        Click(page, "Върни за преглед");
        Assert.True(batch.Documents[1].HasWarnings);
        Click(page, "Отбележи като прегледани");

        batch.Documents[1].Lines[0].TrySet(LineField.Vat, "91", out _);
        view.Edited([batch.Documents[1]]);
        Assert.True(batch.Documents[1].HasWarnings);   // another amount, another question
    });

    [Fact]
    public void A_check_can_be_switched_off_but_not_one_that_holds_documents_back() => OnScreen(Purchases(), (view, batch) =>
    {
        var page = OpenPage<AllChecksPage>(view, "Проверки (2)");
        var checks = (DataGridView)Field<Control>(page, "_checks");
        Untick(checks, 0, RowOf(checks, 2, "ДДС не отговаря на ставка"));
        Assert.False(batch.Documents[1].Has(IssueCode.VatUnresolved));
        Assert.True(new AppState(_settings).Settings.General.IsOff(IssueCode.VatUnresolved));

        Untick(checks, 0, RowOf(checks, 2, "ДДС не отговаря на ставка"), value: true);
        Assert.True(batch.Documents[1].Has(IssueCode.VatUnresolved));
        Assert.False(new AppState(_settings).Settings.General.IsOff(IssueCode.VatUnresolved));

        var general = new Core.Settings.ConverterSettings();
        general.SetOff(IssueCode.MissingRate, true);
        Assert.False(general.IsOff(IssueCode.MissingRate));
    });

    [Fact]
    public void Deal_types_are_set_for_everything_a_check_found() => OnScreen(
        new TestSheet(Layout.Purchases)
            .Purchase(1d, "1", 100, 0, ("AC", "-"), ("AD", "-"), ("AG", "ДА"), ("J", "DE123456789"))
            .Purchase(2d, "2", 50, 0, ("AC", "-"), ("AD", "-"), ("AG", "ДА"), ("J", "EL123456789"))
            .Purchase(3d, "3", 70, 0, ("AC", "-"), ("AD", "-"), ("AG", "ДА"))
            .Read(), (view, batch) =>
    {
        Assert.Equal(["3", "3", "3"], batch.Documents.Select(d => d.Deals()));
        var page = OpenPage<AllChecksPage>(view, "Проверки (2)");
        Choose(page, IssueCode.ForeignNoDiary);
        var findings = (DataGridView)Field<Control>(page, "_findings");
        Assert.Equal(2, findings.RowCount);

        findings.SelectAll();
        Field<ComboBox>(page, "_deal").SelectedItem = "1 - Вътрешен оборот";
        Click(page, "Задай вид");
        Assert.Equal(["1", "1", "3"], batch.Documents.Select(d => d.Deals()));
        Assert.Equal("Проверки", Field<Button>(view, "_allChecks").Text);
    });

    [Fact]
    public void The_deal_page_shows_the_totals_and_changes_the_type() => OnScreen(Purchases(), (view, batch) =>
    {
        OpenPage<AllChecksPage>(view, "Проверки (2)");
        var page = Page<DealsPage>(view);
        var totals = (DataGridView)Field<Control>(page, "_totals");
        Assert.Equal(["2 - Получени доставки, ВОП с право на ПДК", "4 - Получени доставки, ВОП с право на ЧДК"],
            Enumerable.Range(0, totals.RowCount).Select(r => (string)totals[0, r].Value!));
        Assert.Equal(("100,00", "20,00", "120,00"), ((string)totals[3, 0].Value!, (string)totals[4, 0].Value!, (string)totals[5, 0].Value!));

        totals.CurrentCell = totals[0, 1];
        Pump();
        var parts = (DataGridView)Field<Control>(page, "_parts");
        Choose(parts, ColumnOf(parts, "✎ Вид"), 0, "2");
        Assert.Equal("2;2", batch.Documents[1].Deals());
        Assert.Equal(1, totals.RowCount);
        Assert.True(view.HasUnsavedEdits);
    });

    [Fact]
    public void The_partners_page_lists_each_once_and_corrects_all_of_its_documents() => OnScreen(
        new TestSheet(Layout.Purchases)
            .Purchase(1d, "1", 100, 20)
            .Purchase(2d, "2", 100, 20)
            .Purchase(3d, "3", 100, 20, ("I", "Друго Име ООД"))
            .Purchase(4d, "4", 100, 20, ("J", "12345678901"))
            .Read(), (view, batch) =>
    {
        OpenPage<AllChecksPage>(view, "Проверки (1)");
        var page = Page<PartnersPage>(view);
        var grid = (DataGridView)Field<Control>(page, "_grid");
        Assert.Equal(3, grid.RowCount);
        string Note(string name, string eik) => (string)grid[6, Enumerable.Range(0, grid.RowCount)
            .Single(r => Equals(grid[0, r].Value, name) && Equals(grid[1, r].Value, eik))].Value!;

        Assert.Contains("същият номер и с име „Друго Име ООД“", Note("Тест Партньор ООД", SupplierEik));
        Assert.Contains("същият номер и с име „Тест Партньор ООД“", Note("Друго Име ООД", SupplierEik));
        Assert.Contains("номер с 11 цифри", Note("Тест Партньор ООД", "12345678901"));
        Assert.Contains($"същото име и с номер {SupplierEik}", Note("Тест Партньор ООД", "12345678901"));

        int row = Enumerable.Range(0, grid.RowCount).Single(r => Equals(grid[0, r].Value, "Тест Партньор ООД") && Equals(grid[1, r].Value, SupplierEik));
        Type(grid, 0, row, "Тест Партньор ЕООД");
        Assert.Equal(["Тест Партньор ЕООД", "Тест Партньор ЕООД", "Друго Име ООД", "Тест Партньор ООД"], batch.Documents.Select(d => d.PartnerName));
    });

    [Fact]
    public void A_double_click_shows_the_document_whatever_the_filter_hides() => OnScreen(Purchases(), (view, batch) =>
    {
        Field<TextBox>(view, "_search").Text = "няма такъв";
        Pump();
        Assert.Empty(Field<Grid<OperationDocument>>(view, "_documents").Items);
        view.Reveal(batch.Documents[2]);
        Pump();
        Assert.Same(batch.Documents[2], Field<Grid<OperationDocument>>(view, "_documents").Current);
    });

    [Fact]
    public void Foreign_invoices_have_no_vat_page() => OnScreen(
        new TestSheet(Layout.Foreign).Invoice(1d, "INV-1", 10, "EUR", 10).Read(), (view, _) =>
    {
        Assert.False(ButtonOf(view, "ДДС").Visible);
        OpenPage<AllChecksPage>(view, "Проверки");
        Assert.Equal(["Всички проверки", "Вид сделка", "Сметки", "Контрагенти"], Pages(view).Select(p => p.Title));
    });

    // ----- settings -----

    [Fact]
    public void The_settings_window_keeps_an_exception_for_one_client() => OnThread(() =>
    {
        var app = NewApp();
        var batch = Purchases();
        using (var form = new SettingsForm(app, batch))
        {
            Show(form);
            Field<TextBox>(form, "_clientPurchaseVat").Text = "453/1/9";
            Assert.Equal("3 – без данъчен кредит (влиза в дневника, к. 9)", Field<ComboBox>(form, "_noDiary").Text);
            Assert.Equal("След всяка сметка (602;453/1;621;453/1)", Field<ComboBox>(form, "_vatLines").Text);
            Field<ComboBox>(form, "_dealFormat").SelectedIndex = 1;
            Field<ComboBox>(form, "_noDiary").SelectedIndex = 2;
            Click(form, "Запази");
        }

        var saved = new AppState(_settings);
        Assert.Equal("453/1/9", saved.OptionsFor(batch).PurchaseVatAccount);
        Assert.Single(saved.Clients.All);
        Assert.Equal(Core.Settings.DealTypeFormat.SingleWhenEqual, saved.Settings.General.DealTypeFormat);
        Assert.Equal(Core.Settings.NoDiaryMode.Skip, saved.Settings.General.NoDiary);

        OnScreenNow(batch, view => Assert.Equal("ДДС сметка: 453/1/9", Field<Label>(view, "_exception").Text));
    });

    [Fact]
    public void Opening_the_settings_and_saving_changes_nothing() => OnThread(() =>
    {
        var app = NewApp();
        app.Settings.General.Accounts["401"] = "401/1";
        var client = new Core.Settings.ClientSettings { Name = "Тестова Фирма ЕООД", Eik = "987654326", Accounts = { ["601/5"] = "601/9" }, AccountCurrencies = { ["4011"] = "USD" } };
        app.Save(client);
        app.Save();
        string before = File.ReadAllText(_settings);

        using var form = new SettingsForm(app, Purchases());
        Show(form);
        Click(form, "Запази");
        Assert.Equal(before, File.ReadAllText(_settings));
        client = Assert.Single(new AppState(_settings).Clients.All);
        Assert.Equal(("601/9", "USD"), (client.Accounts["601/5"], client.AccountCurrencies["4011"]));
    });

    // ----- operating -----

    private static ChecksForm Checks(WorkView view)
    {
        var form = Field<ChecksForm>(view, "_checks");
        form.Location = new Point(-6000, 0);
        return form;
    }

    private static List<CheckPage> Pages(WorkView view) => Field<List<CheckPage>>(Checks(view), "_pages");

    /// <summary>Opens „Проверки" with a button of the toolbar; returns the page it shows.</summary>
    private static T OpenPage<T>(WorkView view, string button) where T : CheckPage
    {
        Click(view, button);
        var page = Pages(view).OfType<T>().Single();
        Assert.True(page.Visible);
        return page;
    }

    private static T Page<T>(WorkView view) where T : CheckPage
    {
        var page = Pages(view).OfType<T>().Single();
        Checks(view).ShowPage(page.Title);
        Pump();
        return page;
    }

    private static void Choose(AllChecksPage page, IssueCode code)
    {
        var checks = (DataGridView)Field<Control>(page, "_checks");
        checks.CurrentCell = checks[2, RowOf(checks, 2, Core.Checks.CheckCatalog.Get(code).Title)];
        Pump();
    }

    private static int RowOf(DataGridView grid, int column, string value) =>
        Enumerable.Range(0, grid.RowCount).Single(r => Equals(grid[column, r].Value, value));

    private static Button ButtonOf(Control parent, string text) => All(parent).OfType<Button>().Single(b => b.Text == text);

    private static void Untick(DataGridView grid, int column, int row, bool value = false)
    {
        grid.CurrentCell = grid[column, row];
        grid.BeginEdit(false);
        ((DataGridViewCheckBoxCell)grid.CurrentCell).EditingCellFormattedValue = value;
        grid.NotifyCurrentCellDirty(true);
        Pump();
    }

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

    private void OnScreenNow(OperationBatch batch, Action<WorkView> act)
    {
        var app = new AppState(_settings);
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
        var button = ButtonOf(parent, text);
        Assert.True(button.Enabled, text);
        button.PerformClick();
        Pump();
    }

    private static IEnumerable<Control> All(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(c => All(c).Prepend(c));

    private static T Field<T>(object owner, string name) where T : class =>
        Assert.IsAssignableFrom<T>(owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner));
}
