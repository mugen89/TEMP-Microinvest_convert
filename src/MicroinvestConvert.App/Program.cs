using System.Diagnostics;
using MicroinvestConvert.App.Forms;
using MicroinvestConvert.App.Ui;
using MicroinvestConvert.App.Updates;

namespace MicroinvestConvert.App;

internal static class Program
{
    /// <summary>Set after an update: the program closes and starts again.</summary>
    public static bool RestartRequested { get; set; }

    public static UpdateInstaller Installer { get; } = new(
        Environment.ProcessPath ?? Application.ExecutablePath, Path.Combine(AppInfo.LocalFolder, "Versions"));

    /// <summary>A new version waits here between its download and its installation.</summary>
    public static string DownloadFolder => Path.Combine(AppInfo.LocalFolder, "Updates");

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        ErrorReporter.Install();
        Version? replaced = null;
        if (UpdateInstaller.IsPublishedProgram)
        {
            replaced = Installer.FinishPreviousInstall();
            UpdateInstaller.DropDownloads(DownloadFolder);
        }

        using var updates = new UpdateService(AppInfo.Version);
        if (args is ["--update"]) return UpdateWithoutWindow(updates);

        using var app = new AppState(AppInfo.SettingsPath, AppInfo.DatabasePaths);
        using (var main = new MainForm(app, updates, replaced))
        {
            // A file dragged onto the .exe, or opened „с програмата".
            var files = args.Where(File.Exists).ToList();
            if (files.Count > 0) main.Shown += (_, _) => main.OpenFiles(files, null, main);
            Application.Run(main);
        }

        if (RestartRequested) Process.Start(new ProcessStartInfo(Installer.ExePath) { UseShellExecute = true })?.Dispose();
        return 0;
    }

    /// <summary>MicroinvestConvert.exe --update : installs the newest version, no questions. 0 = updated or up to date.</summary>
    private static int UpdateWithoutWindow(UpdateService updates)
    {
        try
        {
            if (!UpdateInstaller.IsPublishedProgram) throw new UpdateException("Обновява се само инсталираната програма.");
            return Task.Run(async () =>
            {
                var release = await updates.FindNewerAsync();
                if (release == null) return 0;
                string exe = await updates.DownloadAsync(release, DownloadFolder, null, CancellationToken.None);
                Installer.Install(exe);
                return 0;
            }).GetAwaiter().GetResult();
        }
        catch (UpdateException ex)
        {
            Dialogs.Error(null, "Обновяването не успя", ex.Message);
            return 1;
        }
    }
}
