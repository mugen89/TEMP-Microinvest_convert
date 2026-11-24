using MicroinvestConvert.App.Ui;
using MicroinvestConvert.Core.Exchange;
using MicroinvestConvert.Core.Output;
using MicroinvestConvert.Core.Source;
using MicroinvestConvert.Core.Values;
using MicroinvestConvert.Data;

namespace MicroinvestConvert.App.Forms;

/// <summary>
/// „Валутни курсове": from which day they are kept, whether they come by themselves, the button
/// that brings them now, and the rates there are – of a day or of a currency, also as a workbook.
/// </summary>
internal sealed class RatesForm : AppForm
{
    private static readonly (RateView View, string Label)[] Views =
    [
        (RateView.Day, "Всички валути за дата"),
        (RateView.Currency, "Избрана валута – всички дни"),
        (RateView.ThreeMonths, "Избрана валута – 3 месеца назад"),
        (RateView.ThreeCalendarMonths, "Избрана валута – 3 календарни месеца"),
    ];

    private readonly AppState _app;
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

    private readonly ComboBox _view = Make.Combo(270, [.. Views.Select(v => v.Label)]);
    private readonly TextBox _day = Make.TextBox(96, "дд.мм.гггг");
    private readonly ComboBox _currency = Make.Combo(230);
    private readonly FlatButton _export = new("Експорт в Excel…");
    private readonly Label _table = Make.Label("", Theme.Muted);
    private readonly Grid<RateLine> _grid = new();
    private FlowLayoutPanel _dayPart = null!, _currencyPart = null!;
    private IReadOnlyList<(string Code, string Name)> _currencies = [];
    private bool _filling;

    // What is shown, in words: for the workbook and its name.
    private string _title = "", _period = "", _fileName = "";

    /// <param name="app">What the windows share.</param>
    /// <param name="needed">Days that open documents wait for.</param>
    public RatesForm(AppState app, IReadOnlyList<DateTime>? needed = null)
    {
        _app = app;
        _rates = app.Rates;
        _needed = needed ?? [];

        Text = "Валутни курсове";
        AsDialog(900, 640, 820, 460);

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
        ShowNewest();
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
        _view.SelectedIndexChanged += (_, _) => RefreshRates();
        _currency.SelectedIndexChanged += (_, _) => RefreshRates();
        _export.Click += (_, _) => Export();

        _dayPart = Make.Flow(Make.Label("Дата:"), back, _day, forward);
        _currencyPart = Make.Flow(Make.Label("Валута:"), _currency);
        _table.Margin = new Padding(3, 1, 3, 4);

        var filter = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 2, Margin = new Padding(9, 4, 9, 0) };
        filter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filter.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filter.Controls.Add(Make.Flow(Make.Label("Показвай:"), _view, Make.Gap(8), _dayPart, _currencyPart), 0, 0);
        filter.Controls.Add(_export, 1, 0);
        filter.Controls.Add(_table, 0, 1);
        filter.SetColumnSpan(_table, 2);
        return filter;

        FlatButton Step(string text, int days)
        {
            var button = new FlatButton(text, ButtonLook.Quiet);
            button.Click += (_, _) =>
            {
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
        _grid.AddColumn(new() { Header = "Евро за единица", Width = 136, Right = true, Get = r => Fmt.Rate(1m / r.PerEuro), SortKey = r => 1m / r.PerEuro, Tip = "Колко евро е една единица валута – така е курсът в кол. A на файла за Делта Pro" });
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

    // The newest day, as the window was left the last time.
    private void ShowNewest()
    {
        _filling = true;   // everything at once
        var ranges = _rates.Ranges;
        _day.Text = Fmt.Date(ranges.Count > 0 ? ranges[^1].To : DateTime.Today);
        _view.SelectedIndex = Math.Max(0, Array.FindIndex(Views, v => v.View == _app.RatesShown.View));
        _filling = false;
        RefreshRates();
    }

    private RateView View => Views[Math.Max(0, _view.SelectedIndex)].View;

    private void RefreshRates()
    {
        if (IsDisposed || _filling) return;
        _filling = true;
        try
        {
            _dayPart.Visible = View == RateView.Day;
            _currencyPart.Visible = View != RateView.Day;
            FillCurrencies();
            if (View == RateView.Day) ShowDay();
            else ShowCurrency();
        }
        catch (DatabaseException ex)
        {
            ShowLines([], ex.Message);
        }
        finally
        {
            _filling = false;
        }
    }

    private void FillCurrencies()
    {
        string chosen = _currency.SelectedIndex >= 0 ? _currencies[_currency.SelectedIndex].Code : _app.RatesShown.Currency;
        var all = _rates.Store?.Currencies() ?? [];
        if (!all.SequenceEqual(_currencies))
        {
            _currencies = all;
            _currency.Items.Clear();
            _currency.Items.AddRange([.. all.Select(c => (object)$"{c.Code} – {c.Name}")]);
            int at = all.ToList().FindIndex(c => c.Code == chosen);
            _currency.SelectedIndex = at >= 0 ? at : all.Count > 0 ? 0 : -1;
        }

        _app.RatesShown = (View, _currency.SelectedIndex >= 0 ? _currencies[_currency.SelectedIndex].Code : chosen);
    }

    private void ShowDay()
    {
        if (CellValues.Date(_day.Text) is not { } day)
        {
            ShowLines([], "Въведете датата като 01.01.2026.");
            return;
        }

        var published = _rates.Store?.TableOf(day);
        var lines = _rates.Store?.RatesOf(day).Select(r => new RateLine(day, r.Row.Code, r.Row.Name, r.PerEuro, published!.Value)) ?? [];
        _title = $"Валутни курсове за {Fmt.Date(day)}";
        _period = published is { } of && of != day ? $"Важи курсът от {Fmt.Date(of)}." : "";
        _fileName = $"Курсове {Fmt.Date(day)}";
        ShowLines(lines, published == null ? "За този ден няма изтеглен курс." : _period);
    }

    private void ShowCurrency()
    {
        if (_rates.Store is not { } store || _currency.SelectedIndex < 0)
        {
            ShowLines([], "Няма изтеглени курсове.");
            return;
        }

        var (code, name) = _currencies[_currency.SelectedIndex];
        var history = store.History(code);   // the newest first
        if (history.Count == 0)
        {
            ShowLines([], "Няма изтеглени курсове.");
            return;
        }

        var period = RateCalendar.PeriodOf(View, history[^1].Day, history[0].Day);
        var lines = history.Where(r => r.Day >= period.From).Select(r => new RateLine(r.Day, code, name, r.PerEuro, r.Published)).ToList();
        string dates = $"{Fmt.Date(period.From)} – {Fmt.Date(period.To)}", all = period.Days == 1 ? "1 ден" : $"{period.Days} дни";
        _title = $"Валутни курсове – {code} ({name})";
        _period = $"{dates}: " + (lines.Count == period.Days ? all : $"{lines.Count} от {all}") + (View switch
        {
            RateView.ThreeMonths => ", 3 месеца назад от последния курс.",
            RateView.ThreeCalendarMonths => ", 3 календарни месеца до последния курс.",
            _ => ".",
        });
        _fileName = $"Курсове {code} {Fmt.Date(period.From)}-{Fmt.Date(period.To)}";
        ShowLines(lines, _period);
    }

    private void ShowLines(IEnumerable<RateLine> lines, string text)
    {
        _grid.SetRows(lines);
        _table.Text = text;
        _export.Enabled = _grid.Items.Count > 0;
    }

    // What is shown, in the order it is shown.
    private void Export()
    {
        if (ExcelFiles.PickToSave(this, "Експорт на курсовете", _fileName + RatesWorkbook.Extension) is not { } path) return;

        // Added, not changed: the dates in the name end like an extension.
        if (!path.EndsWith(RatesWorkbook.Extension, StringComparison.OrdinalIgnoreCase)) path += RatesWorkbook.Extension;
        string[] notes =
        [
            _period,
            $"Източник: {_rates.SourceName} – курсовете на БНБ, от 2026 г. референтните на ЕЦБ. Записано на {DateTime.Now:dd.MM.yyyy HH:mm} от {AppInfo.Name} {AppInfo.VersionText}.",
            "„За 1 евро“ са единиците валута за едно евро. „Евро за единица“ е обратното – курсът в кол. A на файла за Делта Pro.",
        ];
        try
        {
            RatesWorkbook.Write(path, _title, notes.Where(n => n.Length > 0).ToList(), _grid.Items);
        }
        catch (ConvertException ex)
        {
            Dialogs.Error(this, "Експортът не успя", ex.Message);
            return;
        }

        if (_app.Settings.OpenAfterSave) ExcelFiles.Open(this, path);
        else _table.Text = "Записано: " + path;
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
