using MicroinvestConvert.App.Ui;
using MicroinvestConvert.Core.Exchange;
using MicroinvestConvert.Core.Values;
using MicroinvestConvert.Data;

namespace MicroinvestConvert.App.Forms;

/// <summary>
/// „Валутни курсове": from which day they are kept, whether they come by themselves, the button
/// that brings them now, and the rates there are.
/// </summary>
internal sealed class RatesForm : AppForm
{
    private sealed record Row(DateTime Day, DateTime Published, string Code, string Name, decimal PerEuro);

    private const string AllCurrencies = "всички валути";

    private readonly RatesService _rates;
    private readonly IReadOnlyList<DateTime> _needed;

    private readonly TextBox _from = Make.TextBox(96, "дд.мм.гггг");
    private readonly Label _fromHint = Make.Label("", Theme.Muted);
    private readonly CheckBox _auto = Make.Check("Изтегляй при стартиране");
    private readonly FlatButton _pull = new("Изтегли сега", ButtonLook.Primary);
    private readonly FlatButton _pullNeeded = new("", ButtonLook.Normal);
    private readonly FlatButton _stop = new("Спри");
    private readonly Label _days = Make.Label("", font: Theme.Bold);
    private readonly Label _last = Make.Label("", Theme.Muted);
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Height = 8, Margin = new Padding(12, 0, 12, 4), Visible = false };

    private readonly TextBox _day = Make.TextBox(96, "дд.мм.гггг");
    private readonly ComboBox _currency = Make.Combo(230);
    private readonly Label _table = Make.Label("", Theme.Muted);
    private readonly Grid<Row> _grid = new();
    private IReadOnlyList<(string Code, string Name)> _currencies = [];
    private bool _filling;

    /// <param name="app">What the windows share.</param>
    /// <param name="needed">Days that open documents wait for.</param>
    public RatesForm(AppState app, IReadOnlyList<DateTime>? needed = null)
    {
        _rates = app.Rates;
        _needed = needed ?? [];

        Text = "Валутни курсове";
        AsDialog(840, 620, 760, 460);

        BuildGrid();
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(BuildSettings(), 0, 0);
        root.Controls.Add(_progress, 0, 1);
        root.Controls.Add(BuildFilter(), 0, 2);
        root.Controls.Add(_grid, 0, 3);
        root.Controls.Add(BuildFooter(app.DatabasePath), 0, 4);
        Controls.Add(root);

        _rates.Changed += RefreshState;
        _rates.Arrived += RefreshRates;
        RefreshState();
        ShowNewestDay();
        EndLayout();
    }

    // ----- layout -----

    private Control BuildSettings()
    {
        _from.Text = Fmt.Date(_rates.From);
        _from.Leave += (_, _) => TakeFrom();
        _from.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            TakeFrom();
        };
        _from.TextChanged += (_, _) => RefreshState();
        _auto.Checked = _rates.Auto;
        _auto.CheckedChanged += (_, _) => Keep(() => _rates.Auto = _auto.Checked);

        _pull.Click += async (_, _) => await PullAsync(_rates.PullAsync);
        _pullNeeded.Text = $"Само за отворения файл ({_needed.Count})";
        _pullNeeded.Visible = _needed.Count > 0;
        _pullNeeded.Click += async (_, _) => await PullAsync(() => _rates.PullAsync(_needed));
        _stop.Click += (_, _) => _rates.Stop();
        _stop.Visible = false;

        var settings = Make.Flow(Make.Label("Начална дата:"), _from, Make.Gap(8), _auto, Make.Gap(12), _pull, _stop, _pullNeeded);
        settings.Margin = new Padding(9, 10, 9, 0);
        _fromHint.Margin = new Padding(12, 2, 12, 4);
        _days.Margin = new Padding(12, 2, 12, 0);
        _last.Margin = new Padding(12, 2, 12, 6);
        return Make.Stack(settings, _fromHint, _days, _last);
    }

    private Control BuildFilter()
    {
        var back = Step("◀", -1);
        var forward = Step("▶", 1);
        _day.Leave += (_, _) => RefreshRates();
        _day.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            RefreshRates();
        };
        _currency.SelectedIndexChanged += (_, _) => RefreshRates();

        var filter = Make.Flow(Make.Label("За дата:"), back, _day, forward, Make.Gap(10), Make.Label("Валута:"), _currency, Make.Gap(6), _table);
        filter.Margin = new Padding(9, 4, 9, 4);
        return filter;

        FlatButton Step(string text, int days)
        {
            var button = new FlatButton(text, ButtonLook.Quiet);
            button.Click += (_, _) =>
            {
                _currency.SelectedIndex = 0;
                _day.Text = Fmt.Date((CellValues.Date(_day.Text) ?? DateTime.Today).AddDays(days));
                RefreshRates();
            };
            return button;
        }
    }

    private void BuildGrid()
    {
        _grid.ReadOnly = true;
        _grid.SetHeights(24, 28);
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AddColumn(new() { Header = "Дата", Width = 92, Get = r => Fmt.Date(r.Day), SortKey = r => r.Day });
        _grid.AddColumn(new() { Header = "Код", Width = 60, Get = r => r.Code });
        _grid.AddColumn(new() { Header = "Валута", Width = 150, Fill = true, Get = r => r.Name });
        _grid.AddColumn(new() { Header = "За 1 евро", Width = 120, Right = true, Get = r => Fmt.Rate(r.PerEuro), SortKey = r => r.PerEuro, Tip = "Колко единици валута са едно евро" });
        _grid.AddColumn(new() { Header = "Евро за единица", Width = 136, Right = true, Get = r => Fmt.Rate(1m / r.PerEuro), SortKey = r => 1m / r.PerEuro, Tip = "Колко евро е една единица валута" });
        _grid.AddColumn(new() { Header = "Курс от", Width = 92, Get = r => Fmt.Date(r.Published), SortKey = r => r.Published, Tip = "В почивни дни важи последният публикуван курс" });
        _grid.SortRequested += RefreshRates;
    }

    private Control BuildFooter(string database)
    {
        var source = Make.Label($"Източник: {_rates.SourceName}  ·  {(database.Length > 0 ? database : "базата данни не е достъпна")}", Theme.Muted, Theme.Small);
        source.AutoSize = false;
        source.AutoEllipsis = true;
        source.Dock = DockStyle.Fill;
        source.TextAlign = ContentAlignment.MiddleLeft;

        var close = new FlatButton("Затвори", ButtonLook.Quiet);
        close.Click += (_, _) => Close();
        CancelButton = close;

        var bar = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Padding = new Padding(8, 4, 8, 6), BackColor = Theme.Surface };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.Controls.Add(source, 0, 0);
        bar.Controls.Add(close, 1, 0);
        return bar;
    }

    // ----- the starting date -----

    /// <summary>What is typed: no date, a date, or not a date. The last is only said beside the field.</summary>
    private bool TryReadFrom(out DateTime? from, out string problem)
    {
        from = null;
        problem = "";
        string text = _from.Text.Trim();
        if (text.Length == 0) return true;

        if (CellValues.Date(text) is not { } day) problem = "Въведете датата като 01.01.2026.";
        else if (day < RateCalendar.Earliest) problem = $"Курсове има от {Fmt.Date(RateCalendar.Earliest)}.";
        else if (day > DateTime.Today) problem = "Датата е в бъдещето.";
        else from = day;
        return problem.Length == 0;
    }

    private void TakeFrom()
    {
        if (!TryReadFrom(out var from, out _)) return;
        Keep(() => _rates.From = from);
        _from.Text = Fmt.Date(from);
    }

    // A setting that cannot be kept is worth a word.
    private void Keep(Action set)
    {
        try { set(); }
        catch (DatabaseException ex) { Dialogs.Warning(this, "Настройката не е запазена", ex.Message); }
    }

    // ----- downloading -----

    private async Task PullAsync(Func<Task<RateUpdate?>> pull)
    {
        TakeFrom();
        try
        {
            var result = await pull();
            if (IsDisposed || result?.Failure is not { } failure) return;
            Dialogs.Warning(this, failure.Title, failure.Message + (result.Added > 0 ? $"\n\nИзтеглено преди това: {RatesService.Text(result with { Failure = null })}." : ""));
        }
        catch (DatabaseException ex)
        {
            if (!IsDisposed) Dialogs.Warning(this, "Курсовете не могат да бъдат записани", ex.Message);
        }
    }

    private void RefreshState()
    {
        if (IsDisposed) return;

        bool valid = TryReadFrom(out var from, out string problem);
        _fromHint.Text = !valid ? problem
            : from == null ? "Докато датата е празна, нищо не се изтегля."
            : "Курсовете се изтеглят от тази дата досега.";
        _fromHint.ForeColor = valid ? Theme.Muted : Theme.ErrorInk;

        _days.Text = "Има курсове за: " + _rates.DaysText;
        _last.Text = _rates.IsRunning
            ? _rates.Progress.Total > 0 ? $"Изтегляне – {_rates.Progress.Done} от {_rates.Progress.Total}" : "Изтегляне…"
            : _rates.LastResult.Length > 0 ? "Последно изтегляне: " + _rates.LastResult : "Още не са изтегляни.";

        _progress.Visible = _rates.IsRunning;
        if (_rates.IsRunning)
        {
            _progress.Maximum = Math.Max(1, _rates.Progress.Total);
            _progress.Value = Math.Min(_rates.Progress.Done, _progress.Maximum);
        }

        _stop.Visible = _rates.IsRunning;
        _pull.Enabled = !_rates.IsRunning && valid && from != null && _rates.Store != null;
        _pullNeeded.Enabled = !_rates.IsRunning && _rates.Store != null;
    }

    // ----- the rates -----

    private void ShowNewestDay()
    {
        var ranges = _rates.Ranges;
        _day.Text = Fmt.Date(ranges.Count > 0 ? ranges[^1].To : DateTime.Today);
        RefreshRates();
    }

    private void RefreshRates()
    {
        if (IsDisposed || _filling) return;
        _filling = true;
        try
        {
            FillCurrencies();
            Fill();
        }
        catch (DatabaseException ex)
        {
            _grid.SetRows([]);
            _table.Text = ex.Message;
        }
        finally
        {
            _filling = false;
        }
    }

    private void FillCurrencies()
    {
        var all = _rates.Store?.Currencies() ?? [];
        if (all.SequenceEqual(_currencies) && _currency.Items.Count > 0) return;

        string chosen = _currency.Text;
        _currencies = all;
        _currency.Items.Clear();
        _currency.Items.Add(AllCurrencies);
        _currency.Items.AddRange([.. all.Select(c => (object)$"{c.Code} – {c.Name}")]);
        _currency.SelectedIndex = Math.Max(0, _currency.Items.IndexOf(chosen));
    }

    private void Fill()
    {
        _table.Text = "";
        if (_rates.Store is not { } store)
        {
            _grid.SetRows([]);
            return;
        }

        if (_currency.SelectedIndex > 0)
        {
            var (code, name) = _currencies[_currency.SelectedIndex - 1];
            _grid.SetRows(store.History(code).Select(r => new Row(r.Day, r.Published, code, name, r.PerEuro)));
            return;
        }

        if (CellValues.Date(_day.Text) is not { } day)
        {
            _grid.SetRows([]);
            _table.Text = "Въведете датата като 01.01.2026.";
            return;
        }

        var published = store.TableOf(day);
        _grid.SetRows(store.RatesOf(day).Select(r => new Row(day, published!.Value, r.Row.Code, r.Row.Name, r.PerEuro)));
        if (published == null) _table.Text = "за този ден няма изтеглен курс";
        else if (published != day) _table.Text = $"важи курсът от {Fmt.Date(published)}";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        TakeFrom();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _rates.Changed -= RefreshState;
            _rates.Arrived -= RefreshRates;
        }

        base.Dispose(disposing);
    }
}
