using System.Reflection;
using MicroinvestConvert.Data;

namespace MicroinvestConvert.App;

/// <summary>Names, version and places of the program.</summary>
internal static class AppInfo
{
    public const string Name = "Microinvest Конвертор";
    public const string FolderName = "MicroinvestConvert";
    public const string ExeName = "MicroinvestConvert.exe";

    // Where the releases are. The repository is public, so no key is needed to read them.
    public const string RepositoryOwner = "mugen89";
    public const string RepositoryName = "TEMP-Microinvest_convert";

    public static string RepositoryUrl => $"https://github.com/{RepositoryOwner}/{RepositoryName}";
    public static string DealTypesUrl => RepositoryUrl + "/blob/main/Deal_Type.md";

    public static Version Version { get; } = ReadVersion();

    public static string VersionText => Version.ToString(3);

    private static Version ReadVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
    }

    /// <summary>Settings follow the Windows user: %AppData%\MicroinvestConvert.</summary>
    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), FolderName, "settings.json");

    /// <summary>Logs, downloads and previous versions: %LocalAppData%\MicroinvestConvert.</summary>
    public static string LocalFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderName);

    /// <summary>The database is beside the program; with the logs, when that folder cannot be written.</summary>
    public static IReadOnlyList<string> DatabasePaths =>
    [
        Path.Combine(Path.GetDirectoryName(Environment.ProcessPath ?? Application.ExecutablePath)!, Database.FileName),
        Path.Combine(LocalFolder, Database.FileName),
    ];

    public static Icon LoadIcon()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico");
        return stream != null ? new Icon(stream) : SystemIcons.Application;
    }
}
