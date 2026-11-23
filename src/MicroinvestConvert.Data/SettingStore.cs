namespace MicroinvestConvert.Data;

/// <summary>Settings that stay with the database, by name.</summary>
public sealed class SettingStore(Database database)
{
    /// <summary>Null when it was never set, or was cleared.</summary>
    public string? Get(string key) => database.Use(c =>
        c.Read("SELECT Value FROM Settings WHERE Key = $key", r => r.GetString(0), ("$key", key)).FirstOrDefault());

    /// <summary>Null clears it.</summary>
    public void Set(string key, string? value) => database.Change(c =>
    {
        if (value == null) c.Execute("DELETE FROM Settings WHERE Key = $key", ("$key", key));
        else
            c.Execute("INSERT INTO Settings (Key, Value) VALUES ($key, $value) ON CONFLICT (Key) DO UPDATE SET Value = $value",
                ("$key", key), ("$value", value));
    });
}
