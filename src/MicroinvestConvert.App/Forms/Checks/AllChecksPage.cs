using MicroinvestConvert.App.Ui;
using MicroinvestConvert.Core.Checks;
using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Nomenclature;
using MicroinvestConvert.Core.Rules;

namespace MicroinvestConvert.App.Forms.Checks;

/// <summary>
/// Every check of the module with a tick – on or off – and how much it found; beside it what
/// the chosen one found, with the fields that answer it.
/// </summary>
internal sealed class AllChecksPage : CheckPage
{
    private sealed class CheckRow
    {
        public required CheckInfo Info { get; init; }
        public required int Order { get; init; }

        /// <summary>Waiting for somebody.</summary>
        public int Open { get; set; }
        public int Total { get; set; }

        public string Count => Open > 0 ? Open < Total ? $"{Open} от {Total}" : Open.ToString() : Total > 0 ? $"({Total})" : "";
    }

    private sealed record Finding(OperationDocument Document, Issue Issue);

    private const string Pencil = "✎ ";

    private readonly List<CheckRow> _rows;
    private readonly Grid<CheckRow> _checks = new();
    private readonly CheckBox _onlyFound = Make.Check("Само с намерени");

    private readonly Label _heading = Make.Label("", font: Theme.Heading);
    private readonly Label _help = Make.Text("", Theme.Muted, 800);
    private readonly Label _message = Make.Text("", Theme.Ink, 800);
    private readonly Grid<Finding> _findings = new();
    private readonly Dictionary<DocField, int> _columnOf = [];
    private readonly Dictionary<DocField, string> _headerOf = [];
    private int _tickColumn, _accountColumn, _dealColumn;

    private readonly FlatButton _tick, _untick, _setDeal;
    private readonly ComboBox _deal = Make.Combo(250);
    private readonly List<Button> _forSelection = [];

    private bool _refreshing;

    /// <summary>The last change was more than a tick „прегледан".</summary>
    private bool _corrected;

    public override string Title => "Всички проверки";
    public override int Open => Batch.Documents.Count(d => d is { Export: true, SkippedBySetting: false } && d.Issues.Any(i => i.IsOpen));

    public AllChecksPage(ICheckHost host) : base(host)
    {
        _rows = CheckCatalog.For(Batch.Kind).Select((info, i) => new CheckRow { Info = info, Order = i }).ToList();

        BuildChecks();
        BuildFindings();

        _tick = MakeButton("Отбележи като прегледани", () => Tick(true), "Избраните са видени и са наред – не се броят повече за преглед.");
        _untick = MakeButton("Върни за преглед", () => Tick(false));
        _setDeal = MakeButton("Задай вид", SetDeal, "Видът сделка на всички редове на избраните документи.");
        var include = MakeButton("Включи във файла", () => Include(true), look: ButtonLook.Quiet);
        var exclude = MakeButton("Изключи от файла", () => Include(false), look: ButtonLook.Quiet);
        var show = MakeButton("Покажи документа", Reveal, "Отива на документа в прозореца на модула. Същото прави и двойно щракване.", ButtonLook.Quiet);
        var all = MakeButton("Избери всички", () => { _findings.SelectAll(); _findings.Focus(); }, look: ButtonLook.Quiet);
        _forSelection.AddRange([_tick, _untick, _setDeal, include, exclude, show]);

        _deal.Items.Add("автоматично");
        _deal.Items.AddRange([.. DealTypes.ChoicesFor(Batch.Kind.IsPurchaseSide() ? DealSide.Purchase : DealSide.Sale).Select(d => (object)d.Label)]);
        _deal.SelectedIndex = 0;

        _heading.Margin = new Padding(8, 8, 3, 0);
        _help.Margin = new Padding(8, 0, 8, 4);
        _message.Margin = new Padding(8, 5, 8, 2);
        var actions = Make.Flow(_tick, _untick, _deal, _setDeal, Make.Gap(6), include, exclude, show, all);
        actions.Margin = new Padding(5, 2, 3, 3);

        _onlyFound.CheckedChanged += (_, _) => RefreshRows();
        _onlyFound.Margin = new Padding(8, 6, 3, 4);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 6, BackColor = Theme.Window };
        split.Panel1.Controls.Add(Rows(1, _onlyFound, _checks));
        split.Panel2.Controls.Add(Rows(2, _heading, _help, _findings, _message, actions));
        split.HandleCreated += (_, _) => BeginInvoke(() =>
        {
            int width = LogicalToDeviceUnits(470);
            if (split.Width > width * 2) split.SplitterDistance = width;
        });
        Controls.Add(split);
        EndLayout();
    }

    // ----- the checks -----

    private void BuildChecks()
    {
        _checks.Use(Palette);
        _checks.SetHeights(26, 30);
        _checks.AddColumn(new()
        {
            Header = "✓", Width = 34, IsCheck = true, Tip = "Проверката е включена. Важи за всички прозорци и се запомня.",
            Get = r => !Host.Options.General.IsOff(r.Info.Code),
            Set = (r, v) =>
            {
                if (!r.Info.CanBeOff) return $"„{r.Info.Title}“ спира записа на документа и не може да се изключи.";
                Host.App.Settings.General.SetOff(r.Info.Code, v.Length == 0);
                return null;
            },
        });
        _checks.AddColumn(new()
        {
            Header = "", Width = 38, Center = true, SortKey = r => -(int)r.Info.Severity,
            Get = r => r.Info.Severity switch { IssueSeverity.Error => "✖", IssueSeverity.Warning => "⚠", _ => "i" },
        });
        _checks.AddColumn(new() { Header = "Проверка", Width = 150, Fill = true, Get = r => r.Info.Title });
        _checks.AddColumn(new() { Header = "Намерени", Width = 92, Right = true, Get = r => r.Count, SortKey = r => -(r.Open * 100000 + r.Total) });

        _checks.StyleCell = (row, column, style) =>
        {
            bool off = Host.Options.General.IsOff(row.Info.Code);
            if (row.Open > 0) style.BackColor = row.Info.Severity == IssueSeverity.Error ? Theme.ErrorBack : Theme.WarningBack;
            if (off || row.Total == 0) style.ForeColor = Theme.Excluded;
            if (column == 1 && !off && row.Total > 0)
                style.ForeColor = row.Info.Severity switch
                {
                    IssueSeverity.Error => Theme.ErrorInk,
                    IssueSeverity.Warning => Theme.WarningInk,
                    _ => Theme.Muted,
                };
            if (row.Open > 0 && column > 1) style.Font = Theme.Bold;
        };
        _checks.Edited += _ =>
        {
            Host.App.Save();
            Host.SettingsChanged();
        };
        _checks.CurrentCellChanged += (_, _) => { if (!_refreshing) ShowFindings(); };
        _checks.SortRequested += RefreshRows;
    }

    public override void RefreshRows()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var found = Batch.Documents.Where(d => d is { Export: true, SkippedBySetting: false })
                .SelectMany(d => d.Issues).ToLookup(i => i.Code);
            foreach (var row in _rows)
            {
                row.Total = found[row.Info.Code].Count();
                row.Open = found[row.Info.Code].Count(i => i.IsOpen);
            }

            _checks.SetRows(_rows.Where(r => !_onlyFound.Checked || r.Total > 0).OrderBy(Rank).ThenBy(r => r.Order));
        }
        finally
        {
            _refreshing = false;
        }

        ShowFindings();
    }

    // What waits comes first: errors, then warnings; then what only found something.
    private static int Rank(CheckRow row) =>
        row.Open > 0 ? row.Info.Severity == IssueSeverity.Error ? 0 : 1 : row.Total > 0 ? 2 : 3;

    // ----- what the chosen check found -----

    private void BuildFindings()
    {
        _findings.Use(Palette);
        _findings.SetHeights(26, 30);

        _tickColumn = _findings.AddColumn(new()
        {
            Header = "Видян", Width = 62, IsCheck = true, Tip = "Прегледан е и е наред – не се брои повече за преглед",
            Get = f => f.Issue.Reviewed,
            Set = (f, v) => { f.Document.SetReviewed(f.Issue, v.Length > 0); return null; },
        });
        _findings.AddColumn(new()
        {
            Header = "Запис", Width = 56, IsCheck = true, Tip = "Документът се записва във файла",
            Get = f => f.Document.Export,
            Set = (f, v) => { f.Document.Export = v.Length > 0; _corrected = true; return null; },
        });
        _findings.AddColumn(new() { Header = "№", Width = 44, Right = true, Get = f => f.Document.Index, Tip = "№ във файла" });

        Field(DocField.DocType, "Тип", 70, DocumentTypes.All.Select(t => t.ShortName).ToList(), v => DocumentTypes.Find(v)?.Label ?? v);
        Field(DocField.Number, "Документ №", 112);
        Field(DocField.Date, "Дата", 88);
        Field(DocField.Period, "Период", 92);
        Field(DocField.AccountingDate, "Сч. дата", 102);
        Field(DocField.PartnerName, "Контрагент", 170);
        Field(DocField.Eik, "ЕИК", 112);
        Field(DocField.VatNumber, "ДДС №", 122);
        Field(DocField.CounterAccount, "Сметка контрагент", 156);
        _accountColumn = _findings.AddColumn(new()
        {
            Header = Pencil + "Сметка", Width = 92, Tip = "Сметката на редовете без правилна сметка",
            Get = f => string.Join(", ", WithoutAccount(f.Document).Select(l => l.AccountKey).Distinct()),
            Set = (f, v) =>
            {
                foreach (var line in WithoutAccount(f.Document).ToList())
                {
                    line.TrySet(LineField.Account, v, out _);
                    line.TrySet(LineField.Analytic, "", out _);
                }

                _corrected = true;
                return null;
            },
        });
        Field(DocField.Payment, "Плащане", 104, PaymentMethods.All.Select(PaymentMethods.Label).ToList(), v => v.Length == 0 ? "(няма)" : v);
        Field(DocField.CashAccount, "Каса", 78);
        Field(DocField.Currency, "Валута", 92);
        Field(DocField.Reason, "Основание", 180);
        Field(DocField.Vies, "VIES", 76);

        var deals = DealTypes.ChoicesFor(Batch.Kind.IsPurchaseSide() ? DealSide.Purchase : DealSide.Sale).Select(d => d.Code.ToString()).ToList();
        _dealColumn = _findings.AddColumn(new()
        {
            Header = Pencil + "Вид", Width = 76, Center = true, Tip = "N · Вид сделка",
            Items = ["авто", .. deals], ItemLabel = v => int.TryParse(v, out int code) ? DealTypes.Get(code).Label : "автоматично, по правилата",
            Get = f => string.Join("; ", f.Document.DealTypesUsed),
            Set = (f, v) =>
            {
                string error = "";
                bool ok = f.Document.Lines.TrueForAll(l => l.TrySet(LineField.DealType, v == "авто" ? "" : v, out error));
                _corrected = true;
                return ok ? null : error;
            },
        });
        _findings.AddColumn(new() { Header = "Общо", Width = 92, Right = true, Get = f => Fmt.Money(f.Document.TotalGross), SortKey = f => f.Document.TotalGross });
        _findings.AddColumn(new() { Header = "Какво е намерено", Width = 220, Fill = true, Get = f => f.Issue.Message });

        _findings.StyleCell = (f, column, style) =>
        {
            if (f.Issue.Reviewed || !f.Document.Export) style.ForeColor = Theme.Excluded;
            if (!_findings.Columns[column].ReadOnly && column > 1) style.BackColor = Palette.Soft;
        };
        _findings.Edited += rows =>
        {
            var documents = rows.Select(r => r.Document).Distinct().ToList();
            bool corrected = _corrected;
            _corrected = false;
            if (corrected) Host.Edited(documents);
            else Host.Ticked(documents);
        };
        _findings.RowActivated += f => Host.Reveal(f.Document);
        _findings.SelectionChanged += (_, _) => RefreshActions();
        _findings.CurrentCellChanged += (_, _) => _message.Text = _findings.Current?.Issue.Message ?? "";
        _findings.SortRequested += ShowFindings;
    }

    private void Field(DocField field, string header, int width, IReadOnlyList<string>? items = null, Func<string, string>? label = null)
    {
        _headerOf[field] = header;
        _columnOf[field] = _findings.AddColumn(new()
        {
            Header = header, Width = width, Items = items, ItemLabel = label,
            Get = f => field == DocField.AccountingDate ? Fmt.Date(f.Document.AccountingDate) : f.Document.GetText(field),
            SortKey = field switch
            {
                DocField.Date => f => f.Document.Date,
                DocField.Period => f => f.Document.Period,
                DocField.AccountingDate => f => f.Document.AccountingDate,
                _ => null,
            },
            Set = (f, v) =>
            {
                if (!f.Document.TrySet(field, v, out string error)) return error;
                _corrected = true;
                return null;
            },
        });
    }

    private static IEnumerable<LedgerLine> WithoutAccount(OperationDocument doc) =>
        doc.Lines.Where(l => AccountCodes.Problem(l.AccountKey) != null);

    private CheckInfo? _shown;

    private void ShowFindings()
    {
        var check = _checks.Current?.Info;
        if (check != _shown)
        {
            _shown = check;
            _findings.SetRows([]);   // no current cell while its column may disappear
            Arrange(check);
        }

        _findings.SetRows(check == null
            ? []
            : Batch.Documents.Where(d => !d.SkippedBySetting).SelectMany(d => d.Issues.Where(i => i.Code == check.Code).Select(i => new Finding(d, i))));
        RefreshActions();
    }

    // The columns and buttons that answer this check; a pencil marks what can be typed in.
    private void Arrange(CheckInfo? check)
    {
        _heading.Text = check?.Title ?? "Няма намерени";
        _help.Text = check?.Help ?? "";
        if (check != null && !check.CanBeOff) _help.Text += " Документът не се записва, докато не се поправи.";

        DocField[] always = [DocField.Number, DocField.Date, DocField.PartnerName];
        foreach (var (field, column) in _columnOf)
        {
            bool answers = check?.Fields.Contains(field) == true;
            var view = _findings.Columns[column];
            view.Visible = answers || always.Contains(field);
            view.ReadOnly = !answers;
            view.HeaderText = answers ? Pencil + _headerOf[field] : _headerOf[field];
        }

        bool warning = check?.Severity == IssueSeverity.Warning;
        _findings.Columns[_tickColumn].Visible = warning;
        _findings.Columns[_accountColumn].Visible = check?.AsksAccount == true;
        _findings.Columns[_dealColumn].Visible = check?.AsksDealType == true;
        _tick.Visible = _untick.Visible = warning;
        _deal.Visible = _setDeal.Visible = check?.AsksDealType == true;
    }

    private void RefreshActions()
    {
        bool any = _findings.SelectedItems().Count > 0;
        foreach (var button in _forSelection) button.Enabled = any;
    }

    // ----- actions -----

    private void Tick(bool reviewed)
    {
        var chosen = _findings.SelectedItems();
        foreach (var f in chosen) f.Document.SetReviewed(f.Issue, reviewed);
        if (chosen.Count > 0) Host.Ticked(Documents(chosen));
    }

    private void SetDeal()
    {
        var chosen = _findings.SelectedItems();
        if (chosen.Count == 0) return;
        string value = _deal.SelectedIndex <= 0 ? "" : _deal.Text;
        foreach (var f in chosen) f.Document.Lines.ForEach(l => l.TrySet(LineField.DealType, value, out _));
        Host.Edited(Documents(chosen));
    }

    private void Include(bool include)
    {
        var chosen = _findings.SelectedItems();
        foreach (var f in chosen) f.Document.Export = include;
        if (chosen.Count > 0) Host.Edited(Documents(chosen));
    }

    private void Reveal()
    {
        if (_findings.SelectedItems() is [var first, ..]) Host.Reveal(first.Document);
    }

    private static List<OperationDocument> Documents(IEnumerable<Finding> findings) => findings.Select(f => f.Document).Distinct().ToList();
}
