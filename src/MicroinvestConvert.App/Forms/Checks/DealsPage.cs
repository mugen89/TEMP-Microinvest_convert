using System.Globalization;
using MicroinvestConvert.App.Ui;
using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Nomenclature;

namespace MicroinvestConvert.App.Forms.Checks;

/// <summary>
/// „Вид сделка": the totals of every deal type – what the ledgers will show – and the documents
/// behind them, where the type can be changed.
/// </summary>
internal sealed class DealsPage : CheckPage
{
    private sealed record Total(DealType Deal, int Documents, decimal Base, decimal Vat)
    {
        public decimal Gross => Base + Vat;
    }

    /// <summary>The part of a document with one deal type.</summary>
    private sealed record Part(OperationDocument Document, int Deal, string Accounts, decimal Base, decimal Vat, string Why, bool ByHand);

    private const string AutoDeal = "авто";

    private readonly Grid<Total> _totals = new();
    private readonly Grid<Part> _parts = new();
    private readonly Label _heading = Make.Label("", font: Theme.Bold);
    private readonly ComboBox _deal = Make.Combo(300);
    private readonly List<Button> _forSelection = [];
    private bool _refreshing;

    public override string Title => "Вид сделка";

    public DealsPage(ICheckHost host) : base(host)
    {
        bool sales = !Batch.Kind.IsPurchaseSide();

        _totals.Use(Palette);
        _totals.ReadOnly = true;
        _totals.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _totals.SetHeights(25, 30);
        _totals.AddColumn(new() { Header = "Вид сделка (кол. N)", Width = 330, Fill = true, Get = t => t.Deal.Label, SortKey = t => t.Deal.Code });
        _totals.AddColumn(new() { Header = "Къде влиза", Width = 190, Get = t => t.Deal.LedgerText });
        if (sales) _totals.AddColumn(new() { Header = "VIES", Width = 60, Center = true, Get = t => t.Deal.Vies.ToString(CultureInfo.InvariantCulture) });
        _totals.AddColumn(new() { Header = "Документи", Width = 100, Right = true, Get = t => t.Documents.ToString(CultureInfo.InvariantCulture), SortKey = t => t.Documents });
        MoneyTotal("Основа", t => t.Base);
        MoneyTotal("ДДС", t => t.Vat);
        MoneyTotal("Общо", t => t.Gross);
        _totals.CurrentCellChanged += (_, _) => { if (!_refreshing) ShowParts(); };
        _totals.SortRequested += RefreshRows;

        _parts.Use(Palette);
        _parts.SetHeights(25, 30);
        _parts.AddColumn(new() { Header = "№", Width = 44, Right = true, Get = p => p.Document.Index });
        _parts.AddColumn(new() { Header = "Тип", Width = 56, Get = p => p.Document.DocType });
        _parts.AddColumn(new() { Header = "Документ №", Width = 112, Get = p => p.Document.Number });
        _parts.AddColumn(new() { Header = "Дата", Width = 84, Get = p => Fmt.Date(p.Document.Date), SortKey = p => p.Document.Date });
        _parts.AddColumn(new() { Header = "Контрагент", Width = 190, Get = p => p.Document.PartnerName });
        _parts.AddColumn(new() { Header = "ДДС №", Width = 112, Get = p => p.Document.VatNumber });
        _parts.AddColumn(new() { Header = "Сметки", Width = 130, Get = p => p.Accounts });
        MoneyPart("Основа", p => p.Base);
        MoneyPart("ДДС", p => p.Vat);
        _parts.AddColumn(new()
        {
            Header = "✎ Вид", Width = 76, Center = true, Tip = "Друг вид сделка за тази част на документа",
            Items = [AutoDeal, .. Choices.Select(d => d.Code.ToString(CultureInfo.InvariantCulture))],
            ItemLabel = v => int.TryParse(v, out int code) ? DealTypes.Get(code).Label : "автоматично, по правилата",
            Get = p => p.Deal.ToString(CultureInfo.InvariantCulture),
            Set = (p, v) => Change(p, v == AutoDeal ? "" : v),
        });
        _parts.AddColumn(new() { Header = "Защо", Width = 240, Fill = true, Get = p => p.ByHand ? "зададен ръчно" : p.Why });
        _parts.StyleCell = (p, column, style) =>
        {
            if (column == 9)
            {
                style.Font = Theme.Bold;
                style.BackColor = Palette.Soft;
            }

            if (p.ByHand) style.ForeColor = Theme.EditedInk;
        };
        _parts.Edited += rows => Host.Edited(rows.Select(p => p.Document).Distinct().ToList());
        _parts.RowActivated += p => Host.Reveal(p.Document);
        _parts.SelectionChanged += (_, _) => RefreshActions();
        _parts.SortRequested += ShowParts;

        _deal.Items.Add("автоматично");
        _deal.Items.AddRange([.. Choices.Select(d => (object)d.Label)]);
        _deal.SelectedIndex = 0;
        var set = MakeButton("Задай на избраните", SetDeal);
        var all = MakeButton("Избери всички", () => { _parts.SelectAll(); _parts.Focus(); }, look: ButtonLook.Quiet);
        _forSelection.Add(set);
        _heading.Margin = new Padding(8, 8, 8, 3);
        var actions = Make.Flow(_heading, _deal, set, all);
        actions.Margin = new Padding(0, 4, 0, 2);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 6, BackColor = Theme.Window };
        split.Panel1.Controls.Add(_totals);
        split.Panel2.Controls.Add(Rows(1, actions, _parts));
        split.HandleCreated += (_, _) => BeginInvoke(() => { if (split.Height > 260) split.SplitterDistance = (int)(split.Height * 0.34); });
        Controls.Add(split);
        EndLayout();
    }

    private IEnumerable<DealType> Choices => DealTypes.ChoicesFor(Batch.Kind.IsPurchaseSide() ? DealSide.Purchase : DealSide.Sale);

    private void MoneyTotal(string header, Func<Total, decimal> value) =>
        _totals.AddColumn(new() { Header = header, Width = 104, Right = true, Get = t => Fmt.Money(value(t)), SortKey = t => value(t) });

    private void MoneyPart(string header, Func<Part, decimal> value) =>
        _parts.AddColumn(new() { Header = header, Width = 88, Right = true, Get = p => Fmt.Money(value(p)), SortKey = p => value(p) });

    // ----- rows -----

    private IEnumerable<Part> Parts() => Batch.Documents.Where(d => d.WillExport).SelectMany(d => d.Segments
        .GroupBy(s => s.DealType)
        .Select(g => new Part(d, g.Key,
            string.Join(", ", g.Where(s => !s.IsVat).Select(s => s.Account).Distinct()),
            g.Where(s => !s.IsVat).Sum(s => s.Amount), g.Where(s => s.IsVat).Sum(s => s.Amount),
            g.SelectMany(s => s.Lines).Select(l => l.Why).FirstOrDefault() ?? "",
            g.SelectMany(s => s.Lines).Any(l => l.DealTypeOverride != null))));

    public override void RefreshRows()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            int? current = _totals.Current?.Deal.Code;
            var totals = Parts().GroupBy(p => p.Deal).OrderBy(g => g.Key)
                .Select(g => new Total(DealTypes.Get(g.Key), g.Count(), g.Sum(p => p.Base), g.Sum(p => p.Vat))).ToList();
            _totals.SetRows(totals);
            if (totals.Find(t => t.Deal.Code == current) is { } again) _totals.GoTo(again);
        }
        finally
        {
            _refreshing = false;
        }

        ShowParts();
    }

    private void ShowParts()
    {
        int? deal = _totals.Current?.Deal.Code;
        var parts = Parts().Where(p => p.Deal == deal).ToList();
        _heading.Text = deal is { } code ? $"Вид {code}: {Fmt.Documents(parts.Count)}" : "Няма документи за запис";
        _parts.SetRows(parts);
        RefreshActions();
    }

    private void RefreshActions()
    {
        bool any = _parts.SelectedItems().Count > 0;
        foreach (var button in _forSelection) button.Enabled = any;
    }

    // ----- changing the type -----

    private static string? Change(Part part, string deal)
    {
        string error = "";
        bool ok = part.Document.Lines.Where(l => l.DealType == part.Deal).ToList()
            .TrueForAll(l => l.TrySet(LineField.DealType, deal, out error));
        return ok ? null : error;
    }

    private void SetDeal()
    {
        var chosen = _parts.SelectedItems();
        if (chosen.Count == 0) return;
        foreach (var part in chosen) Change(part, _deal.SelectedIndex <= 0 ? "" : _deal.Text);
        Host.Edited(chosen.Select(p => p.Document).Distinct().ToList());
    }
}
