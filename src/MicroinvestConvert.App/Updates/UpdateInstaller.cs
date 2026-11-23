using System.Diagnostics;

namespace MicroinvestConvert.App.Updates;

/// <summary>A previous version kept on this computer.</summary>
internal sealed record KeptVersion(Version Version, string Path, DateTime Kept);

/// <summary>
/// Puts a new program in the place of the running one. Windows lets a running .exe be
/// renamed, so: the running one steps aside, the new one takes its name, and the program
/// starts again. Whatever fails on the way is put back. The replaced version is kept, so it
/// can be returned to.
/// </summary>
internal sealed class UpdateInstaller(string exePath, string keptFolder)
{
    private const int KeepVersions = 3;
    private const string OldSuffix = ".old";
    private const string NewSuffix = ".new";

    public string ExePath { get; } = exePath;

    private string OldPath => ExePath + OldSuffix;
    private string NewPath => ExePath + NewSuffix;

    /// <summary>The program as published: one self-contained .exe.</summary>
    public static bool IsPublishedProgram =>
#pragma warning disable IL3000 // empty exactly when the program is a single file — which is the question
        typeof(UpdateInstaller).Assembly.Location.Length == 0;
#pragma warning restore IL3000

    /// <summary>Why an update is impossible here; null when it is possible.</summary>
    public string? Obstacle()
    {
        if (!File.Exists(ExePath)) return "Програмата не намира собствения си файл.";

        string probe = Path.Combine(Path.GetDirectoryName(ExePath)!, Path.GetRandomFileName());
        try
        {
            using (File.Create(probe)) { }
            File.Delete(probe);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "Няма право на запис в папката на програмата:\n" + Path.GetDirectoryName(ExePath) +
                   "\n\nПреместете програмата в своя папка (например в Документи) или я стартирайте като администратор.";
        }
    }

    /// <summary>Replaces the program. Returns only when the new one is in place.</summary>
    public void Install(string newExe)
    {
        if (Obstacle() is { } obstacle) throw new UpdateException(obstacle);

        bool stepped = false;
        try
        {
            // Copied next to the program first, so the swap itself is two renames.
            File.Copy(newExe, NewPath, overwrite: true);
            if (File.Exists(OldPath)) File.Delete(OldPath);
            File.Move(ExePath, OldPath);
            stepped = true;
            File.Move(NewPath, ExePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (stepped && !File.Exists(ExePath)) TryQuietly(() => File.Move(OldPath, ExePath));
            TryQuietly(() => File.Delete(NewPath));
            throw new UpdateException("Новата версия не можа да бъде поставена. Програмата остава, както беше.\n\n" + ex.Message, ex);
        }
    }

    /// <summary>
    /// At the start of the new version: the old one, which has just closed, goes to the kept
    /// versions. Returns which one it was; null when nothing was installed since the last start.
    /// </summary>
    public Version? FinishPreviousInstall()
    {
        TryQuietly(() => { if (File.Exists(NewPath)) File.Delete(NewPath); });
        if (!File.Exists(OldPath)) return null;

        var replaced = VersionOf(OldPath);
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                Directory.CreateDirectory(keptFolder);
                File.Move(OldPath, KeptPath(VersionOf(OldPath)), overwrite: true);
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(150);   // the old process is still closing
            }
        }

        foreach (var extra in Kept().Skip(KeepVersions)) TryQuietly(() => File.Delete(extra.Path));
        return replaced;
    }

    /// <summary>What was downloaded is in place by now.</summary>
    public static void DropDownloads(string folder) =>
        TryQuietly(() => { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); });

    /// <summary>The versions that can be returned to, newest first; the running one is not among them.</summary>
    public IReadOnlyList<KeptVersion> Kept()
    {
        if (!Directory.Exists(keptFolder)) return [];
        var running = VersionOf(ExePath);

        return Directory.GetFiles(keptFolder, "MicroinvestConvert-*.exe")
            .Select(path => new KeptVersion(VersionOf(path), path, File.GetLastWriteTime(path)))
            .Where(k => k.Version != running)
            .OrderByDescending(k => k.Version)
            .ToList();
    }

    private string KeptPath(Version version) => Path.Combine(keptFolder, $"MicroinvestConvert-{version.ToString(3)}.exe");

    internal static Version VersionOf(string exe)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exe);
            return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
        }
        catch (FileNotFoundException)
        {
            return new Version(0, 0, 0);
        }
    }

    private static void TryQuietly(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
