using MicroinvestConvert.Core.Exchange;

namespace MicroinvestConvert.Data;

/// <summary>
/// The exchange rates in the database. What was read stays in memory, because the rules ask again
/// after every correction. Used from several threads while rates are being downloaded. To the rules
/// a database that cannot be read is one without rates: the documents wait for them.
/// </summary>
public sealed class RateStore(Database database) : IRateStore
{
    private sealed record Table(string Currency, Dictionary<string, RateRow> Rows);

    private readonly Lock _lock = new();
    private readonly Dictionary<DateTime, Table> _tables = [];
    private Dictionary<DateTime, DateTime>? _days;   // day → its table; replaced and never changed

    public ExchangeRate? Find(string currency, DateTime day)
    {
        try
        {
            return TableOf(day) is { } published && Load(published) is { } table && table.Rows.TryGetValue(currency, out var row)
                ? new ExchangeRate(currency, day.Date, published, row.PerEuro(table.Currency))
                : null;
        }
        catch (DatabaseException)
        {
            return null;
        }
    }

    public bool Knows(DateTime day)
    {
        try { return Known().ContainsKey(day.Date); }
        catch (DatabaseException) { return false; }
    }

    public IReadOnlySet<DateTime> Days() => Known().Keys.ToHashSet();

    public DateTime? TableOf(DateTime day) => Known().TryGetValue(day.Date, out var table) ? table : null;

    public DateTime? Latest() => Known() is { Count: > 0 } days ? days.Values.Max() : null;

    /// <summary>The rates of a day, by code; none when it is not known.</summary>
    public IReadOnlyList<(RateRow Row, decimal PerEuro)> RatesOf(DateTime day) =>
        TableOf(day) is { } published && Load(published) is { } table
            ? table.Rows.Values.OrderBy(r => r.Code, StringComparer.Ordinal).Select(r => (r, r.PerEuro(table.Currency))).ToList()
            : [];

    /// <summary>Every currency there has been, by code.</summary>
    public IReadOnlyList<(string Code, string Name)> Currencies() =>
        database.Use(c => c.Read("SELECT Code, Name FROM Currencies ORDER BY Code", r => (r.GetString(0), r.GetString(1))));

    /// <summary>The rate of a currency on every known day, the newest first.</summary>
    public IReadOnlyList<ExchangeRate> History(string currency) => database.Use(c => c.Read(
        """
        SELECT d.Day, d.Date, t.Currency, r.Units, r.Rate, r.Reverse
        FROM RateDays d JOIN RateTables t ON t.Date = d.Date JOIN Rates r ON r.Date = d.Date AND r.Code = $code
        ORDER BY d.Day DESC
        """,
        r => new ExchangeRate(currency, Stored.Day(r.GetString(0)), Stored.Day(r.GetString(1)),
            new RateRow(currency, "", Stored.Number(r.GetString(3)), Stored.Number(r.GetString(4)), Stored.Number(r.GetString(5))).PerEuro(r.GetString(2))),
        ("$code", currency)));

    public void Save(IReadOnlyCollection<RateTable> tables, IReadOnlyDictionary<DateTime, DateTime> days)
    {
        database.Change(c =>
        {
            string now = Stored.Now();
            foreach (var table in tables)
            {
                string date = Stored.Day(table.Date);
                c.Execute("INSERT INTO RateTables (Date, Currency, Fetched) VALUES ($date, $currency, $now) " +
                          "ON CONFLICT (Date) DO UPDATE SET Currency = $currency, Fetched = $now",
                    ("$date", date), ("$currency", table.Currency), ("$now", now));
                c.Execute("DELETE FROM Rates WHERE Date = $date", ("$date", date));
                foreach (var row in table.Rows)
                {
                    c.Execute("INSERT INTO Rates (Date, Code, Units, Rate, Reverse) VALUES ($date, $code, $units, $rate, $reverse)",
                        ("$date", date), ("$code", row.Code), ("$units", Stored.Number(row.Units)), ("$rate", Stored.Number(row.Rate)),
                        ("$reverse", Stored.Number(row.Reverse)));
                    c.Execute("INSERT INTO Currencies (Code, Name) VALUES ($code, $name) ON CONFLICT (Code) DO UPDATE SET Name = $name WHERE Name <> $name",
                        ("$code", row.Code), ("$name", row.Name));
                }
            }

            foreach (var (day, table) in days)
                c.Execute("INSERT INTO RateDays (Day, Date) VALUES ($day, $date) ON CONFLICT (Day) DO UPDATE SET Date = $date",
                    ("$day", Stored.Day(day)), ("$date", Stored.Day(table)));
        });

        lock (_lock)
        {
            foreach (var table in tables) _tables.Remove(table.Date);
            if (_days == null) return;
            var all = new Dictionary<DateTime, DateTime>(_days);
            foreach (var (day, table) in days) all[day] = table;
            _days = all;
        }
    }

    // ----- what is in memory -----

    private Dictionary<DateTime, DateTime> Known()
    {
        lock (_lock)
            return _days ??= database.Use(c => c.Read("SELECT Day, Date FROM RateDays", r => (Day: Stored.Day(r.GetString(0)), Table: Stored.Day(r.GetString(1)))))
                .ToDictionary(x => x.Day, x => x.Table);
    }

    private Table? Load(DateTime published)
    {
        lock (_lock)
        {
            if (_tables.TryGetValue(published, out var loaded)) return loaded;

            string date = Stored.Day(published);
            var table = database.Use(c =>
            {
                var currency = c.Read("SELECT Currency FROM RateTables WHERE Date = $date", r => r.GetString(0), ("$date", date));
                if (currency.Count == 0) return null;
                var rows = c.Read(
                    "SELECT r.Code, COALESCE(n.Name, ''), r.Units, r.Rate, r.Reverse FROM Rates r LEFT JOIN Currencies n ON n.Code = r.Code WHERE r.Date = $date",
                    r => new RateRow(r.GetString(0), r.GetString(1), Stored.Number(r.GetString(2)), Stored.Number(r.GetString(3)), Stored.Number(r.GetString(4))),
                    ("$date", date));
                return new Table(currency[0], rows.ToDictionary(r => r.Code, StringComparer.Ordinal));
            });
            if (table != null) _tables[published] = table;
            return table;
        }
    }
}
