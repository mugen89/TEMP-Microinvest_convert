using MicroinvestConvert.Core.Settings;

namespace MicroinvestConvert.App;

/// <summary>What the windows share: the settings and where they are kept.</summary>
internal sealed class AppState
{
    private readonly SettingsStore _store;

    public SettingsFile Settings { get; private set; }

    /// <summary>Told once, when the settings could not be read.</summary>
    public string LoadProblem => _store.LoadProblem;

    /// <summary>The settings were changed in some window: every open file is calculated again.</summary>
    public event Action? SettingsChanged;

    public void RaiseSettingsChanged() => SettingsChanged?.Invoke();

    public AppState(string settingsPath)
    {
        _store = new SettingsStore(settingsPath);
        Settings = _store.Load();
    }

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
}
