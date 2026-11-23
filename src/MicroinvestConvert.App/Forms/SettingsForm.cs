using MicroinvestConvert.App.Ui;
using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Rules;
using MicroinvestConvert.Core.Settings;
using MicroinvestConvert.Core.Values;

namespace MicroinvestConvert.App.Forms;

/// <summary>„Настройки": everything that is rarely changed and so stays out of the main window.</summary>
internal sealed class SettingsForm : AppForm
{
    private readonly AppState _app;
    private readonly string _clientEik, _clientName;

    private readonly TextBox _purchaseVat = Make.TextBox(120);
    private readonly TextBox _saleVat = Make.TextBox(120);
    private readonly TextBox _clientPurchaseVat = Make.TextBox(120);
    private readonly TextBox _clientSaleVat = Make.TextBox(120);

    private readonly TextBox _cashAccount = Make.TextBox(120, "няма");
    private readonly CheckBox _cashRows;
    private readonly ComboBox _noDiary = Make.Combo(330,
        "1 – Вътрешен оборот (не влиза в дневника)", "3 – без данъчен кредит (влиза в дневника, к. 9)", "Не се записва във файла");
    private readonly ComboBox _dealFormat = Make.Combo(330,
        "По една стойност за всяка сума (2;2)", "Една стойност, когато всички са еднакви (2)");
    private readonly ComboBox _vatLines = Make.Combo(330,
        "Общо за документа (602;621;453/1)", "След всяка сметка (602;453/1;621;453/1)");
    private readonly CheckBox _updates;
    private readonly Grid<Mapping> _mappings = new();
    private readonly List<Mapping> _mappingRows = [];

    private sealed class Mapping
    {
        /// <summary>Null = for everybody.</summary>
        public required ClientSettings? Client { get; init; }
        public required string Source { get; init; }
        public required string Target { get; init; }
        public required string Currency { get; init; }
    }

    public SettingsForm(AppState app, OperationBatch? batch)
    {
        _app = app;
        _clientEik = batch?.ClientEik ?? "";
        _clientName = batch?.ClientName ?? "";
        var general = app.Settings.General;

        Text = "Настройки";
        AsDialog(720, 450, 640, 420, maximize: false);

        _cashRows = Make.Check("Документ, платен в брой, получава втори ред (РКО при покупка, ПКО при продажба)", general.CashRows);
        _updates = Make.Check("При стартиране проверявай за нова версия", app.Settings.CheckForUpdates);
        _purchaseVat.Text = general.PurchaseVatAccount;
        _saleVat.Text = general.SaleVatAccount;
        _cashAccount.Text = general.DefaultCashAccount;
        _noDiary.SelectedIndex = (int)general.NoDiary;
        _dealFormat.SelectedIndex = (int)general.DealTypeFormat;
        _vatLines.SelectedIndex = (int)general.VatLines;

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 5), Margin = new Padding(8, 8, 8, 0) };
        tabs.TabPages.Add(Page("ДДС сметки", BuildVatAccounts()));
        tabs.TabPages.Add(Page("Файл", BuildFile()));
        tabs.TabPages.Add(Page("Сметки", BuildAccounts()));
        tabs.TabPages.Add(Page("Обновяване", Make.Stack(_updates,
            Make.Text($"Версия {AppInfo.VersionText}. Новите версии се взимат от {AppInfo.RepositoryUrl}.", Theme.Muted, 620))));

        var save = new FlatButton("Запази", ButtonLook.Primary);
        save.Click += (_, _) => Accept();
        var cancel = new FlatButton("Отказ", ButtonLook.Quiet);
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        AcceptButton = save;
        CancelButton = cancel;

        var buttons = Make.Flow(cancel, save);
        buttons.Anchor = AnchorStyles.Right;
        buttons.Margin = new Padding(8);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(tabs, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);
        EndLayout();
    }

    private static TabPage Page(string title, Control content)
    {
        var page = new TabPage(title) { BackColor = Theme.Surface, Padding = new Padding(14, 12, 14, 12), AutoScroll = true };
        content.Dock = content is TableLayoutPanel { AutoSize: false } ? DockStyle.Fill : DockStyle.Top;
        page.Controls.Add(content);
        return page;
    }

    private Control BuildVatAccounts()
    {
        bool hasClient = _clientEik.Length > 0 || _clientName.Length > 0;
        var client = _app.Clients.Find(_clientEik, _clientName);
        _clientPurchaseVat.Text = client?.PurchaseVatAccount ?? "";
        _clientSaleVat.Text = client?.SaleVatAccount ?? "";
        _clientPurchaseVat.PlaceholderText = _clientSaleVat.PlaceholderText = "като за всички";
        _clientPurchaseVat.Enabled = _clientSaleVat.Enabled = hasClient;

        string clientTitle = hasClient ? (_clientName.Length > 0 ? _clientName : _clientEik) : "Само за една фирма (отворете неин файл)";

        var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Margin = new Padding(0, 4, 0, 8) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { AutoSize = true }, 0, 0);
        table.Controls.Add(Make.Label("Всички фирми", font: Theme.Bold), 1, 0);
        table.Controls.Add(Make.Label(clientTitle, hasClient ? Theme.Ink : Theme.Muted, Theme.Bold), 2, 0);
        table.Controls.Add(Make.Label("ДДС при покупки"), 0, 1);
        table.Controls.Add(_purchaseVat, 1, 1);
        table.Controls.Add(_clientPurchaseVat, 2, 1);
        table.Controls.Add(Make.Label("ДДС при продажби"), 0, 2);
        table.Controls.Add(_saleVat, 1, 2);
        table.Controls.Add(_clientSaleVat, 2, 2);

        return Make.Stack(
            Make.Text("Сметката, по която се записва ДДС. Стандартно: 453/1 при покупки и 453/2 при продажби. " +
                      "Делта Pro разпознава вида на сделката по тези две сметки, затова ги променяйте само като изключение.", Theme.Muted, 620),
            table,
            Make.Text("Когато важи друга сметка, главният прозорец го показва.", Theme.Muted, 620));
    }

    private Control BuildFile()
    {
        var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 6) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.Controls.Add(Make.Label("Вид сделка (кол. N)"), 0, 0);
        table.Controls.Add(_dealFormat, 1, 0);
        table.Controls.Add(Make.Label("ДДС в сумите (кол. A)"), 0, 1);
        table.Controls.Add(_vatLines, 1, 1);
        table.Controls.Add(Make.Label("Покупки „без ДДС дневник“"), 0, 2);
        table.Controls.Add(_noDiary, 1, 2);

        return Make.Stack(
            table,
            _cashRows,
            Make.Flow(Make.Label("Каса, когато във файла няма:"), _cashAccount),
            Make.Text("Закръглянето на ДДС и допустимата разлика са в „Проверки → ДДС“. Кои проверки са включени – в „Проверки → Всички проверки“.", Theme.Muted, 620));
    }

    private Control BuildAccounts()
    {
        var general = _app.Settings.General;
        AddMappings(null, general.Accounts, general.AccountCurrencies);
        foreach (var client in _app.Clients.All) AddMappings(client, client.Accounts, client.AccountCurrencies);

        _mappings.ReadOnly = true;
        _mappings.SetHeights(25, 28);
        _mappings.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _mappings.BorderStyle = BorderStyle.FixedSingle;
        _mappings.AddColumn(new() { Header = "Във файла", Width = 110, Get = m => m.Source });
        _mappings.AddColumn(new() { Header = "В Делта Pro", Width = 110, Get = m => m.Target });
        _mappings.AddColumn(new() { Header = "Валута", Width = 80, Center = true, Get = m => m.Currency, Tip = "На валутна сметка в Делта Pro" });
        _mappings.AddColumn(new() { Header = "Важи за", Width = 200, Fill = true, Get = m => m.Client?.Name ?? "Всички фирми" });
        _mappings.SortRequested += () => _mappings.SetRows(_mappingRows);
        _mappings.SetRows(_mappingRows);

        var remove = new FlatButton("Премахни избраните");
        remove.Click += (_, _) =>
        {
            foreach (var row in _mappings.SelectedItems()) _mappingRows.Remove(row);
            _mappings.SetRows(_mappingRows);
        };

        var page = new TableLayoutPanel { ColumnCount = 1, RowCount = 3, Dock = DockStyle.Fill, AutoSize = false };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.Controls.Add(Make.Text("Сметките се записват, както са във файла. Тук са запомнените съответствия за тези, " +
                                    "които в Делта Pro са с друг номер (добавят се от „Проверки → Сметки“ в отворен файл).", Theme.Muted, 620), 0, 0);
        page.Controls.Add(_mappings, 0, 1);
        page.Controls.Add(remove, 0, 2);
        return page;
    }

    private void AddMappings(ClientSettings? client, Dictionary<string, string> accounts, Dictionary<string, string> currencies)
    {
        foreach (string source in accounts.Keys.Union(currencies.Keys, StringComparer.OrdinalIgnoreCase))
            _mappingRows.Add(new Mapping
            {
                Client = client, Source = source,
                Target = accounts.GetValueOrDefault(source, ""), Currency = currencies.GetValueOrDefault(source, ""),
            });
    }

    private void Accept()
    {
        string purchase = AccountText.Normalise(_purchaseVat.Text), sale = AccountText.Normalise(_saleVat.Text);
        string clientPurchase = AccountText.Normalise(_clientPurchaseVat.Text), clientSale = AccountText.Normalise(_clientSaleVat.Text);
        string cash = AccountText.Normalise(_cashAccount.Text);

        foreach (var (name, value, required) in new[]
                 {
                     ("ДДС при покупки", purchase, true), ("ДДС при продажби", sale, true),
                     ("ДДС при покупки за фирмата", clientPurchase, false), ("ДДС при продажби за фирмата", clientSale, false),
                     ("Каса", cash, false),
                 })
        {
            if (value.Length == 0 && !required) continue;
            if (AccountCodes.Problem(value) is not { } problem) continue;
            Dialogs.Warning(this, $"Неправилна сметка – {name}", $"„{value}“ {problem}.");
            return;
        }

        var general = _app.Settings.General;
        general.PurchaseVatAccount = purchase;
        general.SaleVatAccount = sale;
        general.CashRows = _cashRows.Checked;
        general.DefaultCashAccount = cash;
        general.NoDiary = (NoDiaryMode)_noDiary.SelectedIndex;
        general.DealTypeFormat = (DealTypeFormat)_dealFormat.SelectedIndex;
        general.VatLines = (VatLines)_vatLines.SelectedIndex;
        _app.Settings.CheckForUpdates = _updates.Checked;

        var changed = KeepMappings();
        if (_clientEik.Length > 0 || _clientName.Length > 0)
        {
            // A client is made only when there is something to keep for it.
            var client = _app.Clients.FindOrNew(_clientEik, _clientName);
            if (client.PurchaseVatAccount != clientPurchase || client.SaleVatAccount != clientSale)
            {
                client.PurchaseVatAccount = clientPurchase;
                client.SaleVatAccount = clientSale;
                changed.Add(client);
            }
        }

        string? failure = _app.Save();
        foreach (var client in changed) failure = _app.Save(client) ?? failure;
        if (failure != null) Dialogs.Warning(this, "Настройките не можаха да бъдат записани", failure);
        DialogResult = DialogResult.OK;
    }

    // What is left in the table is what stays. Returns the clients it changed something for.
    private HashSet<ClientSettings> KeepMappings()
    {
        Keep(null, _app.Settings.General.Accounts, _app.Settings.General.AccountCurrencies);
        return _app.Clients.All.Where(c => Keep(c, c.Accounts, c.AccountCurrencies)).ToHashSet();

        bool Keep(ClientSettings? client, Dictionary<string, string> accounts, Dictionary<string, string> currencies)
        {
            var left = _mappingRows.Where(r => r.Client == client).Select(r => r.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var gone = accounts.Keys.Union(currencies.Keys, StringComparer.OrdinalIgnoreCase).Where(k => !left.Contains(k)).ToList();
            foreach (string key in gone)
            {
                accounts.Remove(key);
                currencies.Remove(key);
            }

            return gone.Count > 0;
        }
    }
}
