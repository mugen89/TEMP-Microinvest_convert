using MicroinvestConvert.App.Ui;
using MicroinvestConvert.App.Updates;

namespace MicroinvestConvert.App.Forms;

/// <summary>„Обновяване": is there a newer version, what is new in it, install it, or go back.</summary>
internal sealed class UpdateForm : AppForm
{
    private readonly UpdateService _updates;
    private readonly UpdateInstaller _installer;
    private readonly CancellationTokenSource _cancel = new();

    private readonly Label _status = Make.Label("Проверява се…", font: Theme.Heading);
    private readonly TextBox _notes = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true,
        BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Surface, Font = Theme.Base, Margin = new Padding(12, 4, 12, 4),
    };

    private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Visible = false, Height = 10, Margin = new Padding(12, 2, 12, 2) };
    private readonly FlatButton _install = new("Обнови", ButtonLook.Primary) { Enabled = false };
    private readonly ComboBox _kept = Make.Combo(230);
    private readonly FlatButton _goBack = new("Върни тази версия");
    private readonly FlatButton _close = new("Затвори", ButtonLook.Quiet);
    private ReleaseInfo? _release;

    public UpdateForm(AppState app, UpdateService updates)
    {
        _updates = updates;
        _installer = Program.Installer;

        Text = "Обновяване";
        AsDialog(660, 460, 560, 380, maximize: false);

        _status.Margin = new Padding(12, 12, 12, 2);
        var version = Make.Label($"Инсталирана версия: {AppInfo.VersionText}", Theme.Muted);
        version.Margin = new Padding(12, 0, 12, 4);

        _install.Click += async (_, _) => await InstallAsync();
        _close.Click += (_, _) => Close();
        CancelButton = _close;
        _goBack.Click += (_, _) => GoBack();

        var page = new FlatButton("Страница на програмата", ButtonLook.Quiet);
        page.Click += (_, _) => DealTypesForm.Open(_release?.PageUrl.ToString() ?? AppInfo.RepositoryUrl + "/releases");

        var back = Make.Flow(Make.Label("Предишна версия:"), _kept, _goBack);
        back.Margin = new Padding(9, 2, 9, 2);

        var bar = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Padding = new Padding(8, 6, 8, 8), BackColor = Theme.Surface };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.Controls.Add(page, 0, 0);
        bar.Controls.Add(Make.Flow(_close, _install), 1, 0);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(_status, 0, 0);
        root.Controls.Add(version, 0, 1);
        root.Controls.Add(_notes, 0, 2);
        root.Controls.Add(_progress, 0, 3);
        root.Controls.Add(back, 0, 4);
        root.Controls.Add(bar, 0, 5);
        Controls.Add(root);

        var kept = _installer.Kept();
        back.Visible = kept.Count > 0 && UpdateInstaller.IsPublishedProgram;
        foreach (var k in kept) _kept.Items.Add(new KeptItem(k));
        if (_kept.Items.Count > 0) _kept.SelectedIndex = 0;
        EndLayout();
    }

    private sealed record KeptItem(KeptVersion Kept)
    {
        public override string ToString() => $"{Kept.Version.ToString(3)}  (до {Kept.Kept:dd.MM.yyyy HH:mm})";
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await CheckAsync();
    }

    private async Task CheckAsync()
    {
        try
        {
            var release = await _updates.FindLatestAsync(_cancel.Token);
            if (IsDisposed) return;

            if (release == null)
            {
                _status.Text = "Още няма публикувана версия";
                return;
            }

            _notes.Text = Plain(release.Notes);
            if (release.Version <= _updates.Current)
            {
                _status.Text = "Имате последната версия";
                _status.ForeColor = Theme.OkInk;
                return;
            }

            _release = release;
            string when = release.Published is { } p ? $" от {p.ToLocalTime():dd.MM.yyyy}" : "";
            _status.Text = $"Има нова версия {release.Version.ToString(3)}{when}";

            if (!UpdateInstaller.IsPublishedProgram)
            {
                _notes.Text = "Това е програма от средата за разработка – обновява се само инсталираната (един .exe файл).\r\n\r\n" + _notes.Text;
                return;
            }

            _install.Enabled = true;
            AcceptButton = _install;
        }
        catch (UpdateException ex)
        {
            if (IsDisposed) return;
            _status.Text = "Проверката не успя";
            _status.ForeColor = Theme.WarningInk;
            _notes.Text = ex.Message.ReplaceLineEndings();
        }
        catch (OperationCanceledException)
        {
            // The window was closed.
        }
    }

    // The notes are Markdown; in a plain box the marks would only get in the way.
    internal static string Plain(string markdown) => string.Join(Environment.NewLine, markdown.ReplaceLineEndings("\n").Split('\n')
        .Select(line => line.TrimEnd().Replace("**", "").Replace("`", ""))
        .Select(line => line.StartsWith('#') ? line.TrimStart('#', ' ') : line.StartsWith("- ") ? "• " + line[2..] : line));

    private async Task InstallAsync()
    {
        if (_release == null) return;
        if (_installer.Obstacle() is { } obstacle)
        {
            Dialogs.Warning(this, "Обновяването не е възможно", obstacle);
            return;
        }

        _install.Enabled = false;
        _goBack.Enabled = false;
        _progress.Visible = true;
        try
        {
            _status.Text = $"Изтегля се версия {_release.Version.ToString(3)}…";
            var progress = new Progress<int>(p => { if (!IsDisposed) _progress.Value = p; });
            string exe = await _updates.DownloadAsync(_release, Program.DownloadFolder, progress, _cancel.Token);

            _status.Text = "Инсталира се…";
            _installer.Install(exe);
            DialogResult = DialogResult.OK;   // the program starts again
        }
        catch (UpdateException ex)
        {
            if (IsDisposed) return;
            _status.Text = "Обновяването не успя";
            _progress.Visible = false;
            _install.Enabled = true;
            _goBack.Enabled = true;
            Dialogs.Error(this, "Обновяването не успя", ex.Message);
        }
        catch (OperationCanceledException)
        {
            // The window was closed during the download; nothing has been changed.
        }
    }

    private void GoBack()
    {
        if (_kept.SelectedItem is not KeptItem item) return;
        if (!Dialogs.Ask(this, $"Да върна ли версия {item.Kept.Version.ToString(3)}?",
                "Програмата ще се рестартира. Настройките се запазват.", "Върни", "Отказ"))
            return;

        try
        {
            _installer.Install(item.Kept.Path);
            DialogResult = DialogResult.OK;
        }
        catch (UpdateException ex)
        {
            Dialogs.Error(this, "Връщането не успя", ex.Message);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _cancel.Cancel();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _cancel.Dispose();
        base.Dispose(disposing);
    }
}
