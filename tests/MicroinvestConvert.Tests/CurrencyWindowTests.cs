using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Windows.Forms;
using MicroinvestConvert.App;
using MicroinvestConvert.App.Forms;
using MicroinvestConvert.App.Forms.Checks;
using MicroinvestConvert.App.Ui;
using MicroinvestConvert.App.Updates;
using MicroinvestConvert.Core.Exchange;
using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Settings;
using static MicroinvestConvert.Tests.TestSheet;

namespace MicroinvestConvert.Tests;

/// <summary>Exchange rates and clients in the windows; which messages come up, and which do not.</summary>
public sealed class CurrencyWindowTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "mic-cur-" + Guid.NewGuid());
    private readonly FakeSource _source = new();
    private readonly List<string> _messages = [];

    private static readonly DateTime Today = DateTime.Today;
    private static readonly DateTime LongAgo = Today.AddDays(-30);

    public CurrencyWindowTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, true);

    private static string Text(DateTime day) => day.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    /// <summary>A purchase in dollars (№ 1) and one in euro (№ 2), both of that day.</summary>
    private static TestSheet Dollars(DateTime date) => new TestSheet(Layout.Purchases)
        .Purchase(1d, "0000000001", 1200, 0, ("T", "USD"), ("W", "0%"), ("E", Text(date)), ("H", date.ToString("MM.yyyy", CultureInfo.InvariantCulture)))
        .Purchase(2d, "0000000002", 100, 20, ("E", Text(date)), ("H", date.ToString("MM.yyyy", CultureInfo.InvariantCulture)));

    private static string InEuro(decimal amount, DateTime rateDay) =>
        Math.Round(amount / FakeSource.Dollar(rateDay), 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>The working day a table is from, as the source gives it.</summary>
    private static DateTime TableDay(DateTime day)
    {
        while (RateCalendar.IsWeekend(day)) day = day.AddDays(-1);
        return day;
    }

    // ----- the start window -----

    [Fact]
    public void Without_a_starting_date_the_start_window_says_there_are_none_and_nothing_is_asked() => OnScreen((main, app) =>
    {
        Until(() => !app.Rates.IsRunning);
        Assert.Contains("Валутни курсове: няма", Labels(main));
        Assert.Empty(_source.Asked);
        Assert.Empty(_messages);
    });

    [Fact]
    public void With_a_starting_date_the_rates_come_at_the_start_and_their_days_are_shown() => OnScreen((main, app) =>
    {
        Until(() => app.Rates.Ranges.Count > 0 && !app.Rates.IsRunning);
        string shown = Assert.Single(Labels(main), l => l.StartsWith("Валутни курсове: ", StringComparison.Ordinal));
        Assert.Contains(Text(TableDay(LongAgo)), shown);
        Assert.Empty(_messages);
    }, app => app.Rates.From = LongAgo);

    [Theory]
    [InlineData(RateFailure.NoInternet)]
    [InlineData(RateFailure.SiteDown)]
    [InlineData(RateFailure.BadPage)]
    public void A_failure_at_the_start_is_not_told(RateFailure failure) => OnScreen((main, app) =>
    {
        Until(() => _source.Asked.Count > 0 && !app.Rates.IsRunning);
        Assert.Contains("Валутни курсове: няма", Labels(main));
        Assert.Empty(_messages);
    }, app =>
    {
        app.Rates.From = LongAgo;
        _source.Fails = _ => new RateException(failure, "…");
    });

    [Fact]
    public void A_file_opened_with_the_program_does_not_take_the_place_of_the_update() => OnThread(() =>
    {
        using var app = NewApp(a => a.Rates.From = LongAgo.AddDays(5));
        using var updates = new UpdateService(AppInfo.Version);
        using var main = new MainForm(app, updates);
        string file = Dollars(LongAgo).Save(_folder, "покупки.xls");
        main.Shown += (_, _) => main.OpenFiles([file], null, main);   // before the start window asks for its update
        Show(main);
        Until(() => !app.Rates.IsRunning && app.Rates.Store!.Days().Count > 20 && app.Rates.Store.Knows(LongAgo));
        var module = Application.OpenForms.OfType<ModuleForm>().Single(m => m.Owner == main);
        Assert.True(module.Batch!.Documents[0].WillExport);
        Assert.Empty(_messages);
        module.Dispose();
    });

    [Fact]
    public void The_start_window_says_when_the_database_had_to_be_started_anew()
    {
        File.WriteAllText(Path.Combine(_folder, Data.Database.FileName), "нещо друго " + new string('x', 2000));
        OnScreen((main, _) =>
        {
            Assert.Contains(Labels(main), l => l.Contains("беше повредена"));
            Assert.Empty(_messages);   // a bar, not a message
        });
    }

    // ----- „Валутни курсове" -----

    [Fact]
    public void The_starting_date_is_typed_and_cleared() => WithRatesForm((form, app) =>
    {
        var from = Field<TextBox>(form, "_from");
        var hint = Field<Label>(form, "_fromHint");
        var pull = Field<Button>(form, "_pull");
        Assert.Equal(("", "Докато датата е празна, нищо не се изтегля.", false), (from.Text, hint.Text, pull.Enabled));
        Assert.True(Field<CheckBox>(form, "_auto").Checked);

        foreach (string wrong in new[] { "скоро", "31.02.2026", "01.01.1999", Text(Today.AddDays(3)) })
        {
            from.Text = wrong;
            Assert.Equal((Theme.ErrorInk, false), (hint.ForeColor, pull.Enabled));
            Leave(from);
            Assert.Null(app.Rates.From);
        }

        from.Text = "1.1.2026";
        Assert.True(pull.Enabled);
        Leave(from);
        Assert.Equal(("01.01.2026", new DateTime(2026, 1, 1)), (from.Text, app.Rates.From));

        from.Text = "";
        Leave(from);
        Assert.Null(app.Rates.From);
        Assert.Empty(_source.Asked);
        Assert.Empty(_messages);
    });

    [Fact]
    public void The_button_brings_the_rates_without_a_message() => WithRatesForm((form, app) =>
    {
        Field<TextBox>(form, "_from").Text = Text(LongAgo);
        Click(form, "Изтегли сега");
        Until(() => !app.Rates.IsRunning && app.Rates.Ranges.Count > 0);
        Assert.StartsWith("Има курсове за: ", Field<Label>(form, "_days").Text);
        Assert.Contains("нови дни", Field<Label>(form, "_last").Text);
        Assert.Contains("USD – Щатски долар", Field<ComboBox>(form, "_currency").Items.Cast<object>().Select(o => o.ToString()));
        Assert.Empty(_messages);

        var grid = (DataGridView)Field<Control>(form, "_grid");
        Field<TextBox>(form, "_day").Text = Text(LongAgo);
        Leave(Field<TextBox>(form, "_day"));
        Assert.Equal(["DKK", "GBP", "USD"], Enumerable.Range(0, grid.RowCount).Select(r => (string)grid[1, r].Value!));

        var currency = Field<ComboBox>(form, "_currency");
        currency.SelectedItem = "USD – Щатски долар";
        Pump();
        Assert.Equal(app.Rates.Store!.Days().Count, grid.RowCount);
        Assert.All(Enumerable.Range(0, grid.RowCount), r => Assert.Equal("USD", grid[1, r].Value));
    });

    [Fact]
    public void Asked_with_the_button_a_failure_is_told() => WithRatesForm((form, app) =>
    {
        Field<TextBox>(form, "_from").Text = Text(LongAgo);
        _source.Fails = _ => FakeSource.NoInternet;
        Click(form, "Изтегли сега");
        Until(() => _messages.Count == 1);
        Assert.StartsWith("Няма връзка с интернет", _messages[0]);

        _source.Fails = _ => FakeSource.SiteDown;
        Until(() => Field<Button>(form, "_pull").Enabled);
        Click(form, "Изтегли сега");
        Until(() => _messages.Count == 2);
        Assert.StartsWith("Сайтът с курсовете не е намерен", _messages[1]);
        Assert.Contains("сайтът с курсовете не е намерен", Field<Label>(form, "_last").Text);
    });

    [Fact]
    public void The_download_can_be_stopped() => WithRatesForm((form, app) =>
    {
        var gate = new TaskCompletionSource();
        _source.Before = () => gate.Task;
        Field<TextBox>(form, "_from").Text = Text(LongAgo);
        Click(form, "Изтегли сега");
        Until(() => _source.Asked.Count > 0);
        Assert.True(Field<Button>(form, "_stop").Visible);
        Assert.False(Field<Button>(form, "_pull").Enabled);
        Click(form, "Спри");
        gate.SetResult();
        Until(() => !app.Rates.IsRunning);
        Pump();
        Assert.False(Field<Button>(form, "_stop").Visible);
        Assert.Empty(_messages);
    });

    [Fact]
    public void The_days_of_the_open_file_can_be_brought_by_themselves() => WithRatesForm((form, app) =>
    {
        Click(form, "Само за отворения файл (1)");
        Until(() => app.Rates.Store!.Days().Contains(LongAgo));
        Assert.Null(app.Rates.From);
        Assert.Empty(_messages);
    }, needed: [LongAgo]);

    [Fact]
    public void Closing_the_window_keeps_the_date_that_was_typed() => WithRatesForm((form, app) =>
    {
        Field<TextBox>(form, "_from").Text = "02.01.2026";
        form.Close();
        Assert.Equal(new DateTime(2026, 1, 2), app.Rates.From);
    });

    // ----- the opened file -----

    [Fact]
    public void A_file_in_euro_shows_nothing_about_currencies() => WithFile(new TestSheet(Layout.Purchases).Purchase(1d, "1", 100, 20), (view, _, _) =>
    {
        var grid = Documents(view);
        Assert.Equal((false, false, false, true), (Visible(grid, "Дата на курса"), Visible(grid, "Курс"), Visible(grid, "По фактура"), Visible(grid, "Валута")));
        Assert.False(Field<Button>(view, "_rates").Visible);
        Assert.Empty(_source.Asked);
    }, app => app.Rates.From = LongAgo);

    [Fact]
    public void The_rate_a_document_needs_comes_by_itself_when_the_file_is_opened() => WithFile(Dollars(LongAgo), (view, batch, _) =>
    {
        Until(() => batch.Documents[0].WillExport);
        var grid = Documents(view);
        Assert.True(Visible(grid, "Дата на курса"));
        Assert.Equal(InEuro(1200, TableDay(LongAgo)), batch.Documents[0].Amounts());
        Assert.Equal(("1 200,00", Text(LongAgo), Text(TableDay(LongAgo))),
            (Cell(grid, "По фактура", 0).Replace('\u00A0', ' '), Cell(grid, "Дата на курса", 0), Cell(grid, "Курс от", 0)));
        Assert.Equal(("", "", "", ""), (Cell(grid, "По фактура", 1), Cell(grid, "Дата на курса", 1), Cell(grid, "Курс", 1), Cell(grid, "Курс от", 1)));
        Assert.False(view.HasUnsavedEdits);
        Assert.Empty(_messages);
    }, app => app.Rates.From = LongAgo.AddDays(5));   // the day is before the starting date

    [Fact]
    public void Another_date_is_typed_and_its_rate_comes_by_itself() => WithFile(Dollars(LongAgo), (view, batch, _) =>
    {
        Until(() => batch.Documents[0].WillExport);
        var other = LongAgo.AddDays(-9);
        Type(Documents(view), "Дата на курса", 0, Text(other));
        Until(() => batch.Documents[0].WillExport);
        Assert.Equal(InEuro(1200, TableDay(other)), batch.Documents[0].Amounts());
        Assert.True(view.HasUnsavedEdits);

        Type(Documents(view), "Дата на курса", 0, "");
        Assert.Equal(InEuro(1200, TableDay(LongAgo)), batch.Documents[0].Amounts());
        Assert.Equal(Text(LongAgo), Cell(Documents(view), "Дата на курса", 0));
        Assert.Empty(_messages);
    }, app => app.Rates.From = LongAgo);

    [Fact]
    public void Rates_that_arrive_do_not_touch_what_is_chosen_or_being_typed()
    {
        var gate = new TaskCompletionSource();
        WithFile(Dollars(LongAgo), (view, batch, _) =>
        {
            Until(() => _source.Asked.Count > 0);
            var grid = Documents(view);
            grid.ClearSelection();
            grid.CurrentCell = grid[Column(grid, "Забележка").Index, 0];
            grid[Column(grid, "Забележка").Index, 1].Selected = true;
            Assert.True(grid.BeginEdit(true));
            grid.EditingControl!.Text = "недописа";
            grid.NotifyCurrentCellDirty(true);

            gate.SetResult();
            for (int i = 0; i < 30; i++) { Pump(); Thread.Sleep(5); }
            Assert.Equal((true, "недописа", ""), (grid.IsCurrentCellInEditMode, grid.EditingControl!.Text, batch.Documents[0].Note));
            Assert.False(batch.Documents[0].WillExport);   // waits for the end of the typing

            grid.EditingControl.Text = "недописано";
            Assert.True(grid.EndEdit());
            Until(() => batch.Documents[0].WillExport);
            Assert.Equal("недописано", batch.Documents[0].Note);
            Assert.Equal(2, grid.SelectedCells.Cast<DataGridViewCell>().Select(c => c.RowIndex).Distinct().Count());
        }, app =>
        {
            app.Rates.From = LongAgo;
            _source.Before = () => gate.Task;
        });
    }

    [Fact]
    public void Without_internet_the_document_waits_without_a_message() => WithFile(Dollars(LongAgo), (view, batch, _) =>
    {
        Until(() => _source.Asked.Count > 0);
        Pump();
        Assert.False(batch.Documents[0].WillExport);
        var grid = Documents(view);
        Assert.Equal(("", "няма курс", "Курсове (1)"), (Cell(grid, "Общо", 0), Cell(grid, "Курс от", 0), Field<Button>(view, "_rates").Text));
        Assert.Empty(_messages);

        Type(grid, "Курс", 0, "1,25");
        Assert.Equal(("960", "въведен", true), (batch.Documents[0].Amounts(), Cell(grid, "Курс от", 0), batch.Documents[0].WillExport));
        Assert.Equal("Курсове", Field<Button>(view, "_rates").Text);
    }, app =>
    {
        app.Rates.From = LongAgo;
        _source.Fails = _ => FakeSource.NoInternet;
    });

    [Fact]
    public void Without_a_starting_date_opening_a_file_asks_for_nothing() => WithFile(Dollars(LongAgo), (view, batch, _) =>
    {
        Pump();
        Assert.Empty(_source.Asked);
        Assert.True(batch.Documents[0].Has(IssueCode.MissingRate));
        Assert.Equal("Курсове (1)", Field<Button>(view, "_rates").Text);
    });

    [Fact]
    public void The_date_of_the_rate_is_set_for_many_documents_at_once() => WithFile(
        new TestSheet(Layout.Purchases)
            .Purchase(1d, "1", 1000, 200, ("T", "USD"), ("E", Text(LongAgo)), ("H", "-"))
            .Purchase(2d, "2", 500, 0, ("T", "USD"), ("W", "0%"), ("E", Text(LongAgo.AddDays(1))), ("H", "-"))
            .Purchase(3d, "3", 100, 20, ("E", Text(LongAgo)), ("H", "-")), (view, batch, _) =>
    {
        Until(() => batch.Documents.TrueForAll(d => d.IsInEuro));
        Documents(view).SelectAll();
        Pump();
        var other = LongAgo.AddDays(5);
        Field<ComboBox>(view, "_bulkField").SelectedItem = "Дата на курса";
        Field<ComboBox>(view, "_bulkValue").Text = Text(other);
        Click(view, "Задай");
        Until(() => batch.Documents.TrueForAll(d => d.IsInEuro));
        Assert.Equal([other, other, null], batch.Documents.Select(d => d.RateDateOverride));   // not the one in euro
        Assert.Equal(InEuro(500, TableDay(other)), batch.Documents[1].Amounts());

        Field<ComboBox>(view, "_bulkValue").Text = "";   // without a question: it is only the rule again
        Click(view, "Задай");
        Assert.All(batch.Documents, d => Assert.Null(d.RateDateOverride));
        Assert.Empty(_messages);
    }, app => app.Rates.From = LongAgo);

    [Fact]
    public void The_filter_shows_the_documents_in_another_currency() => WithFile(Dollars(LongAgo), (view, _, _) =>
    {
        Field<ComboBox>(view, "_filter").SelectedItem = "Във валута";
        Pump();
        Assert.Equal(1, Documents(view).RowCount);
        Assert.Equal("0000000001", Cell(Documents(view), "Документ №", 0));
    });

    [Fact]
    public void The_columns_come_when_a_document_gets_another_currency_and_go_with_it() => WithFile(
        new TestSheet(Layout.Purchases).Purchase(1d, "1", 195.583m, 0, ("W", "0%")), (view, batch, _) =>
    {
        var grid = Documents(view);
        Type(grid, "Валута", 0, "лв");
        Assert.Equal((true, "фиксиран", "", "100"), (Visible(grid, "Курс"), Cell(grid, "Курс от", 0), Cell(grid, "Дата на курса", 0), batch.Documents[0].Amounts()));
        Type(grid, "Валута", 0, "EUR");
        Assert.Equal((false, "195.58"), (Visible(grid, "Курс"), batch.Documents[0].Amounts()));
    });

    [Fact]
    public void The_rows_of_a_document_show_their_euro_and_a_foreign_one_can_be_corrected() => WithFile(
        new TestSheet(Layout.Foreign).Invoice(1d, "INV-1", 100, "USD", 86.96m).Invoice(2d, "INV-2", 10, "EUR", 10), (view, batch, _) =>
    {
        var lines = (DataGridView)Field<Control>(view, "_lines");
        Assert.Equal(("100,00", "86,96"), (Cell(lines, "По фактура", 0), Cell(lines, "В евро", 0)));
        Assert.Equal("файла", Cell(Documents(view), "Курс от", 0));
        Type(lines, "В евро", 0, "87");
        Assert.Equal("87", batch.Documents[0].Amounts());

        Documents(view).CurrentCell = Documents(view)[4, 1];
        Pump();
        Type(lines, "В евро", 0, "11");   // an invoice in euro has one amount
        Assert.Equal("10", batch.Documents[1].Amounts());
        Assert.Contains("Редът е в евро", Assert.Single(_messages));
    });

    [Fact]
    public void A_missing_rate_is_answered_in_the_checks() => WithFile(Dollars(LongAgo), (view, batch, _) =>
    {
        Click(view, "Проверки (1)");
        var page = All(Field<ChecksForm>(view, "_checks")).OfType<AllChecksPage>().Single();
        var findings = (DataGridView)Field<Control>(page, "_findings");
        Assert.Equal((1, "✎ Дата на курса", "✎ Курс", "✎ Валута"),
            (findings.RowCount, Header(findings, "Дата на курса"), Header(findings, "Курс"), Header(findings, "Валута")));
        Type(findings, "✎ Курс", 0, "1.25");
        Assert.Equal("960", batch.Documents[0].Amounts());
        Assert.Equal("Проверки", Field<Button>(view, "_allChecks").Text);
    });

    private static string FirstAmount(WorkView view)
    {
        Field<TabControl>(view, "_tabs").SelectedIndex = 1;
        Pump();
        return (string)((DataGridView)Field<Control>(view, "_preview"))[0, 0].Value!;
    }

    [Fact]
    public void An_account_is_marked_as_an_ordinary_one_for_the_client_and_back() => WithFile(Dollars(LongAgo), (view, batch, app) =>
    {
        const string WithDollars = @"^[\d.]+ \(1200\|0\.\d+\|0\.\d+\)$";
        Until(() => batch.Documents[0].WillExport);
        Assert.Matches(WithDollars, FirstAmount(view));   // by itself

        Click(view, "Сметки");
        var page = All(Field<ChecksForm>(view, "_checks")).OfType<AccountsPage>().Single();
        var grid = (DataGridView)Field<Control>(page, "_grid");
        int row = Enumerable.Range(0, grid.RowCount).Single(r => Equals(grid[0, r].Value, "401"));
        Assert.Equal("", Cell(grid, "✎ Валута", row));
        Type(grid, "✎ Валута", row, "щатски");
        Assert.Contains("не е код на валута", Assert.Single(_messages));
        Type(grid, "✎ Валута", row, "евро");
        Click(page, "Запомни");
        Assert.Equal("EUR", app.Clients.Find(batch.ClientEik, batch.ClientName)!.AccountCurrencies["401"]);
        Assert.Equal(("", false), (batch.Documents[0].CounterCurrency, batch.Documents[0].Has(IssueCode.AccountCurrency)));
        Assert.Matches(@"^[\d.]+$", FirstAmount(view));

        // Kept in one currency: the document in euro is not in it.
        Type(grid, "✎ Валута", row, "usd");
        Click(page, "Запомни");
        Assert.True(batch.Documents[1].Has(IssueCode.AccountCurrency));
        Assert.Matches(WithDollars, FirstAmount(view));

        // Nothing marked.
        Type(grid, "✎ Валута", row, "");
        Click(page, "Запомни");
        Assert.Empty(app.Clients.Find(batch.ClientEik, batch.ClientName)!.AccountCurrencies);
        Assert.Equal(("USD", false), (batch.Documents[0].CounterCurrency, batch.Documents[1].Has(IssueCode.AccountCurrency)));
    }, app => app.Rates.From = LongAgo);

    [Fact]
    public void How_a_document_will_be_posted_shows_its_currency() => WithFile(Dollars(LongAgo), (view, batch, _) =>
    {
        Until(() => batch.Documents[0].WillExport);
        var postings = (DataGridView)Field<Control>(view, "_postings");
        Assert.True(Visible(postings, "Във валута"));
        foreach (int row in new[] { 0, 1 })   // the expense and the supplier
            Assert.Equal("1200,00 USD", Cell(postings, "Във валута", row).Replace("\u00a0", ""));

        Documents(view).CurrentCell = Documents(view)[4, 1];   // the one in euro
        Pump();
        Assert.False(Visible(postings, "Във валута"));
        Assert.Equal(3, postings.RowCount);
    }, app => app.Rates.From = LongAgo);

    [Fact]
    public void Without_the_currency_the_amounts_are_in_euro_only() => WithFile(Dollars(LongAgo), (view, batch, _) =>
    {
        Until(() => batch.Documents[0].WillExport);
        Assert.Matches(@"^[\d.]+$", FirstAmount(view));
        Assert.False(Visible((DataGridView)Field<Control>(view, "_postings"), "Във валута"));
    }, app =>
    {
        app.Rates.From = LongAgo;
        app.Settings.General.CurrencyAmounts = CurrencyAmounts.EuroOnly;
    });

    // ----- „Фирми" -----

    [Fact]
    public void A_client_is_made_with_a_name_only() => WithClientsForm((form, app) =>
    {
        Field<TextBox>(form, "_name").Text = "  Нова   Фирма ";
        Click(form, "Добави");
        Assert.Equal(("Нова Фирма", ""), (Assert.Single(app.Clients.All).Name, app.Clients.All[0].Eik));
        Assert.Equal("", Field<TextBox>(form, "_name").Text);

        Field<TextBox>(form, "_name").Text = "нова фирма";
        Click(form, "Добави");
        Assert.Contains("Вече има фирма", Assert.Single(_messages));
        Assert.Single(app.Clients.All);
    });

    [Fact]
    public void What_is_typed_about_a_client_is_kept_at_once() => WithClientsForm((form, app) =>
    {
        var grid = (DataGridView)Field<Control>(form, "_clients");
        Type(grid, "✎ ЕИК", 0, SupplierVat);   // both numbers in one
        Type(grid, "✎ ДДС покупки", 0, "453/1/7");
        Type(grid, "✎ Каса", 0, "501 1");      // not an account
        Type(grid, "✎ Бележка", 0, "бележка");
        Assert.Contains("знакът", Assert.Single(_messages));

        var kept = Assert.Single(new AppState(SettingsPath, rateSource: new FakeSource()).Clients.All);
        Assert.Equal((SupplierEik, SupplierVat, "453/1/7", "", "бележка"), (kept.Eik, kept.VatNumber, kept.PurchaseVatAccount, kept.CashAccount, kept.Note));
        Assert.True(((ClientsForm)form).AnythingChanged);
    }, app => app.Save(new ClientSettings { Name = "Фирма" }));

    [Fact]
    public void Accounts_are_added_and_removed() => WithClientsForm((form, app) =>
    {
        Field<TextBox>(form, "_source").Text = "4012";
        Click(form, "Добави сметка");
        Assert.Contains("Въведете сметката в Делта Pro или валутата", Assert.Single(_messages));
        Field<TextBox>(form, "_target").Text = "401/2";
        Field<TextBox>(form, "_currency").Text = "usd";
        Click(form, "Добави сметка");
        var client = Assert.Single(app.Clients.All);
        Assert.Equal(("401/2", "USD"), (client.Accounts["4012"], client.AccountCurrencies["4012"]));

        Click(form, "Премахни избраните");
        Assert.Empty(client.Accounts);
        Assert.Empty(client.AccountCurrencies);
    }, app => app.Save(new ClientSettings { Name = "Фирма" }));

    [Fact]
    public void The_client_of_an_open_file_is_offered() => WithClientsForm((form, app) =>
    {
        Click(form, "Добави „Тестова Фирма ЕООД“");
        Assert.Equal(("Тестова Фирма ЕООД", "987654326"), (Assert.Single(app.Clients.All).Name, app.Clients.All[0].Eik));
        Assert.DoesNotContain(All(form).OfType<Button>(), b => b.Text.StartsWith("Добави „", StringComparison.Ordinal));
    }, open: [new TestSheet(Layout.Purchases).Purchase(1d, "1", 100, 20).Read()]);

    [Fact]
    public void Deleting_a_client_is_asked_about() => WithClientsForm((form, app) =>
    {
        Click(form, "Изтрий фирмата");   // the answer here is „Отказ"
        Assert.StartsWith("Да изтрия ли „Фирма“?", Assert.Single(_messages));
        Assert.Single(app.Clients.All);
    }, app => app.Save(new ClientSettings { Name = "Фирма" }));

    [Fact]
    public void The_settings_of_a_client_are_found_by_its_name() => WithFile(
        new TestSheet(Layout.Purchases).Purchase(1d, "1", 100, 20), (_, batch, _) => Assert.EndsWith("453/9", batch.Documents[0].Accounts()),
        app => app.Save(new ClientSettings { Name = "тестова фирма еоод", PurchaseVatAccount = "453/9" }));   // without an ЕИК

    // ----- operating -----

    private string SettingsPath => Path.Combine(_folder, "settings.json");

    private AppState NewApp(Action<AppState>? prepare)
    {
        var app = new AppState(SettingsPath, rateSource: _source, pause: TimeSpan.Zero);
        app.Settings.CheckForUpdates = false;
        prepare?.Invoke(app);
        return app;
    }

    private void OnScreen(Action<MainForm, AppState> act, Action<AppState>? prepare = null) => OnThread(() =>
    {
        using var app = NewApp(prepare);
        using var updates = new UpdateService(AppInfo.Version);
        using var main = new MainForm(app, updates);
        Show(main);
        act(main, app);
    });

    private void WithRatesForm(Action<RatesForm, AppState> act, IReadOnlyList<DateTime>? needed = null) => OnThread(() =>
    {
        using var app = NewApp(null);
        using var form = new RatesForm(app, needed);
        Show(form);
        act(form, app);
    });

    private void WithClientsForm(Action<Form, AppState> act, Action<AppState>? prepare = null, IReadOnlyList<OperationBatch>? open = null) => OnThread(() =>
    {
        using var app = NewApp(prepare);
        using var form = new ClientsForm(app, open ?? []);
        Show(form);
        act(form, app);
    });

    private void WithFile(TestSheet sheet, Action<WorkView, OperationBatch, AppState> act, Action<AppState>? prepare = null) => OnThread(() =>
    {
        using var app = NewApp(prepare);
        var batch = sheet.Read();
        using var form = new Form { ClientSize = new Size(1500, 800) };
        using var view = new WorkView(app, batch);
        form.Controls.Add(view);
        Show(form);
        act(view, batch, app);
    });

    private void OnThread(Action act)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(_context = new WindowsFormsSynchronizationContext());
                Dialogs.Instead = (heading, text) => _messages.Add(heading + "\n" + text);
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

    // DoEvents takes the context of the window thread away; without it an „await" would go on elsewhere.
    [ThreadStatic]
    private static SynchronizationContext? _context;

    private static void Pump()
    {
        for (int i = 0; i < 8; i++) Application.DoEvents();
        SynchronizationContext.SetSynchronizationContext(_context);
    }

    private static void Until(Func<bool> done)
    {
        var limit = DateTime.UtcNow.AddSeconds(20);
        while (!done() && DateTime.UtcNow < limit)
        {
            Pump();
            Thread.Sleep(5);
        }

        Assert.True(done());
        Pump();
    }

    private static void Leave(Control box)
    {
        typeof(Control).GetMethod("OnLeave", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(box, [EventArgs.Empty]);
        Pump();
    }

    private static void Click(Control parent, string text)
    {
        var button = All(parent).OfType<Button>().Single(b => b.Text == text && b.Visible);
        Assert.True(button.Enabled, text);
        button.PerformClick();
        Pump();
    }

    private static DataGridView Documents(WorkView view) => (DataGridView)Field<Control>(view, "_documents");

    private static DataGridViewColumn Column(DataGridView grid, string header) => grid.Columns.Cast<DataGridViewColumn>().Single(c => c.HeaderText == header);

    private static bool Visible(DataGridView grid, string header) => Column(grid, header).Visible;

    private static string Header(DataGridView grid, string ending) =>
        grid.Columns.Cast<DataGridViewColumn>().Single(c => c.Visible && c.HeaderText.EndsWith(ending, StringComparison.Ordinal)).HeaderText;

    private static string Cell(DataGridView grid, string header, int row) => (string)(grid[Column(grid, header).Index, row].Value ?? "");

    private static void Type(DataGridView grid, string header, int row, string text)
    {
        grid.CurrentCell = grid[Column(grid, header).Index, row];
        Assert.True(grid.BeginEdit(true));
        grid.EditingControl!.Text = text;
        grid.NotifyCurrentCellDirty(true);
        Assert.True(grid.EndEdit());
        Pump();
    }

    private static List<string> Labels(Control parent) => All(parent).OfType<Label>().Where(l => l.Visible).Select(l => l.Text).ToList();

    private static IEnumerable<Control> All(Control parent) => parent.Controls.Cast<Control>().SelectMany(c => All(c).Prepend(c));

    private static T Field<T>(object owner, string name) where T : class =>
        Assert.IsAssignableFrom<T>(owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner));
}
