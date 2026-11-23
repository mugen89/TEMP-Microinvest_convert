using System.Diagnostics;
using MicroinvestConvert.App.Ui;
using MicroinvestConvert.Core.Nomenclature;

namespace MicroinvestConvert.App.Forms;

/// <summary>„Видове сделки": a quick reference of column N, with the way to the whole document.</summary>
internal sealed class DealTypesForm : AppForm
{
    // Where each deal type comes from in this version. Deal_Type.md is the full text.
    private static readonly Dictionary<int, string> Sources = new()
    {
        [1] = "Плащания (РКО, ПКО); чуждестранни фактури; продажби без стойност в дневника",
        [2] = "Покупки AC/AD, когато ДДС е 20% или 9% от основата",
        [3] = "Покупки AB; покупки AC без ДДС (0%); покупки „без ДДС дневник“ (AG)",
        [4] = "Покупки AE/AF; покупки, при които ДДС не отговаря на ставка",
        [5] = "Не се ползва", [6] = "Не се ползва",
        [7] = "Продажби AB/AC", [8] = "Не се ползва (протоколи)", [9] = "Не се ползва (протоколи)", [10] = "Не се ползва (протоколи)",
        [11] = "Продажби AD/AE", [12] = "Продажби AG", [13] = "Продажби AF", [14] = "Продажби AH",
        [15] = "Продажби AI", [16] = "Продажби AJ", [17] = "Продажби AK", [18] = "Не идва от файла; може да се зададе ръчно",
    };

    public DealTypesForm()
    {
        Text = "Видове сделки (колона N)";
        AsDialog(1140, 590, 860, 400);

        var grid = new Grid<DealType> { ReadOnly = true };
        grid.SetHeights(26, 30);
        grid.AddColumn(new() { Header = "Вид сделка", Width = 340, Get = d => d.Label, SortKey = d => d.Code });
        grid.AddColumn(new() { Header = "Къде влиза", Width = 190, Get = d => d.LedgerText });
        grid.AddColumn(new() { Header = "VIES", Width = 62, Center = true, Get = d => d.Side == DealSide.Sale ? d.Vies.ToString() : "" });
        grid.AddColumn(new() { Header = "Откъде идва в тази версия", Width = 300, Fill = true, Get = d => Sources[d.Code] });
        grid.StyleCell = (d, _, style) =>
        {
            if (Sources[d.Code].StartsWith("Не ", StringComparison.Ordinal)) style.ForeColor = Theme.Muted;
        };
        grid.SortRequested += () => grid.SetRows(DealTypes.All);
        grid.SetRows(DealTypes.All);

        var document = new FlatButton("Целият документ (Deal_Type.md)", ButtonLook.Quiet);
        document.Click += (_, _) => Open(AppInfo.DealTypesUrl);
        var close = new FlatButton("Затвори", ButtonLook.Primary);
        close.Click += (_, _) => Close();
        CancelButton = close;

        var bar = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Padding = new Padding(8, 6, 8, 8), BackColor = Theme.Surface };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.Controls.Add(document, 0, 0);
        bar.Controls.Add(close, 1, 0);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(grid, 0, 0);
        root.Controls.Add(bar, 0, 1);
        Controls.Add(root);
        EndLayout();
    }

    public static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Dialogs.Warning(null, "Страницата не можа да бъде отворена", url + "\n\n" + ex.Message);
        }
    }
}
