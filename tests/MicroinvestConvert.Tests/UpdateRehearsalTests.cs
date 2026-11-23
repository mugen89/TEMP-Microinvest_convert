using System.Diagnostics;
using MicroinvestConvert.App;
using MicroinvestConvert.App.Updates;

namespace MicroinvestConvert.Tests;

/// <summary>
/// The whole update with two real published programs: the old one runs, is replaced, the new
/// one starts and keeps the old, and then the old one is returned to. Opens windows, so it
/// runs only when asked:
///   MICONVERT_OLD_EXE, MICONVERT_NEW_EXE = two published MicroinvestConvert.exe
/// </summary>
public class UpdateRehearsalTests
{
    [Fact]
    public void Update_and_go_back_with_real_programs()
    {
        string? oldExe = Environment.GetEnvironmentVariable("MICONVERT_OLD_EXE");
        string? newExe = Environment.GetEnvironmentVariable("MICONVERT_NEW_EXE");
        if (string.IsNullOrEmpty(oldExe) || string.IsNullOrEmpty(newExe)) return;

        var oldVersion = UpdateInstaller.VersionOf(oldExe);
        var newVersion = UpdateInstaller.VersionOf(newExe);
        Assert.NotEqual(oldVersion, newVersion);

        // Not in „Program Files": the folder of the program, as a user would have it.
        string folder = Path.Combine(Path.GetTempPath(), "mic-rehearsal-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        string exe = Path.Combine(folder, AppInfo.ExeName);
        File.Copy(oldExe, exe);

        // The program keeps replaced versions in its own folder of the Windows user.
        string versions = Path.Combine(AppInfo.LocalFolder, "Versions");
        bool hadFolder = Directory.Exists(AppInfo.LocalFolder);
        string keptOld = Path.Combine(versions, $"MicroinvestConvert-{oldVersion.ToString(3)}.exe");
        string keptNew = Path.Combine(versions, $"MicroinvestConvert-{newVersion.ToString(3)}.exe");
        Assert.False(File.Exists(keptOld) || File.Exists(keptNew), "на този компютър вече има запазени версии");

        var installer = new UpdateInstaller(exe, versions);
        try
        {
            using (var running = Start(exe, oldVersion))
            {
                installer.Install(newExe);
                Assert.False(running.HasExited);
                Assert.Equal(newVersion, UpdateInstaller.VersionOf(exe));
                Close(running);
            }

            using (var updated = Start(exe, newVersion))
            {
                Assert.False(File.Exists(exe + ".old"));
                Assert.True(File.Exists(keptOld));
                Assert.Equal(oldVersion, Assert.Single(installer.Kept()).Version);

                installer.Install(keptOld);   // „Върни тази версия"
                Close(updated);
            }

            using (var returned = Start(exe, oldVersion))
            {
                Assert.True(File.Exists(keptNew));
                Close(returned);
            }
        }
        finally
        {
            foreach (string kept in new[] { keptOld, keptNew }) File.Delete(kept);
            if (!hadFolder && Directory.Exists(AppInfo.LocalFolder)) Directory.Delete(AppInfo.LocalFolder, true);
            Directory.Delete(folder, true);
        }
    }

    private static Process Start(string exe, Version version)
    {
        var process = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true })!;
        for (int i = 0; i < 200; i++)
        {
            process.Refresh();
            if (process.HasExited) throw new InvalidOperationException("Програмата се затвори сама, код " + process.ExitCode);
            if (process.MainWindowTitle.Contains(version.ToString(3))) return process;
            Thread.Sleep(100);
        }

        string title = process.MainWindowTitle;
        process.Kill();
        throw new InvalidOperationException($"Няма прозорец на версия {version.ToString(3)}; заглавието е „{title}“");
    }

    private static void Close(Process process)
    {
        process.CloseMainWindow();
        if (!process.WaitForExit(10000))
        {
            process.Kill();
            throw new InvalidOperationException("Програмата не се затвори");
        }

        Assert.Equal(0, process.ExitCode);
    }
}
