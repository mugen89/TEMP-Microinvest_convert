using MicroinvestConvert.App.Ui;
using MicroinvestConvert.Core.Exchange;
using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Rules;
using MicroinvestConvert.Core.Settings;
using MicroinvestConvert.Core.Values;

namespace MicroinvestConvert.App.Forms;

/// <summary>
/// „Фирми": the clients in the database. One is made with a name only – Делта Pro does not ask for
/// its ЕИК. Everything typed is kept at once.
/// </summary>
internal sealed class ClientsForm : AppForm
{
    private sealed record Account(string Source, string Target, string Currency);

    private readonly AppState _app;
    private readonly IReadOnlyList<OperationBatch> _open;

    private readonly TextBox _name = Make.TextBox(300, "Име на нова фирма");
    private readonly FlowLayoutPanel _fromFiles = Make.Flow();
    private readonly Grid<ClientSettings> _clients = new();
    private readonly FlatButton _delete = new("Изтрий фирмата");
    private readonly Label _accountsTitle = Make.Label("", font: Theme.Bold);
    private readonly Grid<Account> _accounts = new();
    private readonly TextBox _source = Make.TextBox(96, "във файла");
    private readonly TextBox _target = Make.TextBox(110, "в Делта Pro");
    private readonly TextBox _currency = Make.TextBox(70, "валута");
    private readonly FlatButton _addAccount = new("Добави сметка");
    private readonly FlatButton _removeAccount = new("Премахни избраните", ButtonLook.Quiet);

    /// <summary>Open files have to be calculated again.</summary>
    public bool AnythingChanged { get; private set; }

    /// <param name="app">What the windows share.</param>
    /// <param name="open">The open files: their clients are offered.</param>
    public ClientsForm(AppState app, IReadOnlyList<OperationBatch> open)
    {
        _app = app;
        _open = open;

        Text = "Фирми";
        AsDialog(1080, 620, 860, 460);

        BuildClients();
        BuildAccounts();

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 6, BackColor = Theme.Window };
        split.Panel1.Controls.Add(Rows(0, _clients));
        split.Panel2.Controls.Add(Rows(1, AccountsStrip(), _accounts));
        split.HandleCreated += (_, _) => BeginInvoke(() => { if (split.Height > 240) split.SplitterDistance = (int)(split.Height * 0.6); });

        var note = Make.Text("Задължително е само името. Фирмата на отворен файл се познава по ЕИК, а ако го няма – по името. " +
                             "Празна сметка означава „като за всички фирми“.", Theme.Muted, 1000);
        note.Margin = new Padding(12, 10, 12, 2);

        var close = new FlatButton("Затвори", ButtonLook.Primary);
        close.Click += (_, _) => Close();
        CancelButton = close;
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8, 4, 8, 6), BackColor = Theme.Surface,
        };
        bar.Controls.Add(close);

        Controls.Add(Rows(2, note, NewClientStrip(), split, bar));
        RefreshClients();
        EndLayout();
    }

    // ----- layout -----

    private static TableLayoutPanel Rows(int filling, params Control[] controls)
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

    private Control NewClientStrip()
    {
        var add = new FlatButton("Добави", ButtonLook.Primary);
        add.Click += (_, _) => Add(_name.Text, "");
        _name.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            Add(_name.Text, "");
        };
        _delete.Click += (_, _) => Delete();

        var strip = Make.Flow(_name, add, _fromFiles, Make.Gap(16), _delete);
        strip.Margin = new Padding(9, 2, 9, 4);
        return strip;
    }

    private void BuildClients()
    {
        _clients.SetHeights(26, 30);
        Column("✎ Фирма", 200, c => c.Name, SetName, fill: true);
        Column("✎ ЕИК", 112, c => c.Eik, SetEik);
        Column("✎ ДДС №", 122, c => c.VatNumber, (c, v) => { c.VatNumber = v.ToUpperInvariant(); return null; });
        Column("✎ ДДС покупки", 142, c => c.PurchaseVatAccount, (c, v) => SetAccount(v, a => c.PurchaseVatAccount = a), "Сметка за ДДС при покупки; празно – като за всички");
        Column("✎ ДДС продажби", 150, c => c.SaleVatAccount, (c, v) => SetAccount(v, a => c.SaleVatAccount = a), "Сметка за ДДС при продажби; празно – като за всички");
        Column("✎ Каса", 84, c => c.CashAccount, (c, v) => SetAccount(v, a => c.CashAccount = a), "За плащане в брой, когато във файла няма каса; празно – като за всички");
        Column("✎ Бележка", 140, c => c.Note, (c, v) => { c.Note = v; return null; });
        _clients.CurrentCellChanged += (_, _) => RefreshAccounts();
        _clients.Edited += _ => RefreshAccounts();
        _clients.SortRequested += RefreshClients;
    }

    // What is accepted is kept at once; when it cannot be kept, that is the answer.
    private void Column(string header, int width, Func<ClientSettings, string> get, Func<ClientSettings, string, string?> set, string tip = "", bool fill = false) =>
        _clients.AddColumn(new()
        {
            Header = header, Width = width, Fill = fill, Get = get, Tip = tip,
            Set = (client, text) => set(client, CellValues.Clean(text)) ?? Keep(client),
        });

    private void BuildAccounts()
    {
        _accounts.ReadOnly = true;
        _accounts.SetHeights(25, 28);
        _accounts.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _accounts.AddColumn(new() { Header = "Сметка във файла", Width = 150, Get = a => a.Source });
        _accounts.AddColumn(new() { Header = "В Делта Pro", Width = 150, Get = a => a.Target.Length > 0 ? a.Target : "същата" });
        _accounts.AddColumn(new() { Header = "Валута", Width = 160, Fill = true, Get = a => a.Currency, Tip = "На сметка, която в Делта Pro е от тип „Валутна“" });
        _accounts.SortRequested += RefreshAccounts;
    }

    private Control AccountsStrip()
    {
        _addAccount.Click += (_, _) => AddAccount();
        _removeAccount.Click += (_, _) => RemoveAccounts();
        _accountsTitle.Margin = new Padding(6, 8, 12, 3);
        var strip = Make.Flow(_accountsTitle, _source, _target, _currency, _addAccount, Make.Gap(8), _removeAccount);
        strip.Margin = new Padding(3, 4, 3, 3);
        return strip;
    }

    // ----- clients -----

    private void RefreshClients()
    {
        _clients.SetRows(_app.Clients.All);

        _fromFiles.Controls.Clear();
        foreach (var (name, eik) in _open.Where(b => b.ClientName.Length > 0 && _app.Clients.Find(b.ClientEik, b.ClientName) == null)
                     .Select(b => (b.ClientName, b.ClientEik)).Distinct())
        {
            var add = new FlatButton($"Добави „{name}“", ButtonLook.Quiet);
            add.Click += (_, _) => Add(name, eik);
            _fromFiles.Controls.Add(add);
        }

        RefreshAccounts();
    }

    private void Add(string name, string eik)
    {
        name = CellValues.Clean(name);
        if (name.Length == 0)
        {
            _name.Focus();
            return;
        }

        if (_app.Clients.Problem(null, name, eik) is { } problem)
        {
            Dialogs.Warning(this, "Фирмата не е добавена", problem);
            return;
        }

        var client = new ClientSettings { Name = name, Eik = eik };
        if (Keep(client) is { } failure)
        {
            Dialogs.Warning(this, "Фирмата не е добавена", failure);
            return;
        }

        _name.Clear();
        RefreshClients();
        _clients.GoTo(client);
    }

    private void Delete()
    {
        if (_clients.Current is not { } client) return;
        if (!Dialogs.Ask(this, $"Да изтрия ли „{client.Name}“?", "Изтриват се и нейните сметки и настройки. Файловете не се променят.", "Изтрий", "Отказ", warning: true)) return;
        if (_app.Delete(client) is { } failure) Dialogs.Warning(this, "Фирмата не е изтрита", failure);
        AnythingChanged = true;
        RefreshClients();
    }

    private string? Keep(ClientSettings client)
    {
        AnythingChanged = true;
        return _app.Save(client);
    }

    private string? SetName(ClientSettings client, string name)
    {
        if (_app.Clients.Problem(client, name, client.Eik) is { } problem) return problem;
        client.Name = name;
        return null;
    }

    // „BG123456786" is both numbers.
    private string? SetEik(ClientSettings client, string text)
    {
        var identity = PartnerIdentity.Parse(text);
        string eik = identity.Eik.Length > 0 ? identity.Eik : text;
        if (_app.Clients.Problem(client, client.Name, eik) is { } problem) return problem;
        client.Eik = eik;
        if (client.VatNumber.Length == 0) client.VatNumber = identity.Eik.Length > 0 ? identity.VatNumber : "";
        return null;
    }

    private static string? SetAccount(string text, Action<string> set)
    {
        string account = AccountText.Normalise(text);
        if (account.Length > 0 && AccountCodes.Problem(account) is { } problem) return $"„{account}“ – {problem}.";
        set(account);
        return null;
    }

    // ----- the accounts of the chosen client -----

    private void RefreshAccounts()
    {
        var client = _clients.Current;
        _accountsTitle.Text = client != null ? $"Сметки на „{client.Name}“:" : "Няма избрана фирма";
        foreach (var control in new Control[] { _source, _target, _currency, _addAccount, _removeAccount, _delete }) control.Enabled = client != null;

        _accounts.SetRows(client == null ? [] : client.Accounts.Keys.Union(client.AccountCurrencies.Keys, StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .Select(source => new Account(source, client.Accounts.GetValueOrDefault(source, ""), client.AccountCurrencies.GetValueOrDefault(source, ""))));
    }

    private void AddAccount()
    {
        if (_clients.Current is not { } client) return;

        string source = AccountText.Normalise(_source.Text), target = AccountText.Normalise(_target.Text);
        string currency = Currencies.Normalise(_currency.Text);
        if (source.Length == 0)
        {
            _source.Focus();
            return;
        }

        string? problem = target.Length > 0 && AccountCodes.Problem(target) is { } wrong ? $"„{target}“ – {wrong}."
            : currency.Length > 0 && !Currencies.IsCode(currency) ? $"„{currency}“ не е код на валута, например USD."
            : target.Length == 0 && currency.Length == 0 ? "Въведете сметката в Делта Pro или валутата ѝ."
            : null;
        if (problem != null)
        {
            Dialogs.Warning(this, "Сметката не е добавена", problem);
            return;
        }

        if (target.Length > 0) client.Accounts[source] = target;
        if (currency.Length > 0) client.AccountCurrencies[source] = currency;
        if (Keep(client) is { } failure) Dialogs.Warning(this, "Сметката не е запазена", failure);

        foreach (var box in new[] { _source, _target, _currency }) box.Clear();
        _source.Focus();
        RefreshAccounts();
    }

    private void RemoveAccounts()
    {
        if (_clients.Current is not { } client || _accounts.SelectedItems() is not { Count: > 0 } chosen) return;
        foreach (var account in chosen)
        {
            client.Accounts.Remove(account.Source);
            client.AccountCurrencies.Remove(account.Source);
        }

        if (Keep(client) is { } failure) Dialogs.Warning(this, "Промяната не е запазена", failure);
        RefreshAccounts();
    }
}
