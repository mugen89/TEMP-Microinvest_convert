using System.Globalization;
using Microsoft.Data.Sqlite;

namespace MicroinvestConvert.Data;

/// <summary>The database cannot be opened, read or written – in words for the user.</summary>
public sealed class DatabaseException(string message, Exception? inner = null) : Exception(message, inner)
{
    /// <summary>Another program holds it just now. It is there, and later it can be used.</summary>
    public bool IsBusy => Database.IsBusy(InnerException);
}

/// <summary>
/// The file in which the program keeps the exchange rates, the clients and settings of its own.
/// It is opened for every piece of work and closed after it, so nothing holds it meanwhile.
/// </summary>
public sealed class Database
{
    public const string FileName = "MicroinvestConvert.db";

    private const int Busy = 5, Locked = 6, Corrupt = 11, NotADatabase = 26;

    /// <summary>
    /// The steps of the schema. A change is a new step at the end; the ones that are out are never
    /// edited. Steps only add – a program taken back to an older version has to work with a newer file.
    /// </summary>
    internal static readonly IReadOnlyList<string> Schema =
    [
        """
        CREATE TABLE Settings (
            Key   TEXT NOT NULL PRIMARY KEY,
            Value TEXT NOT NULL
        ) WITHOUT ROWID;

        -- What a source published on one day.
        CREATE TABLE RateTables (
            Date     TEXT NOT NULL PRIMARY KEY,   -- the day the table itself says it is for
            Currency TEXT NOT NULL,               -- EUR, or BGN before 2026
            Fetched  TEXT NOT NULL
        ) WITHOUT ROWID;

        CREATE TABLE Rates (
            Date    TEXT NOT NULL REFERENCES RateTables (Date) ON DELETE CASCADE,
            Code    TEXT NOT NULL,
            Units   TEXT NOT NULL,
            Rate    TEXT NOT NULL,                -- EUR table: units of the currency for 1 EUR; BGN table: leva for the units
            Reverse TEXT NOT NULL,
            PRIMARY KEY (Date, Code)
        ) WITHOUT ROWID;

        -- Every day whose rates are known, and the table it takes them from: a Sunday has Friday's.
        CREATE TABLE RateDays (
            Day  TEXT NOT NULL PRIMARY KEY,
            Date TEXT NOT NULL REFERENCES RateTables (Date) ON DELETE CASCADE
        ) WITHOUT ROWID;

        CREATE TABLE Currencies (
            Code TEXT NOT NULL PRIMARY KEY,
            Name TEXT NOT NULL
        ) WITHOUT ROWID;

        -- Only the name is needed.
        CREATE TABLE Clients (
            Id                 INTEGER PRIMARY KEY,
            Name               TEXT NOT NULL,
            Eik                TEXT NOT NULL DEFAULT '',
            VatNumber          TEXT NOT NULL DEFAULT '',
            Note               TEXT NOT NULL DEFAULT '',
            PurchaseVatAccount TEXT NOT NULL DEFAULT '',
            SaleVatAccount     TEXT NOT NULL DEFAULT '',
            CashAccount        TEXT NOT NULL DEFAULT '',
            Created            TEXT NOT NULL
        );
        CREATE UNIQUE INDEX ClientsByEik ON Clients (Eik) WHERE Eik <> '';

        -- The accounts of a client that are not simply „as in the file".
        CREATE TABLE ClientAccounts (
            ClientId INTEGER NOT NULL REFERENCES Clients (Id) ON DELETE CASCADE,
            Account  TEXT NOT NULL,                       -- in the file
            Target   TEXT NOT NULL DEFAULT '',            -- in Делта Pro; empty = the same
            Currency TEXT NOT NULL DEFAULT '',            -- of a currency account; empty = it is not one
            PRIMARY KEY (ClientId, Account)
        ) WITHOUT ROWID;
        """,
    ];

    private readonly string _connection;

    public string Path { get; }

    private Database(string path)
    {
        Path = path;
        _connection = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = false,
            DefaultTimeout = 3,
        }.ToString();
    }

    /// <summary>Opens the database, made when there is none and brought up to date when it is older.</summary>
    /// <exception cref="DatabaseException">It cannot be written there, or the file is something else.</exception>
    public static Database Open(string path)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
            var database = new Database(path);
            database.Upgrade();
            return database;
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            throw Explain(ex, path);
        }
    }

    /// <summary>The same; a damaged file is put aside and a new one is started. <paramref name="problem"/> says so.</summary>
    public static Database OpenOrReplace(string path, out string problem)
    {
        problem = "";
        try
        {
            return Open(path);
        }
        catch (DatabaseException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: Corrupt or NotADatabase })
        {
            string aside = path + ".bad-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            try
            {
                File.Move(path, aside);
            }
            catch (Exception move) when (move is IOException or UnauthorizedAccessException)
            {
                throw Explain(move, path);
            }

            problem = $"Базата данни беше повредена и е започната нова. Старата е запазена като {System.IO.Path.GetFileName(aside)}.";
            return Open(path);
        }
    }

    private static DatabaseException Explain(Exception ex, string path) => new(IsBusy(ex)
        ? $"Базата данни е заета от друга програма. Стартирайте отново по-късно.\n{path}"
        : $"Базата данни не може да бъде отворена: {ex.Message}\n{path}", ex);

    internal static bool IsBusy(Exception? ex) => ex is SqliteException { SqliteErrorCode: Busy or Locked };

    /// <summary>Does a piece of work with an open connection.</summary>
    internal T Use<T>(Func<SqliteConnection, T> work)
    {
        try
        {
            using var connection = new SqliteConnection(_connection);
            connection.Open();
            return work(connection);
        }
        catch (SqliteException ex)
        {
            throw new DatabaseException(IsBusy(ex)
                ? $"Базата данни е заета от друга програма. Опитайте отново.\n{Path}"
                : $"Базата данни не може да бъде прочетена или записана: {ex.Message}\n{Path}", ex);
        }
    }

    /// <summary>The same, all or nothing.</summary>
    internal void Change(Action<SqliteConnection> work) => Use(connection =>
    {
        using var transaction = connection.BeginTransaction();
        work(connection);
        transaction.Commit();
        return 0;
    });

    private void Upgrade()
    {
        using var connection = new SqliteConnection(_connection);
        connection.Open();

        using var versionOf = connection.CreateCommand();
        versionOf.CommandText = "PRAGMA user_version";
        int version = Convert.ToInt32(versionOf.ExecuteScalar(), CultureInfo.InvariantCulture);

        // A file of a newer program is used as it is: newer schemas only add.
        if (version >= Schema.Count)
        {
            RequireWritable(connection, version);
            return;
        }

        if (version > 0) File.Copy(Path, $"{Path}.v{version}", overwrite: true);   // to go back to, if the steps fail

        using var transaction = connection.BeginTransaction();
        foreach (string step in Schema.Skip(version)) connection.Execute(step);
        connection.Execute($"PRAGMA user_version = {Schema.Count}");
        transaction.Commit();
    }

    // Only writing says whether it can be written: a transaction is begun in a read-only file too.
    private static void RequireWritable(SqliteConnection connection, int version)
    {
        try
        {
            connection.Execute($"PRAGMA user_version = {version}");
        }
        catch (SqliteException ex) when (IsBusy(ex))
        {
            // Another program is writing just now, so it can be written.
        }
    }
}

/// <summary>Commands in one line.</summary>
internal static class SqliteExtensions
{
    public static SqliteCommand Command(this SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }

    public static void Execute(this SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        using var command = connection.Command(sql, parameters);
        command.ExecuteNonQuery();
    }

    public static List<T> Read<T>(this SqliteConnection connection, string sql, Func<SqliteDataReader, T> row, params (string Name, object Value)[] parameters)
    {
        using var command = connection.Command(sql, parameters);
        using var reader = command.ExecuteReader();
        var rows = new List<T>();
        while (reader.Read()) rows.Add(row(reader));
        return rows;
    }
}

/// <summary>Days and numbers as they are kept: sortable text, the same on every computer.</summary>
internal static class Stored
{
    public static string Day(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static DateTime Day(string text) => DateTime.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    public static decimal Number(string text) => decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture);
    public static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
