using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using MicroinvestConvert.App;
using MicroinvestConvert.App.Forms;
using MicroinvestConvert.App.Updates;
using MicroinvestConvert.Core.Rules;
using MicroinvestConvert.Core.Source;

namespace MicroinvestConvert.Tests;

/// <summary>
/// Draws the windows to PNG files, for a look at the layout. Runs only when asked:
///   MICONVERT_SHOTS  = folder for the pictures
///   MICONVERT_FILES  = export files, separated by „|"
///   MICONVERT_SCALE  = 1.5 to see the layout as on a screen at 150% (the text stays small)
/// </summary>
public class VisualTests
{
    [Fact]
    public void Draw_the_windows()
    {
        string? folder = Environment.GetEnvironmentVariable("MICONVERT_SHOTS");
        if (string.IsNullOrEmpty(folder)) return;
        string[] files = (Environment.GetEnvironmentVariable("MICONVERT_FILES") ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries);
        Directory.CreateDirectory(folder);

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Application.EnableVisualStyles();
                Application.SetDefaultFont(new Font("Segoe UI", 9f));
                Draw(folder, files);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new InvalidOperationException(failure.ToString(), failure);
    }

    private static void Draw(string folder, string[] files)
    {
        string settings = Path.Combine(Path.GetTempPath(), "mic-shots-" + Guid.NewGuid(), "settings.json");
        var app = new AppState(settings);
        app.Settings.CheckForUpdates = false;
        foreach (string file in files) app.Settings.AddRecent(file);
        using var updates = new UpdateService(AppInfo.Version);

        using (var main = new MainForm(app, updates))
        {
            Show(main);
            Shot(main, Path.Combine(folder, "start.png"));
            for (int i = 0; i < files.Length; i++)
            {
                main.OpenFile(files[i]);
                if (Scale != 1f) Find<WorkView>(main).Scale(new SizeF(Scale, Scale));
                Pump();
                Shot(main, Path.Combine(folder, $"file{i + 1}-documents.png"));

                var tabs = Find<TabControl>(main);
                tabs.SelectedIndex = 1;
                Pump();
                Shot(main, Path.Combine(folder, $"file{i + 1}-preview.png"));
                tabs.SelectedIndex = 2;
                Pump();
                Shot(main, Path.Combine(folder, $"file{i + 1}-log.png"));
                tabs.SelectedIndex = 0;

                main.ClientSize = new Size(main.LogicalToDeviceUnits(1000), main.LogicalToDeviceUnits(640));
                Pump();
                Shot(main, Path.Combine(folder, $"file{i + 1}-small.png"));
                main.ClientSize = new Size(main.LogicalToDeviceUnits(1360), main.LogicalToDeviceUnits(820));
                Pump();
            }
        }

        for (int i = 0; i < files.Length; i++)
        {
            var batch = AiReader.Read(files[i]);
            void Reprocess() => new DocumentProcessor(app.Settings.OptionsFor(batch.ClientEik)).Process(batch);
            Reprocess();
            using (var form = new VatCheckForm(app, batch, Reprocess)) ShowAndShot(form, Path.Combine(folder, $"file{i + 1}-vat.png"));
            using (var form = new AccountsForm(app, batch)) ShowAndShot(form, Path.Combine(folder, $"file{i + 1}-accounts.png"));
            using (var form = new SettingsForm(app, batch))
            {
                Show(form);
                var tabs = Find<TabControl>(form);
                for (int t = 0; t < tabs.TabPages.Count; t++)
                {
                    tabs.SelectedIndex = t;
                    Pump();
                    if (i == 0) Shot(form, Path.Combine(folder, $"settings-{t + 1}.png"));
                }
            }
        }

        using (var form = new DealTypesForm()) ShowAndShot(form, Path.Combine(folder, "deal-types.png"));
        using (var form = new UpdateForm(app, updates))
        {
            Show(form);
            for (int i = 0; i < 40; i++) { Pump(); Thread.Sleep(100); }
            Shot(form, Path.Combine(folder, "update.png"));
        }

        if (Directory.Exists(Path.GetDirectoryName(settings))) Directory.Delete(Path.GetDirectoryName(settings)!, true);
    }

    private static void ShowAndShot(Form form, string path)
    {
        Show(form);
        Shot(form, path);
    }

    private static readonly float Scale = float.TryParse(Environment.GetEnvironmentVariable("MICONVERT_SCALE"),
        System.Globalization.CultureInfo.InvariantCulture, out float scale) ? scale : 1f;

    private static void Show(Form form)
    {
        if (Scale != 1f) form.Scale(new SizeF(Scale, Scale));
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-6000, 0);   // not over the user's windows
        form.ShowInTaskbar = false;
        form.Show();
        Pump();
    }

    private static void Shot(Form form, string path)
    {
        Pump();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(path, ImageFormat.Png);
    }

    private static void Pump()
    {
        for (int i = 0; i < 8; i++)
        {
            Application.DoEvents();
            Thread.Sleep(15);
        }
    }

    private static T Find<T>(Control parent) where T : Control
    {
        foreach (Control child in parent.Controls)
        {
            if (child is T found) return found;
            if (child.Controls.Count > 0 && TryFind<T>(child) is { } deeper) return deeper;
        }

        throw new InvalidOperationException("няма " + typeof(T).Name);
    }

    private static T? TryFind<T>(Control parent) where T : Control
    {
        foreach (Control child in parent.Controls)
        {
            if (child is T found) return found;
            if (TryFind<T>(child) is { } deeper) return deeper;
        }

        return null;
    }
}
