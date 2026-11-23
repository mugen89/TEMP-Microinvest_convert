using MicroinvestConvert.App.Ui;
using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Rules;
using MicroinvestConvert.Core.Settings;
using MicroinvestConvert.Core.Values;

namespace MicroinvestConvert.App.Forms;

/// <summary>„Сметки": every account of the file, and the account it becomes in Делта Pro.</summary>
internal sealed class AccountsForm : AppForm
{
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

    private readonly AppState _app;
    private readonly OperationBatch _batch;
    private readonly Grid<Row> _grid = new();
    private readonly RadioButton _onlyClient, _everybody;
    private readonly List<Row> _rows;

    public AccountsForm(AppState app, OperationBatch batch)
    {
        _app = app;
        _batch = batch;
        var palette = Palette.For(batch.Kind);
        bool hasClient = batch.ClientEik.Length > 0;

        Text = "Сметки";
        AsDialog(880, 560, 720, 400);

        _rows = CollectRows();

        _grid.Use(palette);
        _grid.SetHeights(26, 30);
        _grid.AddColumn(new() { Header = "Във файла", Width = 110, Get = r => r.Key, Tip = "Основна сметка / подсметка" });
        _grid.AddColumn(new() { Header = "Име", Width = 220, Fill = true, Get = r => r.Name });
        _grid.AddColumn(new() { Header = "Роля", Width = 130, Get = r => r.Role });
        _grid.AddColumn(new() { Header = "Документи", Width = 108, Right = true, Get = r => r.Documents.ToString(), SortKey = r => r.Documents });
        _grid.AddColumn(new()
        {
            Header = "В Делта Pro", Width = 150, Get = r => r.Target, Tip = "Променете, ако сметката в Делта Pro е друга. Изтрийте, за да се върне автоматичната.",
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
            if (column != 4) return;
            style.Font = Theme.Bold;
            if (row.Target != row.Automatic) style.ForeColor = Theme.EditedInk;
        };
        _grid.SortRequested += () => _grid.SetRows(_rows);
        _grid.SetRows(_rows);

        _onlyClient = new RadioButton
        {
            Text = hasClient ? $"Само за {(batch.ClientName.Length > 0 ? batch.ClientName : batch.ClientEik)}" : "Само за тази фирма (файлът няма ЕИК)",
            AutoSize = true, Enabled = hasClient, Checked = hasClient, Margin = new Padding(3, 6, 12, 3), UseMnemonic = false,
        };
        _everybody = new RadioButton { Text = "За всички фирми", AutoSize = true, Checked = !hasClient, Margin = new Padding(3, 6, 3, 3) };

        var note = Make.Text(
            "Сметката се записва, както е във файла: основната сметка, после „/“ и подсметката – 4111 и 1 стават 4111/1, 411/1 и 1 стават 411/1/1. " +
            "Когато в Делта Pro номерът е друг, напишете го в последната колона; запомня се и важи и за следващите файлове.", Theme.Muted, 820);
        note.Margin = new Padding(12, 8, 12, 4);

        var save = new FlatButton("Запомни", ButtonLook.Primary);
        save.Use(palette);
        save.Click += (_, _) => Accept();
        var cancel = new FlatButton("Отказ", ButtonLook.Quiet);
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        CancelButton = cancel;

        var bar = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Padding = new Padding(10, 6, 8, 8), BackColor = Theme.Surface };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var scope = Make.Flow(_onlyClient, _everybody);
        bar.Controls.Add(scope, 0, 0);
        bar.Controls.Add(Make.Flow(cancel, save), 1, 0);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(note, 0, 0);
        root.Controls.Add(_grid, 0, 1);
        root.Controls.Add(bar, 0, 2);
        Controls.Add(root);
        EndLayout();
    }

    private List<Row> CollectRows()
    {
        bool purchase = _batch.Kind == OperationKind.Purchase;
        var options = _app.Settings.OptionsFor(_batch.ClientEik);
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
        rows.AddRange(_batch.Documents
            .SelectMany(d => d.Lines.Select(l => (Doc: d, Line: l)))
            .Where(x => x.Line.Account.Length > 0)
            .GroupBy(x => x.Line.AccountKey)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => New(g.First().Line.Account, g.First().Line.Analytic,
                g.First().Line.AnalyticName.Length > 0 ? g.First().Line.AnalyticName : g.First().Line.AccountName,
                purchase ? "разход / актив" : "приход", g.Select(x => x.Doc).Distinct().Count())));
        rows.AddRange(_batch.Documents.Where(d => d.CounterAccount.Length > 0).GroupBy(d => d.CounterAccount)
            .Select(g => New(g.Key, "", purchase ? "Доставчици" : "Клиенти", purchase ? "доставчик" : "клиент", g.Count())));
        rows.AddRange(_batch.Documents.Where(d => d.IsCash && d.CashAccount.Length > 0).GroupBy(d => d.CashAccount)
            .Select(g => New(g.Key, "", "Каса", "плащане в брой", g.Count())));
        return rows;
    }

    private void Accept()
    {
        _grid.EndEdit();

        var changed = _rows.Where(r => r.Changed).ToList();
        if (changed.Count == 0)
        {
            DialogResult = DialogResult.Cancel;
            return;
        }

        var general = _app.Settings.General.Accounts;
        var client = _onlyClient.Checked ? _app.Settings.ClientFor(_batch.ClientEik, _batch.ClientName) : null;
        foreach (var row in changed)
        {
            if (client != null)
            {
                // „Automatic" for the client may still differ from a common mapping.
                client.Accounts.Remove(row.Key);
                if (Resolve(row) != row.Target) client.Accounts[row.Key] = row.Target;
            }
            else
            {
                general.Remove(row.Key);
                if (_app.Settings.Clients.TryGetValue(_batch.ClientEik, out var own)) own.Accounts.Remove(row.Key);
                if (Resolve(row) != row.Target) general[row.Key] = row.Target;
            }
        }

        if (_app.Save() is { } problem) Dialogs.Warning(this, "Настройките не можаха да бъдат записани", problem);
        DialogResult = DialogResult.OK;
    }

    private string Resolve(Row row) =>
        AccountCodes.Format(row.Account, row.Analytic, _app.Settings.OptionsFor(_batch.ClientEik));
}
