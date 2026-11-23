using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace MicroinvestConvert.App.Updates;

/// <summary>Why an update could not be found, downloaded or installed — in words for the user.</summary>
internal sealed class UpdateException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>A published release.</summary>
internal sealed record ReleaseInfo(
    Version Version, string Notes, DateTimeOffset? Published, string AssetName, Uri AssetUrl, long AssetSize, string Sha256, Uri PageUrl);

/// <summary>
/// Reads the releases of the program on GitHub and downloads them. Anonymous: nothing about
/// the user or the files is sent. A download is accepted only with the checksum GitHub
/// publishes for it.
/// </summary>
internal sealed class UpdateService : IDisposable
{
    private const string AssetSuffix = "-win-x64.zip";

    private readonly HttpClient _http;
    private readonly Uri _latest;

    public Version Current { get; }

    public UpdateService(Version current, Uri? latestRelease = null, HttpMessageHandler? handler = null)
    {
        Current = current;
        _latest = latestRelease ?? new Uri($"https://api.github.com/repos/{AppInfo.RepositoryOwner}/{AppInfo.RepositoryName}/releases/latest");
        _http = handler != null ? new HttpClient(handler) : new HttpClient();
        _http.Timeout = TimeSpan.FromSeconds(20);
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MicroinvestConvert", current.ToString(3)));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    /// <summary>The latest release when it is newer than this program; otherwise null.</summary>
    public async Task<ReleaseInfo?> FindNewerAsync(CancellationToken cancel = default)
    {
        var release = await FindLatestAsync(cancel);
        return release != null && release.Version > Current ? release : null;
    }

    /// <summary>The latest release; null when there is none yet.</summary>
    public async Task<ReleaseInfo?> FindLatestAsync(CancellationToken cancel = default)
    {
        try
        {
            using var response = await _http.GetAsync(_latest, cancel);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                throw new UpdateException("GitHub временно отказва проверката. Опитайте отново след час.");
            response.EnsureSuccessStatusCode();

            await using var body = await response.Content.ReadAsStreamAsync(cancel);
            using var json = await JsonDocument.ParseAsync(body, cancellationToken: cancel);
            return Parse(json.RootElement);
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateException("Няма връзка с GitHub. Проверете интернета.", ex);
        }
        catch (TaskCanceledException ex) when (!cancel.IsCancellationRequested)
        {
            throw new UpdateException("GitHub не отговори навреме.", ex);
        }
        catch (JsonException ex)
        {
            throw new UpdateException("Отговорът на GitHub не е разбран.", ex);
        }
    }

    internal static ReleaseInfo Parse(JsonElement release)
    {
        string tag = Text(release, "tag_name");
        if (!TryParseVersion(tag, out var version)) throw new UpdateException($"Версията „{tag}“ не е разбрана.");
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            throw new UpdateException($"Версия {version} няма файлове.");

        foreach (var asset in assets.EnumerateArray())
        {
            string name = Text(asset, "name");
            if (!name.EndsWith(AssetSuffix, StringComparison.OrdinalIgnoreCase)) continue;

            string digest = Text(asset, "digest");
            if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) || digest.Length != "sha256:".Length + 64)
                throw new UpdateException($"За файла на версия {version} няма контролна сума. Обновяването е спряно.");
            if (!Uri.TryCreate(Text(asset, "browser_download_url"), UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps && !url.IsLoopback)
                throw new UpdateException("Адресът на файла не е разбран.");

            Uri.TryCreate(Text(release, "html_url"), UriKind.Absolute, out var page);
            DateTimeOffset? published = DateTimeOffset.TryParse(Text(release, "published_at"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var when) ? when : null;
            long size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out long bytes) ? bytes : 0;

            return new ReleaseInfo(version, Text(release, "body"), published, name, url, size,
                digest["sha256:".Length..].ToLowerInvariant(), page ?? new Uri(AppInfo.RepositoryUrl));
        }

        throw new UpdateException($"Във версия {version} няма файл за Windows ({AssetSuffix}).");
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    /// <summary>„v1.2.3" or „1.2.3".</summary>
    internal static bool TryParseVersion(string tag, out Version version)
    {
        version = new Version(0, 0, 0);
        string text = tag.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(text, out var parsed)) return false;
        version = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build));
        return true;
    }

    /// <summary>Downloads a release and returns the new program, checked and unpacked.</summary>
    public async Task<string> DownloadAsync(ReleaseInfo release, string folder, IProgress<int>? progress, CancellationToken cancel)
    {
        string target = Path.Combine(folder, release.Version.ToString(3));
        try
        {
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            Directory.CreateDirectory(target);
            string zip = Path.Combine(target, release.AssetName);

            string hash = await DownloadFileAsync(release, zip, progress, cancel);
            if (!string.Equals(hash, release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new UpdateException("Изтегленият файл е повреден (контролната сума не съвпада). Опитайте отново.");

            string exe = Path.Combine(target, AppInfo.ExeName);
            using (var archive = ZipFile.OpenRead(zip))
            {
                var entry = archive.Entries.FirstOrDefault(e => e.Name.Equals(AppInfo.ExeName, StringComparison.OrdinalIgnoreCase))
                    ?? throw new UpdateException($"В архива няма {AppInfo.ExeName}.");
                entry.ExtractToFile(exe, overwrite: true);
            }

            File.Delete(zip);
            return exe;
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateException("Изтеглянето прекъсна. Проверете интернета и опитайте отново.", ex);
        }
        catch (TaskCanceledException ex) when (!cancel.IsCancellationRequested)
        {
            throw new UpdateException("Изтеглянето не започна навреме. Опитайте отново.", ex);
        }
        catch (InvalidDataException ex)
        {
            throw new UpdateException("Изтегленият архив не може да бъде отворен.", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UpdateException("Новата версия не можа да бъде записана на диска:\n" + ex.Message, ex);
        }
    }

    private async Task<string> DownloadFileAsync(ReleaseInfo release, string path, IProgress<int>? progress, CancellationToken cancel)
    {
        // The whole download has no time limit, only its start does.
        using var client = new HttpRequestMessage(HttpMethod.Get, release.AssetUrl);
        using var response = await _http.SendAsync(client, HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();

        long total = response.Content.Headers.ContentLength ?? release.AssetSize;
        await using var source = await response.Content.ReadAsStreamAsync(cancel);
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var buffer = new byte[81920];
        long done = 0;
        int shown = -1;
        int read;
        while ((read = await source.ReadAsync(buffer, cancel)) > 0)
        {
            sha.AppendData(buffer, 0, read);
            await file.WriteAsync(buffer.AsMemory(0, read), cancel);
            done += read;
            int percent = total > 0 ? (int)Math.Min(100, done * 100 / total) : 0;
            if (percent != shown) progress?.Report(shown = percent);
        }

        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }

    public void Dispose() => _http.Dispose();
}
