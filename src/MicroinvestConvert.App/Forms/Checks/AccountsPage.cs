using MicroinvestConvert.App.Ui;
using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Rules;
using MicroinvestConvert.Core.Settings;
using MicroinvestConvert.Core.Values;

namespace MicroinvestConvert.App.Forms.Checks;

/// <summary>„Сметки": every account of the file, and the account it becomes in Делта Pro.</summary>
internal sealed class AccountsPage : CheckPage
{
    public const string PageTitle = "Сметки";
    private const int TargetColumn = 4;

    private sealed class Row
    {
        public required string Account { get; init; }
        public required string Analytic { get; init; }
        public required string Name { get; init; }
        public required string Role { get; init; }
        public required int Documents { get; init; }

        /// <summary>Without any mapping.</summary>
        public required string Automatic { get; init; }
        public required string Original { get; init; }
        public string Target { get; set; } = "";

        public string Key => AccountText.Join(Account, Analytic);
        public bool Changed => Target != Original;
    }

    private readonly Grid<Row> _grid = new();
    private readonly RadioButton _onlyClient, _everybody;
    private readonly FlatButton _save, _undo;
    private List<Row> _rows = [];

    public override string Title => PageTitle;

    public AccountsPage(ICheckHost host) : base(host)
    {
        bool hasClient = Batch.ClientEik.Length > 0;

        _grid.Use(Palette);
        _grid.SetHeights(26, 30);
        _grid.AddColumn(new() { Header = "Във файла", Width = 110, Get = r => r.Key, Tip = "Основна сметка / подсметка" });
        _grid.AddColumn(new() { Header = "Име", Width = 220, Fill = true, Get = r => r.Name });
        _grid.AddColumn(new() { Header = "Роля", Width = 130, Get = r => r.Role });
        _grid.AddColumn(new() { Header = "Документи", Width = 108, Right = true, Get = r => r.Documents.ToString(), SortKey = r => r.Documents });
        _grid.AddColumn(new()
        {
            Header = "✎ В Делта Pro", Width = 150, Get = r => r.Target,
            Tip = "Променете, ако сметката в Делта Pro е друга. Изтрийте, за да се върне автоматичната.",
            Set = (r, v) =>
            {
                string target = AccountText.Normalise(v);
                if (target.Length == 0) target = r.Automatic;
                if (target.Length > 0 && AccountCodes.Problem(target) is { } problem) return $"„{target}“ – {problem}.";
                r.Target = target;
                return null;
            },
        });
        _grid.StyleCell = (row, column, style) =>
        {
            if (column != TargetColumn) return;
            style.Font = Theme.Bold;
            style.BackColor = row.Changed ? Theme.WarningBack : Palette.Soft;
            if (row.Target != row.Automatic) style.ForeColor = Theme.EditedInk;
        };
        _grid.Edited += _ => RefreshButtons();
        _grid.SortRequested += () => _grid.SetRows(_rows);

        _onlyClient = new RadioButton
        {
            Text = hasClient ? $"Само за {(Batch.ClientName.Length > 0 ? Batch.ClientName : Batch.ClientEik)}" : "Само за тази фирма (файлът няма ЕИК)",
            AutoSize = true, Enabled = hasClient, Checked = hasClient, Margin = new Padding(3, 8, 12, 3), UseMnemonic = false,
        };
        _everybody = new RadioButton { Text = "За всички фирми", AutoSize = true, Checked = !hasClient, Margin = new Padding(3, 8, 3, 3) };

        _save = MakeButton("Запомни", Accept, "Важи веднага и за следващите файлове.", ButtonLook.Primary);
        _undo = MakeButton("Отказ", () => Reload(keepTyped: false), "Въведеното тук се отхвърля.", ButtonLook.Quiet);

        var bar = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Padding = new Padding(8, 4, 5, 4) };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.Controls.Add(Make.Flow(_onlyClient, _everybody), 0, 0);
        bar.Controls.Add(Make.Flow(_undo, _save), 1, 0);

        Controls.Add(Rows(1,
            Note("Сметката се записва, както е във файла: основната сметка, после „/“ и подсметката – 4111 и 1 стават 4111/1, 411/1 и 1 стават 411/1/1. " +
                 "Когато в Делта Pro номерът е друг, напишете го в последната колона; запомня се и важи и за следващите файлове."),
            _grid, bar));
        EndLayout();
    }

    public override void RefreshRows() => Reload(keepTyped: true);

    private void Reload(bool keepTyped)
    {
        var typed = keepTyped ? _rows.Where(r => r.Changed).ToDictionary(r => (r.Key, r.Role), r => r.Target) : [];
        _rows = CollectRows();
        foreach (var row in _rows)
            if (typed.TryGetValue((row.Key, row.Role), out string? target)) row.Target = target;

        _grid.SetRows(_rows);
        RefreshButtons();
    }

    private void RefreshButtons() => _save.Enabled = _undo.Enabled = _rows.Any(r => r.Changed);

    private List<Row> CollectRows()
    {
        bool purchase = Batch.Kind.IsPurchaseSide();
        var options = Host.Options;
        var automatic = ConverterOptions.Default;

        Row New(string account, string analytic, string name, string role, int documents)
        {
            string target = AccountCodes.Format(account, analytic, options);
            return new Row
            {
                Account = account, Analytic = analytic, Name = name, Role = role, Documents = documents,
                Automatic = AccountCodes.Format(account, analytic, automatic), Original = target, Target = target,
            };
        }

        var rows = new List<Row>();
        rows.AddRange(Batch.Documents
            .SelectMany(d => d.Lines.Select(l => (Doc: d, Line: l)))
            .Where(x => x.Line.Account.Length > 0)
            .GroupBy(x => x.Line.AccountKey)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => New(g.First().Line.Account, g.First().Line.Analytic,
                g.First().Line.AnalyticName.Length > 0 ? g.First().Line.AnalyticName : g.First().Line.AccountName,
                purchase ? "разход / актив" : "приход", g.Select(x => x.Doc).Distinct().Count())));
        rows.AddRange(Batch.Documents.Where(d => d.CounterAccount.Length > 0).GroupBy(d => d.CounterAccount)
            .Select(g => New(g.Key, "", purchase ? "Доставчици" : "Клиенти", purchase ? "доставчик" : "клиент", g.Count())));
        rows.AddRange(Batch.Documents.Where(d => d.IsCash && d.CashAccount.Length > 0).GroupBy(d => d.CashAccount)
            .Select(g => New(g.Key, "", "Каса", "плащане в брой", g.Count())));
        return rows;
    }

    private void Accept()
    {
        _grid.EndEdit();

        var settings = Host.App.Settings;
        var client = _onlyClient.Checked ? settings.ClientFor(Batch.ClientEik, Batch.ClientName) : null;
        foreach (var row in _rows.Where(r => r.Changed))
        {
            if (client != null)
            {
                // „Automatic" for the client may still differ from a common mapping.
                client.Accounts.Remove(row.Key);
                if (Resolve(row) != row.Target) client.Accounts[row.Key] = row.Target;
            }
            else
            {
                settings.General.Accounts.Remove(row.Key);
                if (settings.Clients.TryGetValue(Batch.ClientEik, out var own)) own.Accounts.Remove(row.Key);
                if (Resolve(row) != row.Target) settings.General.Accounts[row.Key] = row.Target;
            }
        }

        if (Host.App.Save() is { } problem) Dialogs.Warning(FindForm(), "Настройките не можаха да бъдат записани", problem);
        _rows = [];   // what was typed is in force now
        Host.SettingsChanged();
    }

    private string Resolve(Row row) =>
        AccountCodes.Format(row.Account, row.Analytic, Host.App.Settings.OptionsFor(Batch.ClientEik));
}
