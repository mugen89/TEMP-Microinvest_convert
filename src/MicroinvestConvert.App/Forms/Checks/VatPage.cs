using MicroinvestConvert.App.Ui;
using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Nomenclature;
using MicroinvestConvert.Core.Rules;
using MicroinvestConvert.Core.Settings;

namespace MicroinvestConvert.App.Forms.Checks;

/// <summary>
/// „ДДС": the VAT of every document against its rate, the rounding and the allowed difference,
/// and the corrections.
/// </summary>
internal sealed class VatPage : CheckPage
{
    public const string PageTitle = "ДДС";

    private static readonly (VatRounding Value, string Label)[] Roundings =
    [
        (VatRounding.HalfUp, "Математическо (0,005 → 0,01)"),
        (VatRounding.HalfEven, "Банково (0,005 → 0,00; 0,015 → 0,02)"),
        (VatRounding.Truncate, "Отрязване (0,009 → 0,00)"),
    ];

    private readonly Grid<VatGroup> _grid = new();
    private readonly ComboBox _rounding = Make.Combo(250);
    private readonly NumericUpDown _perDocument, _perLine;
    private readonly ComboBox _show = Make.Combo(190, "Само с разлика", "Всички");
    private readonly Label _summary = Make.Label("", font: Theme.Bold);
    private readonly List<Button> _actions = [];
    private bool _refreshing;

    public override string Title => PageTitle;
    public override int Open => OpenQuestions(Batch);

    /// <summary>How many VAT questions nobody has answered yet.</summary>
    public static int OpenQuestions(OperationBatch batch) => batch.Documents.Where(d => d.WillExport).SelectMany(d => d.VatGroups).Count(IsOpen);

    private static bool IsQuestion(VatGroup g) =>
        g.Bucket != LedgerBucket.PurchasePartialCredit &&
        (g.Status == VatStatus.Unresolved || (g.Status == VatStatus.Zero && g.DeclaredRate > 0m));

    private static bool IsDecided(VatGroup g) => g.Lines.All(l => l.DealTypeOverride != null);

    // Answered here, ticked off among all the checks, or not asked at all.
    private static bool IsOpen(VatGroup g) => IsQuestion(g) && !IsDecided(g) && g.Document.Issues.Any(i =>
        i.IsOpen && i.Code is IssueCode.VatUnresolved or IssueCode.VatZeroButDeclared or IssueCode.VatMismatch);

    public VatPage(ICheckHost host) : base(host)
    {
        _perDocument = Make.Cents(0m, 10m);
        _perLine = Make.Cents(0m, 1m);
        _rounding.Items.AddRange(Roundings.Select(r => (object)r.Label).ToArray());
        _show.SelectedIndex = 0;
        ShowSettings();

        _rounding.SelectedIndexChanged += (_, _) => SettingsEdited();
        _perDocument.ValueChanged += (_, _) => SettingsEdited();
        _perLine.ValueChanged += (_, _) => SettingsEdited();
        _show.SelectedIndexChanged += (_, _) => RefreshRows();

        var options = Make.Flow(
            Make.Label("Закръгляне:"), _rounding, Make.Gap(10),
            Make.Label("Допустима разлика за документ:"), _perDocument, Make.Gap(6),
            Make.Label("за отделен ред:"), _perLine, Make.Gap(10),
            Make.Label("Показвай:"), _show);
        options.Padding = new Padding(6, 6, 6, 2);

        var note = Note(Batch.Kind == OperationKind.Purchase
            ? "ДДС, който е 20% или 9% от основата (с допустимата разлика), е вид сделка 2. Без ДДС – 3. Когато не отговаря на никоя ставка – 4, докато не решите друго."
            : "Видът сделка идва от колоната на дневника. Тук се сравнява дали ДДС отговаря на 20% и 9%.");

        BuildGrid();
        Controls.Add(Rows(2, options, note, _grid, BuildActions()));
        EndLayout();
    }

    private void BuildGrid()
    {
        _grid.Use(Palette);
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
            else if (IsOpen(g)) style.BackColor = Theme.WarningBack;
        };
        _grid.SelectionChanged += (_, _) => RefreshActions();
        _grid.SortRequested += RefreshRows;
        _grid.RowActivated += g => Host.Reveal(g.Document);
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

    private Control BuildActions()
    {
        var buttons = new List<Control>();

        void Add(string text, string tip, Func<VatGroup, bool> act)
        {
            var button = MakeButton(text, () => Act(act), tip);
            _actions.Add(button);
            buttons.Add(button);
        }

        if (Batch.Kind == OperationKind.Purchase)
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

        _summary.Margin = new Padding(6, 9, 12, 3);
        var bar = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Padding = new Padding(5, 4, 5, 4) };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.Controls.Add(Make.Flow([.. buttons]), 0, 0);
        bar.Controls.Add(_summary, 1, 0);
        return bar;
    }

    private void Act(Func<VatGroup, bool> act)
    {
        var chosen = _grid.SelectedItems();
        if (chosen.Count == 0) return;

        var done = chosen.Where(act).ToList();
        if (done.Count > 0) Host.Edited(done.Select(g => g.Document).Distinct().ToList());
        if (done.Count < chosen.Count)
            Dialogs.Info(FindForm(), "Не е приложимо за всички избрани",
                $"Приложено за {done.Count} от {chosen.Count}. При останалите сумите не позволяват това действие.");
    }

    // They may have been changed in the window of another module.
    private void ShowSettings()
    {
        var general = Host.App.Settings.General;
        _refreshing = true;
        _rounding.SelectedIndex = Array.FindIndex(Roundings, r => r.Value == general.Rounding);
        _perDocument.Value = general.TolerancePerDocument;
        _perLine.Value = general.TolerancePerLine;
        _refreshing = false;
    }

    private void SettingsEdited()
    {
        if (_refreshing) return;
        var general = Host.App.Settings.General;
        general.Rounding = Roundings[Math.Max(0, _rounding.SelectedIndex)].Value;
        general.TolerancePerDocument = _perDocument.Value;
        general.TolerancePerLine = _perLine.Value;
        Host.App.Save();
        Host.SettingsChanged();
    }

    public override void RefreshRows()
    {
        ShowSettings();
        var groups = Batch.Documents.SelectMany(d => d.VatGroups).ToList();
        _grid.SetRows(_show.SelectedIndex == 1 ? groups : groups.Where(g => g.NeedsLook || IsDecided(g)));

        int questions = OpenQuestions(Batch);
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
