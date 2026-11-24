using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using MicroinvestConvert.App;
using MicroinvestConvert.App.Forms;
using MicroinvestConvert.App.Forms.Checks;
using MicroinvestConvert.App.Updates;
using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Settings;
using MicroinvestConvert.Core.Source;

namespace MicroinvestConvert.Tests;

/// <summary>
/// Draws the windows to PNG files, for a look at the layout. Runs only when asked:
///   MICONVERT_SHOTS  = folder for the pictures
///   MICONVERT_FILES  = export files, separated by „|"
///   MICONVERT_DB     = a database of the program, for its exchange rates (a copy of it is used)
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
        Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
        if (Environment.GetEnvironmentVariable("MICONVERT_DB") is { Length: > 0 } database)
            File.Copy(database, Path.Combine(Path.GetDirectoryName(settings)!, Data.Database.FileName));
        using var app = new AppState(settings, rateSource: new FakeSource());
        app.Rates.Auto = false;
        app.Settings.CheckForUpdates = false;
        foreach (string file in files) app.Settings.AddRecent(file);
        using var updates = new UpdateService(AppInfo.Version);

        using (var main = new MainForm(app, updates))
        {
            Show(main);
            Shot(main, Path.Combine(folder, "start.png"));
            main.Size = main.MinimumSize;
            Pump();
            Shot(main, Path.Combine(folder, "start-small.png"));
            main.ClientSize = new Size(main.LogicalToDeviceUnits(1080), main.LogicalToDeviceUnits(690));

            foreach (var kind in OperationKinds.All)
            {
                using var empty = new ModuleForm(main, kind, 0);
                ShowAndShot(empty, Path.Combine(folder, $"empty-{kind}.png"));
            }

            for (int i = 0; i < files.Length; i++)
            {
                var batch = AiReader.Read(files[i]);
                using var window = new ModuleForm(main, batch.Kind, 0);
                window.Show(batch);
                Show(window);
                Shot(window, Path.Combine(folder, $"file{i + 1}-documents.png"));

                var work = Find<WorkView>(window);
                if (batch.Documents.FindIndex(d => d.CounterCurrency.Length > 0) is var inCurrency and >= 0)
                {
                    var documents = All(work).OfType<DataGridView>().First();
                    documents.CurrentCell = documents[4, inCurrency];
                    Pump();
                    Shot(window, Path.Combine(folder, $"file{i + 1}-currency.png"));
                }

                var tabs = Find<TabControl>(work);
                tabs.SelectedIndex = 1;
                Pump();
                Shot(window, Path.Combine(folder, $"file{i + 1}-preview.png"));
                tabs.SelectedIndex = 2;
                Pump();
                Shot(window, Path.Combine(folder, $"file{i + 1}-log.png"));
                tabs.SelectedIndex = 0;

                window.ClientSize = new Size(window.LogicalToDeviceUnits(1080), window.LogicalToDeviceUnits(620));
                Pump();
                Shot(window, Path.Combine(folder, $"file{i + 1}-small.png"));

                using var checks = new ChecksForm(work);
                Show(checks);
                var pages = Find<TabControl>(checks);
                for (int t = 0; t < pages.TabPages.Count; t++)
                {
                    pages.SelectedIndex = t;
                    Pump();
                    Shot(checks, Path.Combine(folder, $"file{i + 1}-checks-{t + 1}.png"));
                }

                using var form = new SettingsForm(app, batch);
                Show(form);
                var settingTabs = Find<TabControl>(form);
                for (int t = 0; t < settingTabs.TabPages.Count && i == 0; t++)
                {
                    settingTabs.SelectedIndex = t;
                    Pump();
                    Shot(form, Path.Combine(folder, $"settings-{t + 1}.png"));
                }
            }
        }

        using (var form = new RatesForm(app, [new DateTime(2026, 6, 10), new DateTime(2026, 6, 12)]))
        {
            Show(form);
            Shot(form, Path.Combine(folder, "rates.png"));
            var view = All(form).OfType<ComboBox>().First();   // a currency, for three calendar months
            view.SelectedIndex = view.Items.Count - 1;
            All(form).OfType<TextBox>().First().Text = "40.01.2026";
            Pump();
            Shot(form, Path.Combine(folder, "rates-currency.png"));
        }

        app.Clients.Save(new ClientSettings { Name = "Тестова Фирма ЕООД", Eik = "123456786", VatNumber = "BG123456786", CashAccount = "501/1", Accounts = { ["401"] = "401/1" }, AccountCurrencies = { ["4012"] = "USD" } });
        app.Clients.Save(new ClientSettings { Name = "Фирма само с име", Note = "без ЕИК" });
        using (var form = new ClientsForm(app, files.Take(2).Select(AiReader.Read).ToList())) ShowAndShot(form, Path.Combine(folder, "clients.png"));

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

    private static IEnumerable<Control> All(Control parent) => parent.Controls.Cast<Control>().SelectMany(c => All(c).Prepend(c));

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
