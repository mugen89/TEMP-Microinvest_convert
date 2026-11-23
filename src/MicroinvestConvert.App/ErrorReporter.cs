using System.Diagnostics;
using System.Globalization;
using MicroinvestConvert.App.Ui;

namespace MicroinvestConvert.App;

/// <summary>An unexpected error is written to a log and shown with a way to send it.</summary>
internal static class ErrorReporter
{
    private static string LogFolder => Path.Combine(AppInfo.LocalFolder, "Logs");

    public static void Install()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Report(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Report(ex);
        };
    }

    public static void Report(Exception ex)
    {
        string details = $"{AppInfo.Name} {AppInfo.VersionText}, {DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture)}\n" +
                         $"{Environment.OSVersion}, .NET {Environment.Version}\n\n{ex}";
        string? log = WriteLog(details);

        var page = new TaskDialogPage
        {
            Caption = AppInfo.Name,
            Heading = "Възникна неочаквана грешка",
            Text = ex.Message + "\n\nПрограмата продължава. Ако се повтори, копирайте подробностите и ги изпратете.",
            Icon = TaskDialogIcon.Error,
            AllowCancel = true,
            Expander = new TaskDialogExpander(details) { CollapsedButtonText = "Подробности", ExpandedButtonText = "Скрий" },
        };
        if (log != null) page.Footnote = new TaskDialogFootnote(log);
        var copy = new TaskDialogButton("Копирай подробностите");
        page.Buttons.Add(copy);
        page.Buttons.Add(new TaskDialogButton("Затвори"));

        if (TaskDialog.ShowDialog(page) != copy) return;
        try { Clipboard.SetText(details); }
        catch (System.Runtime.InteropServices.ExternalException) { Dialogs.Warning(null, "Копирането не успя", log ?? ""); }
    }

    private static string? WriteLog(string details)
    {
        try
        {
            Directory.CreateDirectory(LogFolder);
            string path = Path.Combine(LogFolder, $"error-{DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)}.txt");
            File.WriteAllText(path, details);
            foreach (string old in Directory.GetFiles(LogFolder, "error-*.txt").OrderDescending().Skip(30)) File.Delete(old);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(ex);
            return null;
        }
    }
}
