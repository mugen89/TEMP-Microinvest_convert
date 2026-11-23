using MicroinvestConvert.App.Ui;
using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Nomenclature;
using MicroinvestConvert.Core.Rules;
using MicroinvestConvert.Core.Settings;

namespace MicroinvestConvert.App.Forms;

/// <summary>
/// „Проверка на ДДС": the VAT of every document against its rate, the rounding and the
/// allowed difference, and the corrections.
/// </summary>
internal sealed class VatCheckForm : AppForm
{
    private static readonly (VatRounding Value, string Label)[] Roundings =
    [
        (VatRounding.HalfUp, "Математическо (0,005 → 0,01)"),
        (VatRounding.HalfEven, "Банково (0,005 → 0,00; 0,015 → 0,02)"),
        (VatRounding.Truncate, "Отрязване (0,009 → 0,00)"),
    ];

    private readonly AppState _app;
    private readonly OperationBatch _batch;
    private readonly Action _reprocess;
    private readonly bool _purchase;
    private readonly Grid<VatGroup> _grid = new();
    private readonly ComboBox _rounding = Make.Combo(250);
    private readonly NumericUpDown _perDocument, _perLine;
    private readonly ComboBox _show = Make.Combo(190, "Само с разлика", "Всички");
    private readonly Label _summary = Make.Label("", font: Theme.Bold);
    private readonly List<Button> _actions = [];

    /// <summary>A document was corrected here (not only looked at).</summary>
    public bool EditsMade { get; private set; }

    /// <summary>How many VAT questions nobody has answered yet.</summary>
    public static int OpenQuestions(OperationBatch batch) => batch.Documents
        .Where(d => d.WillExport)
        .SelectMany(d => d.VatGroups)
        .Count(g => IsQuestion(g) && !IsDecided(g));

    private static bool IsQuestion(VatGroup g) =>
        g.Bucket != LedgerBucket.PurchasePartialCredit &&
        (g.Status == VatStatus.Unresolved || (g.Status == VatStatus.Zero && g.DeclaredRate > 0m));

    private static bool IsDecided(VatGroup g) => g.Lines.All(l => l.DealTypeOverride != null);

    public VatCheckForm(AppState app, OperationBatch batch, Action reprocess)
    {
        _app = app;
        _batch = batch;
        _reprocess = reprocess;
        _purchase = batch.Kind == OperationKind.Purchase;
        var palette = Palette.For(batch.Kind);

        Text = "Проверка на ДДС";
        AsDialog(1230, 620, 1060, 440);

        var general = app.Settings.General;
        _perDocument = Make.Cents(general.TolerancePerDocument, 10m);
        _perLine = Make.Cents(general.TolerancePerLine, 1m);
        _rounding.Items.AddRange(Roundings.Select(r => (object)r.Label).ToArray());
        _rounding.SelectedIndex = Array.FindIndex(Roundings, r => r.Value == general.Rounding);
        _show.SelectedIndex = 0;

        _rounding.SelectedIndexChanged += (_, _) => SettingsChanged();
        _perDocument.ValueChanged += (_, _) => SettingsChanged();
        _perLine.ValueChanged += (_, _) => SettingsChanged();
        _show.SelectedIndexChanged += (_, _) => RefreshRows();

        var options = Make.Flow(
            Make.Label("Закръгляне:"), _rounding, Make.Gap(10),
            Make.Label("Допустима разлика за документ:"), _perDocument, Make.Gap(6),
            Make.Label("за отделен ред:"), _perLine, Make.Gap(10),
            Make.Label("Показвай:"), _show);
        options.Padding = new Padding(10, 6, 10, 4);

        string rule = _purchase
            ? "ДДС, който е 20% или 9% от основата (с допустимата разлика), е вид сделка 2. Без ДДС – 3. Когато не отговаря на никоя ставка – 4, докато не решите друго."
            : "Видът сделка идва от колоната на дневника. Тук се сравнява дали ДДС отговаря на 20% и 9%.";
        var note = Make.Text(rule, Theme.Muted, 1120);
        note.Margin = new Padding(13, 0, 10, 4);

        BuildGrid(palette);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(options, 0, 0);
        root.Controls.Add(note, 0, 1);
        root.Controls.Add(_grid, 0, 2);
        root.Controls.Add(BuildActions(palette), 0, 3);
        Controls.Add(root);

        RefreshRows();
        EndLayout();
    }

    private void BuildGrid(Palette palette)
    {
        _grid.Use(palette);
        _grid.ReadOnly = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.SetHeights(25, 30);

        _grid.AddColumn(new() { Header = "Документ №", Width = 112, Get = g => g.Document.Number });
        _grid.AddColumn(new() { Header = "Дата", Width = 84, Get = g => Fmt.Date(g.Document.Date), SortKey = g => g.Document.Date });
        _grid.AddColumn(new() { Header = "Контрагент", Width = 130, Fill = true, Get = g => g.Document.PartnerName });
        _grid.AddColumn(new() { Header = "Колона", Width = 138, Get = g => LedgerBuckets.Title(g.Bucket) });
        Money("Основа", g => g.Base);
        Money("ДДС", g => g.Vat);
        _grid.AddColumn(new() { Header = "Ставка", Width = 78, Right = true, Get = g => Percent(g.DeclaredRate), Tip = "Ставката във файла" });
        _grid.AddColumn(new() { Header = "Излиза", Width = 78, Right = true, Get = g => Percent(g.Percent), SortKey = g => g.Percent, Tip = "ДДС като процент от основата" });
        Money("Очакван", g => g.Expected);
        Money("Разлика", g => g.Difference);
        _grid.AddColumn(new() { Header = "Резултат", Width = 160, Get = Result });
        _grid.AddColumn(new() { Header = "Вид", Width = 56, Center = true, Get = g => string.Join(";", g.Lines.Select(l => l.DealType).Distinct()) });

        _grid.StyleCell = (g, _, style) =>
        {
            if (IsDecided(g)) style.ForeColor = Theme.EditedInk;
            else if (IsQuestion(g)) style.BackColor = Theme.WarningBack;
        };
        _grid.SelectionChanged += (_, _) => RefreshActions();
        _grid.SortRequested += RefreshRows;
    }

    private void Money(string header, Func<VatGroup, decimal> value) =>
        _grid.AddColumn(new() { Header = header, Width = 84, Right = true, Get = g => Fmt.Money(value(g)), SortKey = g => value(g) });

    private static string Percent(decimal? value) => value is { } v ? v.ToString("0.##", Fmt.Bulgarian) + "%" : "";

    private static string Result(VatGroup g)
    {
        if (IsDecided(g)) return "решено ръчно";
        return g.Status switch
        {
            VatStatus.Exact => "точно " + Percent(g.Rate),
            VatStatus.WithinTolerance => $"{Percent(g.Rate)}, в допустимата",
            VatStatus.Zero => g.DeclaredRate > 0m ? "няма ДДС" : "0%",
            VatStatus.Empty => "нулев",
            _ => "не отговаря",
        };
    }

    private Control BuildActions(Palette palette)
    {
        var buttons = new List<Control>();

        void Add(string text, string tip, Func<VatGroup, bool> act)
        {
            var button = new FlatButton(text);
            button.Use(palette);
            button.Click += (_, _) => Act(act);
            new ToolTip { AutoPopDelay = 15000 }.SetToolTip(button, tip);
            _actions.Add(button);
            buttons.Add(button);
        }

        if (_purchase)
        {
            Add("Приеми – вид 2", "Сумите остават, видът сделка е 2 (пълен данъчен кредит).",
                g => VatCorrections.SetDeal(g, DealTypes.PurchaseFullCredit));
            Add("Коригирай ДДС", "ДДС става точно по ставката; общата сума не се променя.", VatCorrections.CorrectVat);
            Add("Раздели: със и без ДДС", "Частта, на която отговаря ДДС, става вид 2, а остатъкът – вид 3.", VatCorrections.Split);
            Add("Вид 3", "Без данъчен кредит: ДДС влиза в разхода.",
                g => VatCorrections.SetDeal(g, DealTypes.PurchaseNoCredit));
            Add("Вид 4", "Частичен данъчен кредит.",
                g => VatCorrections.SetDeal(g, DealTypes.PurchasePartialCredit));
        }
        else
        {
            Add("Приеми така", "Сумите и видът сделка остават.", VatCorrections.AcceptAsIs);
            Add("Коригирай ДДС", "ДДС става точно по ставката; общата сума не се променя.", VatCorrections.CorrectVat);
        }

        Add("Върни от файла", "Редовете на документа – както са във файла, по правилата.", g =>
        {
            g.Document.RevertLines();
            return true;
        });

        var close = new FlatButton("Затвори", ButtonLook.Primary);
        close.Use(palette);
        close.Click += (_, _) => Close();
        CancelButton = close;

        var left = Make.Flow([.. buttons]);
        _summary.Margin = new Padding(6, 9, 12, 3);

        var bar = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, Padding = new Padding(8, 6, 8, 8), BackColor = Theme.Surface };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.Controls.Add(left, 0, 0);
        bar.Controls.Add(_summary, 1, 0);
        bar.Controls.Add(close, 2, 0);
        return bar;
    }

    // ----- actions -----

    private void Act(Func<VatGroup, bool> act)
    {
        var chosen = _grid.SelectedItems();
        if (chosen.Count == 0) return;

        int done = chosen.Count(act);
        if (done > 0)
        {
            EditsMade = true;
            _reprocess();
            RefreshRows();
        }

        if (done < chosen.Count)
            Dialogs.Info(this, "Не е приложимо за всички избрани",
                $"Приложено за {done} от {chosen.Count}. При останалите сумите не позволяват това действие.");
    }

    // ----- refresh -----

    private void SettingsChanged()
    {
        var general = _app.Settings.General;
        general.Rounding = Roundings[Math.Max(0, _rounding.SelectedIndex)].Value;
        general.TolerancePerDocument = _perDocument.Value;
        general.TolerancePerLine = _perLine.Value;
        _app.Save();
        _reprocess();
        RefreshRows();
    }

    private void RefreshRows()
    {
        var groups = _batch.Documents.SelectMany(d => d.VatGroups).ToList();
        _grid.SetRows(_show.SelectedIndex == 1 ? groups : groups.Where(g => g.NeedsLook || IsDecided(g)));

        int questions = OpenQuestions(_batch);
        int cents = groups.Count(g => g.Status == VatStatus.WithinTolerance && !IsDecided(g));
        _summary.Text = questions == 0
            ? cents == 0 ? "Няма разлики" : $"За решаване: няма   ·   в допустимата разлика: {cents}"
            : $"За решаване: {questions}   ·   в допустимата разлика: {cents}";
        _summary.ForeColor = questions > 0 ? Theme.WarningInk : Theme.OkInk;
        RefreshActions();
    }

    private void RefreshActions()
    {
        bool any = _grid.SelectedItems().Count > 0;
        foreach (var button in _actions) button.Enabled = any;
    }
}
