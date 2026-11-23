using MicroinvestConvert.App.Ui;

namespace MicroinvestConvert.App.Forms;

/// <summary>„Какво е ново": the notes of the versions since the one before the update.</summary>
internal sealed class WhatsNewForm : AppForm
{
    public WhatsNewForm(string heading, string notes)
    {
        Text = "Какво е ново";
        AsDialog(660, 460, 520, 340);

        var title = Make.Label(heading, font: Theme.Heading);
        title.Margin = new Padding(12, 12, 12, 4);
        var text = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, TabStop = false,
            BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Surface, Font = Theme.Base, Margin = new Padding(12, 4, 12, 4),
            Text = UpdateForm.Plain(notes),
        };

        var close = new FlatButton("Затвори", ButtonLook.Primary);
        close.Click += (_, _) => Close();
        AcceptButton = close;
        CancelButton = close;
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8, 6, 8, 8), BackColor = Theme.Surface,
        };
        bar.Controls.Add(close);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(title, 0, 0);
        root.Controls.Add(text, 0, 1);
        root.Controls.Add(bar, 0, 2);
        Controls.Add(root);
        EndLayout();
    }
}
