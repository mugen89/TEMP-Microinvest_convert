using MicroinvestConvert.Core.Exchange;
using MicroinvestConvert.Core.Model;
using MicroinvestConvert.Core.Settings;
using MicroinvestConvert.Data;

namespace MicroinvestConvert.App;

/// <summary>What the windows share: the settings, the clients and the exchange rates, and where they are kept.</summary>
internal sealed class AppState : IDisposable
{
    private const string ClientsTakenKey = "clients.taken";

    private readonly SettingsStore _store;

    public SettingsFile Settings { get; private set; }

    /// <summary>Told once, when the settings could not be read.</summary>
    public string LoadProblem => _store.LoadProblem;

    /// <summary>The file of the database; empty when none can be opened.</summary>
    public string DatabasePath { get; private set; } = "";

    /// <summary>Told once, when the database could not be opened or had to be started anew.</summary>
    public string DatabaseProblem { get; private set; } = "";

    public ClientBook Clients { get; }
    public RatesService Rates { get; }

    /// <summary>What „Валутни курсове" showed last, for the next time it is opened.</summary>
    public (RateView View, string Currency) RatesShown { get; set; } = (RateView.Day, "USD");

    /// <summary>The settings were changed in some window: every open file is calculated again.</summary>
    public event Action? SettingsChanged;

    public void RaiseSettingsChanged() => SettingsChanged?.Invoke();

    /// <param name="settingsPath">The settings file.</param>
    /// <param name="databasePaths">Where the database may be, the best place first; otherwise it is beside the settings.</param>
    /// <param name="rateSource">Where exchange rates come from.</param>
    /// <param name="pause">How long the download waits before it tries a page again.</param>
    public AppState(string settingsPath, IReadOnlyList<string>? databasePaths = null, IRateSource? rateSource = null, TimeSpan? pause = null)
    {
        _store = new SettingsStore(settingsPath);
        Settings = _store.Load();

        var database = OpenDatabase(databasePaths ?? [Path.Combine(Path.GetDirectoryName(settingsPath)!, Database.FileName)]);
        var settings = database != null ? new SettingStore(database) : null;
        Clients = LoadClients(database != null ? new ClientStore(database) : null, settings);
        Rates = new RatesService(database != null ? new RateStore(database) : null, settings, rateSource ?? new KikInfoSource(), pause);
    }

    private Database? OpenDatabase(IReadOnlyList<string> paths)
    {
        foreach (string path in paths)
        {
            try
            {
                var database = Database.OpenOrReplace(path, out string problem);
                DatabaseProblem = problem;
                DatabasePath = path;
                return database;
            }
            catch (DatabaseException ex)
            {
                DatabaseProblem = ex.Message;
                if (ex.IsBusy) break;   // it is there: one in another place would only hide what is in it
            }
        }

        return null;
    }

    private ClientBook LoadClients(ClientStore? store, SettingStore? settings)
    {
        try
        {
            var clients = new ClientBook(store);
            if (settings != null && settings.Get(ClientsTakenKey) == null)
            {
                // What 1.1 kept in its settings file. It stays there too, for going back to 1.1.
                foreach (var (eik, client) in Settings.Clients)
                    if (clients.Find(eik, client.Name) == null) clients.Save(client.ToClient(eik));
                settings.Set(ClientsTakenKey, "1");
            }

            return clients;
        }
        catch (DatabaseException ex)
        {
            DatabaseProblem = ex.Message;
            return new ClientBook(null);
        }
    }

    /// <summary>The settings in force for a file.</summary>
    public ConverterOptions OptionsFor(OperationBatch batch) => new(Settings.General, Clients.Find(batch.ClientEik, batch.ClientName));

    /// <summary>Saves the settings. A failure is reported, never thrown.</summary>
    public string? Save()
    {
        try
        {
            _store.Save(Settings);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    /// <summary>Saves a client. A failure is reported, never thrown.</summary>
    public string? Save(ClientSettings client) => Try(() => Clients.Save(client));

    /// <summary>Deletes a client. A failure is reported, never thrown.</summary>
    public string? Delete(ClientSettings client) => Try(() => Clients.Delete(client));

    private static string? Try(Action work)
    {
        try
        {
            work();
            return null;
        }
        catch (DatabaseException ex)
        {
            return ex.Message;
        }
    }

    public void Dispose() => Rates.Dispose();
}
