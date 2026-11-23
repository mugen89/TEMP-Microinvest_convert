using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MicroinvestConvert.App;
using MicroinvestConvert.App.Forms;
using MicroinvestConvert.App.Updates;

namespace MicroinvestConvert.Tests;

public sealed class UpdateServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "mic-upd-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    private sealed class Answers(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Requests.Add(request);
            return Task.FromResult(answer(request));
        }
    }

    private static string ReleaseJson(string tag, string? digest, string asset = "MicroinvestConvert-1.2.0-win-x64.zip",
        string url = "https://example.test/new.zip") => JsonSerializer.Serialize(new
    {
        tag_name = tag,
        body = "## Новото\n- **първо**\n- второ",
        published_at = "2026-11-24T10:00:00Z",
        html_url = "https://example.test/release",
        assets = new[]
        {
            new { name = "SHA256SUMS.txt", browser_download_url = "https://example.test/sums", size = 1L, digest = (string?)null },
            new { name = asset, browser_download_url = url, size = 100L, digest },
        },
    });

    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };

    private static string Digest(byte[] content) => "sha256:" + Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static byte[] Zip(string entryName, byte[] content)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        using (var entry = zip.CreateEntry(entryName).Open())
            entry.Write(content);
        return stream.ToArray();
    }

    private static UpdateService Service(string current, Func<HttpRequestMessage, HttpResponseMessage> answer) =>
        new(Version.Parse(current), handler: new Answers(answer));

    [Theory]
    [InlineData("1.1.9", true)]
    [InlineData("1.2.0", false)]
    [InlineData("2.0.0", false)]
    public async Task A_release_is_news_only_when_it_is_newer(string current, bool news)
    {
        using var service = Service(current, _ => Json(ReleaseJson("v1.2.0", Digest([1]))));
        var release = await service.FindNewerAsync();
        Assert.Equal(news, release != null);
        if (news) Assert.Equal(("1.2.0", "MicroinvestConvert-1.2.0-win-x64.zip"), (release!.Version.ToString(3), release.AssetName));
    }

    [Fact]
    public async Task No_release_yet_is_not_an_error()
    {
        using var service = Service("1.0.0", _ => new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.Null(await service.FindLatestAsync());
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_refusal_is_explained(HttpStatusCode status)
    {
        using var service = Service("1.0.0", _ => new HttpResponseMessage(status));
        await Assert.ThrowsAsync<UpdateException>(() => service.FindLatestAsync());
    }

    [Fact]
    public async Task No_internet_is_explained()
    {
        using var service = Service("1.0.0", _ => throw new HttpRequestException("няма"));
        await Assert.ThrowsAsync<UpdateException>(() => service.FindLatestAsync());
    }

    [Theory]
    [InlineData("v1.2.0", null, "MicroinvestConvert-1.2.0-win-x64.zip", "https://example.test/new.zip")]          // no checksum
    [InlineData("v1.2.0", "sha256:abc", "MicroinvestConvert-1.2.0-win-x64.zip", "https://example.test/new.zip")] // not one
    [InlineData("нещо", "ok", "MicroinvestConvert-1.2.0-win-x64.zip", "https://example.test/new.zip")]            // no version
    [InlineData("v1.2.0", "ok", "readme.txt", "https://example.test/new.zip")]                                   // no program
    [InlineData("v1.2.0", "ok", "MicroinvestConvert-1.2.0-win-x64.zip", "http://example.test/new.zip")]          // not https
    public async Task A_release_that_cannot_be_trusted_is_refused(string tag, string? digest, string asset, string url)
    {
        if (digest == "ok") digest = Digest([1]);
        using var service = Service("1.0.0", _ => Json(ReleaseJson(tag, digest, asset, url)));
        await Assert.ThrowsAsync<UpdateException>(() => service.FindLatestAsync());
    }

    [Fact]
    public async Task The_download_is_checked_and_unpacked()
    {
        byte[] program = Encoding.ASCII.GetBytes("the new program");
        byte[] zip = Zip("folder/" + AppInfo.ExeName, program);
        using var service = Service("1.0.0", r => r.RequestUri!.Host == "example.test"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) }
            : Json(ReleaseJson("v1.2.0", Digest(zip))));

        var seen = new List<int>();
        var release = await service.FindNewerAsync();
        string exe = await service.DownloadAsync(release!, _folder, new SyncProgress(seen), CancellationToken.None);

        Assert.Equal(program, File.ReadAllBytes(exe));
        Assert.Equal(AppInfo.ExeName, Path.GetFileName(exe));
        Assert.Contains(100, seen);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(exe)!));   // the archive is gone

        // once more over the first one
        Assert.Equal(exe, await service.DownloadAsync(release!, _folder, null, CancellationToken.None));
    }

    private sealed class SyncProgress(List<int> seen) : IProgress<int>
    {
        public void Report(int value) => seen.Add(value);
    }

    [Fact]
    public async Task A_changed_download_is_refused()
    {
        byte[] zip = Zip(AppInfo.ExeName, [1, 2, 3]);
        using var service = Service("1.0.0", r => r.RequestUri!.Host == "example.test"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) }
            : Json(ReleaseJson("v1.2.0", Digest([9, 9, 9]))));

        var release = await service.FindNewerAsync();
        var ex = await Assert.ThrowsAsync<UpdateException>(() => service.DownloadAsync(release!, _folder, null, CancellationToken.None));
        Assert.Contains("контролната сума", ex.Message);
    }

    [Fact]
    public async Task An_archive_without_the_program_is_refused()
    {
        byte[] zip = Zip("other.exe", [1]);
        using var service = Service("1.0.0", r => r.RequestUri!.Host == "example.test"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) }
            : Json(ReleaseJson("v1.2.0", Digest(zip))));
        var release = await service.FindNewerAsync();
        await Assert.ThrowsAsync<UpdateException>(() => service.DownloadAsync(release!, _folder, null, CancellationToken.None));
    }

    [Fact]
    public async Task The_request_says_nothing_about_the_user()
    {
        var answers = new Answers(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var service = new UpdateService(new Version(1, 0, 0), handler: answers);
        await service.FindLatestAsync();

        var request = Assert.Single(answers.Requests);
        Assert.Equal("https://api.github.com/repos/mugen89/TEMP-Microinvest_convert/releases/latest", request.RequestUri!.ToString());
        Assert.Equal("MicroinvestConvert/1.0.0", request.Headers.UserAgent.ToString());
        Assert.Null(request.Headers.Authorization);
        Assert.Equal(["Accept", "User-Agent"], request.Headers.Select(h => h.Key).Order());
    }

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2", "1.2.0")]
    [InlineData("V2.0.1.7", "2.0.1")]
    public void Tags_are_versions(string tag, string version)
    {
        Assert.True(UpdateService.TryParseVersion(tag, out var parsed));
        Assert.Equal(Version.Parse(version), parsed);
    }

    [Fact]
    public void Notes_are_shown_without_markdown() =>
        Assert.Equal($"Новото{Environment.NewLine}• първо{Environment.NewLine}код", UpdateForm.Plain("## Новото\n- **първо**\n`код`"));
}

public sealed class UpdateInstallerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "mic-inst-" + Guid.NewGuid());
    private readonly string _exe, _kept;

    public UpdateInstallerTests()
    {
        Directory.CreateDirectory(_folder);
        _exe = Path.Combine(_folder, AppInfo.ExeName);
        _kept = Path.Combine(_folder, "kept");
    }

    public void Dispose()
    {
        for (int i = 0; i < 20; i++)
        {
            try
            {
                Directory.Delete(_folder, true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(100);
            }
        }
    }

    private static string SystemTool(string name) => Path.Combine(Environment.SystemDirectory, name);

    [Fact]
    public void A_running_program_is_replaced_and_the_old_one_is_kept()
    {
        // A copy of ping.exe plays the running program, a copy of where.exe the new one.
        File.Copy(SystemTool("ping.exe"), _exe);
        string next = Path.Combine(_folder, "next.exe");
        File.Copy(SystemTool("where.exe"), next);
        byte[] newBytes = File.ReadAllBytes(next), oldBytes = File.ReadAllBytes(_exe);

        var installer = new UpdateInstaller(_exe, _kept);
        using var running = Process.Start(new ProcessStartInfo(_exe, "-n 30 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false })!;
        try
        {
            Assert.Null(installer.Obstacle());
            installer.Install(next);

            Assert.False(running.HasExited);
            Assert.Equal(newBytes, File.ReadAllBytes(_exe));
            Assert.Equal(oldBytes, File.ReadAllBytes(_exe + ".old"));
            Assert.False(File.Exists(_exe + ".new"));
        }
        finally
        {
            running.Kill();
            running.WaitForExit();
        }

        // what the new version does when it starts
        installer.FinishPreviousInstall();
        Assert.False(File.Exists(_exe + ".old"));
        Assert.Equal(oldBytes, File.ReadAllBytes(Assert.Single(Directory.GetFiles(_kept))));
    }

    [Fact]
    public void A_missing_new_program_changes_nothing()
    {
        File.Copy(SystemTool("ping.exe"), _exe);
        byte[] before = File.ReadAllBytes(_exe);
        var installer = new UpdateInstaller(_exe, _kept);

        Assert.Throws<UpdateException>(() => installer.Install(Path.Combine(_folder, "няма.exe")));
        Assert.Equal(before, File.ReadAllBytes(_exe));
        Assert.Empty(Directory.GetFiles(_folder, "*.new"));
        Assert.Empty(Directory.GetFiles(_folder, "*.old"));
    }

    [Fact]
    public void A_missing_program_is_an_obstacle() => Assert.NotNull(new UpdateInstaller(_exe, _kept).Obstacle());

    [Fact]
    public void The_running_version_is_not_offered_to_go_back_to()
    {
        File.Copy(SystemTool("ping.exe"), _exe);
        Directory.CreateDirectory(_kept);
        File.Copy(_exe, Path.Combine(_kept, "MicroinvestConvert-9.9.9.exe"));   // the same file version as the running one
        Assert.Empty(new UpdateInstaller(_exe, _kept).Kept());
    }

    [Fact]
    public void Finishing_without_an_update_does_nothing()
    {
        File.Copy(SystemTool("ping.exe"), _exe);
        new UpdateInstaller(_exe, _kept).FinishPreviousInstall();
        Assert.False(Directory.Exists(_kept));
    }

    [Fact]
    public void Running_from_the_development_tools_is_not_the_published_program() => Assert.False(UpdateInstaller.IsPublishedProgram);
}
