using System.Globalization;
using MicroinvestConvert.App.Ui;
using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Rules;

namespace MicroinvestConvert.App.Forms.Checks;

/// <summary>
/// „Контрагенти": every partner of the file once, with its numbers, and what does not fit – a
/// wrong ЕИК, one number under two names, one name under two numbers. A correction goes into
/// all of its documents.
/// </summary>
internal sealed class PartnersPage : CheckPage
{
    private sealed class Row
    {
        public required string Name { get; init; }
        public required string Eik { get; init; }
        public required string Vat { get; init; }
        public required List<OperationDocument> Documents { get; init; }
        public string Found { get; set; } = "";

        /// <summary>The name without spaces, marks and case.</summary>
        public string Plain { get; init; } = "";
    }

    private readonly Grid<Row> _grid = new();
    private readonly CheckBox _onlyFound = Make.Check("Само с бележка");
    private List<Row> _rows = [];

    public override string Title => "Контрагенти";
    public override int Open => Collect().Count(r => r.Found.Length > 0);

    public PartnersPage(ICheckHost host) : base(host)
    {
        _grid.Use(Palette);
        _grid.SetHeights(26, 30);
        Column(DocField.PartnerName, "✎ Контрагент", 230, r => r.Name);
        Column(DocField.Eik, "✎ ЕИК", 124, r => r.Eik);
        Column(DocField.VatNumber, "✎ ДДС №", 132, r => r.Vat);
        _grid.AddColumn(new() { Header = "Държава", Width = 80, Center = true, Get = r => new PartnerIdentity(r.Eik, r.Vat).Prefix, Tip = "Кодът в началото на ДДС номера" });
        _grid.AddColumn(new() { Header = "Документи", Width = 100, Right = true, Get = r => r.Documents.Count.ToString(CultureInfo.InvariantCulture), SortKey = r => r.Documents.Count });
        _grid.AddColumn(new() { Header = "Общо", Width = 104, Right = true, Get = r => Fmt.Money(r.Documents.Sum(d => d.TotalGross)), SortKey = r => r.Documents.Sum(d => d.TotalGross) });
        _grid.AddColumn(new() { Header = "Бележка", Width = 300, Fill = true, Get = r => r.Found });
        _grid.StyleCell = (row, column, style) =>
        {
            if (column < 3) style.BackColor = row.Found.Length > 0 ? Theme.WarningBack : Palette.Soft;
            if (column == 6) style.ForeColor = Theme.WarningInk;
        };
        _grid.Edited += rows => Host.Edited(rows.SelectMany(r => r.Documents).Distinct().ToList());
        _grid.RowActivated += r => Host.Reveal(r.Documents[0]);
        _grid.SortRequested += Fill;

        _onlyFound.CheckedChanged += (_, _) => Fill();
        _onlyFound.Margin = new Padding(8, 2, 8, 4);

        Controls.Add(Rows(2,
            Note("Всеки контрагент веднъж. Корекция тук се записва във всичките му документи. Двойно щракване върху ред показва първия от тях."),
            _onlyFound, _grid));
        EndLayout();
    }

    private void Column(DocField field, string header, int width, Func<Row, string> get) => _grid.AddColumn(new()
    {
        Header = header, Width = width, Get = get,
        Set = (r, v) =>
        {
            string error = "";
            return r.Documents.TrueForAll(d => d.TrySet(field, v, out error)) ? null : error;
        },
    });

    public override void RefreshRows()
    {
        _rows = Collect();
        Fill();
    }

    private void Fill() => _grid.SetRows(_rows.Where(r => !_onlyFound.Checked || r.Found.Length > 0).OrderBy(r => r.Found.Length == 0));

    private List<Row> Collect()
    {
        var rows = Batch.Documents.Where(d => d is { Export: true, SkippedBySetting: false })
            .GroupBy(d => (d.PartnerName, d.Eik, d.VatNumber))
            .Select(g => new Row
            {
                Name = g.Key.PartnerName, Eik = g.Key.Eik, Vat = g.Key.VatNumber, Documents = [.. g],
                Plain = new string(g.Key.PartnerName.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray()),
            })
            .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var byNumber = rows.Where(r => Number(r).Length > 0).ToLookup(Number);
        var byName = rows.Where(r => r.Plain.Length > 0).ToLookup(r => r.Plain);
        foreach (var row in rows) row.Found = string.Join("; ", Notes(row, byNumber, byName));
        return rows;
    }

    // Fifteen nines mean „no number", so they say nothing about who it is.
    private static string Number(Row row) =>
        row.Eik.Length > 0 && !PartnerIdentity.IsPlaceholder(row.Eik) ? row.Eik : row.Vat;

    private IEnumerable<string> Notes(Row row, ILookup<string, Row> byNumber, ILookup<string, Row> byName)
    {
        if (row.Name.Length == 0) yield return "няма име";
        if (row.Eik.Length == 0 && row.Vat.Length == 0) yield return "няма номер";

        if (Batch.Kind != OperationKind.Foreign)
        {
            if (!PartnerIdentity.HasValidEikCheckDigit(row.Eik)) yield return "ЕИК с грешна контролна цифра";
            else if (!PartnerIdentity.HasPossibleLength(row.Eik)) yield return $"номер с {row.Eik.Length} цифри – не е български ЕИК";
        }
        else if (new PartnerIdentity(row.Eik, row.Vat).IsBulgarian)
        {
            yield return "български номер";
        }

        var names = byNumber[Number(row)].Where(r => r.Plain != row.Plain && r.Plain.Length > 0).Select(r => r.Name).Distinct().ToList();
        if (names.Count > 0) yield return "същият номер и с име " + string.Join(", ", names.Take(3).Select(n => $"„{n}“"));

        var numbers = byName[row.Plain].Where(r => r != row && Number(r) != Number(row) && Number(r).Length > 0).Select(Number).Distinct().ToList();
        if (numbers.Count > 0 && Number(row).Length > 0) yield return "същото име и с номер " + string.Join(", ", numbers.Take(3));
    }
}
