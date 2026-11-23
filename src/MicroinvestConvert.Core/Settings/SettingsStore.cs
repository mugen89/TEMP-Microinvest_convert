using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MicroinvestConvert.Core.Settings;

/// <summary>Everything the program remembers between two starts.</summary>
public sealed class SettingsFile
{
    /// <summary>2 = since 1.1.0, when two defaults changed.</summary>
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;
    public ConverterSettings General { get; set; } = new();

    /// <summary>Exceptions per client, by ЕИК.</summary>
    public Dictionary<string, ClientSettings> Clients { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool OpenAfterSave { get; set; } = true;
    public bool CheckForUpdates { get; set; } = true;
    public List<string> RecentFiles { get; set; } = [];

    public ConverterOptions OptionsFor(string? clientEik)
    {
        ClientSettings? client = null;
        if (!string.IsNullOrWhiteSpace(clientEik)) Clients.TryGetValue(clientEik.Trim(), out client);
        return new ConverterOptions(General, client);
    }

    /// <summary>The exceptions of a client, created when missing. Null without an ЕИК.</summary>
    public ClientSettings? ClientFor(string? clientEik, string? name)
    {
        if (string.IsNullOrWhiteSpace(clientEik)) return null;
        string key = clientEik.Trim();
        if (!Clients.TryGetValue(key, out var client)) Clients[key] = client = new ClientSettings();
        if (!string.IsNullOrWhiteSpace(name)) client.Name = name;
        return client;
    }

    /// <summary>Clients without exceptions are not worth keeping.</summary>
    public void DropEmptyClients()
    {
        foreach (string key in Clients.Where(c => c.Value.IsEmpty).Select(c => c.Key).ToList())
            Clients.Remove(key);
    }

    public void AddRecent(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > 8) RecentFiles.RemoveRange(8, RecentFiles.Count - 8);
    }
}

/// <summary>Loads and saves the settings as JSON. A damaged file never stops the program.</summary>
public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Path { get; } = path;

    /// <summary>Why the last <see cref="Load"/> fell back to the defaults; empty when it did not.</summary>
    public string LoadProblem { get; private set; } = "";

    public SettingsFile Load()
    {
        LoadProblem = "";
        try
        {
            if (!File.Exists(Path)) return new SettingsFile();
            var file = JsonSerializer.Deserialize<SettingsFile>(File.ReadAllText(Path), Json) ?? new SettingsFile();
            return Repair(file);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            LoadProblem = ex.Message;
            KeepDamagedCopy();
            return new SettingsFile();
        }
    }

    public void Save(SettingsFile file)
    {
        file.DropEmptyClients();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);

        // Written aside and swapped in, so a power cut cannot leave half a file.
        string temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(file, Json));
        File.Move(temp, Path, overwrite: true);
    }

    private void KeepDamagedCopy()
    {
        try
        {
            if (File.Exists(Path)) File.Copy(Path, Path + ".bad", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing more can be done; the defaults are in force.
        }
    }

    // A hand-edited file may hold nulls and out-of-range values.
    private static SettingsFile Repair(SettingsFile file)
    {
        file.General ??= new ConverterSettings();
        var g = file.General;
        g.PurchaseVatAccount = Or(g.PurchaseVatAccount, ConverterSettings.DefaultPurchaseVatAccount);
        g.SaleVatAccount = Or(g.SaleVatAccount, ConverterSettings.DefaultSaleVatAccount);
        g.DefaultCashAccount ??= "";
        g.Accounts = new Dictionary<string, string>(g.Accounts ?? [], StringComparer.OrdinalIgnoreCase);
        g.ChecksOff = (g.ChecksOff ?? []).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
        g.TolerancePerLine = Math.Clamp(g.TolerancePerLine, 0m, 1m);
        g.TolerancePerDocument = Math.Clamp(g.TolerancePerDocument, 0m, 10m);

        // 1.0 wrote every setting, also the untouched ones: what is still the old default moves on.
        if (file.Version < SettingsFile.CurrentVersion)
        {
            if (g.VatLines == VatLines.PerDealType) g.VatLines = VatLines.PerAccount;
            if (g.NoDiary == NoDiaryMode.Internal) g.NoDiary = NoDiaryMode.NoCredit;
            file.Version = SettingsFile.CurrentVersion;
        }

        var clients = new Dictionary<string, ClientSettings>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, client) in file.Clients ?? [])
        {
            if (client is null || string.IsNullOrWhiteSpace(key)) continue;
            client.Name ??= "";
            client.PurchaseVatAccount ??= "";
            client.SaleVatAccount ??= "";
            client.Accounts = new Dictionary<string, string>(client.Accounts ?? [], StringComparer.OrdinalIgnoreCase);
            clients[key.Trim()] = client;
        }

        file.Clients = clients;
        file.RecentFiles = (file.RecentFiles ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        return file;
    }

    private static string Or(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
