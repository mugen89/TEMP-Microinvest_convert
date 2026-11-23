namespace MicroinvestConvert.Core.Settings;

/// <summary>Keeps the clients between two starts.</summary>
public interface IClientStore
{
    IReadOnlyList<ClientSettings> Load();

    /// <summary>A new client gets its <see cref="ClientSettings.Id"/> here.</summary>
    void Save(ClientSettings client);

    void Delete(ClientSettings client);
}

/// <summary>
/// The clients. One needs only a name: Делта Pro does not ask for the ЕИК of the firm. An ЕИК,
/// when it is given, belongs to one client.
/// </summary>
/// <param name="store">Where they are kept; null = only while the program runs.</param>
public sealed class ClientBook(IClientStore? store)
{
    private readonly List<ClientSettings> _clients = [.. store?.Load() ?? []];

    /// <summary>By name.</summary>
    public IReadOnlyList<ClientSettings> All => _clients.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>
    /// The client of a file: by its ЕИК, otherwise by its name – but not one that has another ЕИК.
    /// Null when there is none.
    /// </summary>
    public ClientSettings? Find(string? eik, string? name)
    {
        string number = Clean(eik), key = Key(name);
        if (number.Length > 0 && _clients.Find(c => c.Eik == number) is { } byNumber) return byNumber;
        return key.Length == 0 ? null : _clients.Find(c => Key(c.Name) == key && (c.Eik.Length == 0 || number.Length == 0));
    }

    /// <summary>The client of a file, made when there is none; kept by <see cref="Save"/>.</summary>
    public ClientSettings FindOrNew(string? eik, string? name) => Find(eik, name) ?? new ClientSettings
    {
        Name = Clean(name).Length > 0 ? Clean(name) : Clean(eik),
        Eik = Clean(eik),
    };

    /// <summary>Why a client cannot have this name and number; null when it can.</summary>
    public string? Problem(ClientSettings? client, string name, string eik)
    {
        if (Key(name).Length == 0) return "Фирмата трябва да има име.";
        if (Clean(eik) is { Length: > 0 } number && _clients.Find(c => c != client && c.Eik == number) is { } same)
            return $"ЕИК {number} е на „{same.Name}“.";
        return _clients.Exists(c => c != client && Key(c.Name) == Key(name) && c.Eik == Clean(eik)) ? $"Вече има фирма „{Clean(name)}“." : null;
    }

    public void Save(ClientSettings client)
    {
        store?.Save(client);
        if (!_clients.Contains(client)) _clients.Add(client);
    }

    public void Delete(ClientSettings client)
    {
        if (!_clients.Contains(client)) return;
        store?.Delete(client);
        _clients.Remove(client);
    }

    private static string Clean(string? text) => (text ?? "").Trim();

    // „Фирма" ООД and ФИРМА ООД are the same.
    private static string Key(string? name) =>
        new((name ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
