namespace MicroinvestConvert.Core.Exchange;

/// <summary>The official rate of a currency on a day.</summary>
/// <param name="Currency">The code of the currency.</param>
/// <param name="Day">The day it was asked for.</param>
/// <param name="Published">The day of the table it is from: on a Sunday that is Friday.</param>
/// <param name="PerEuro">Units of the currency for one euro, as the ECB publishes it. Amounts are divided by it.</param>
public sealed record ExchangeRate(string Currency, DateTime Day, DateTime Published, decimal PerEuro);

/// <summary>Where the rules look rates up.</summary>
public interface IExchangeRates
{
    /// <summary>The rate of a currency on a day; null when it is not known.</summary>
    ExchangeRate? Find(string currency, DateTime day);

    /// <summary>The table of that day is known, whichever currencies are in it.</summary>
    bool Knows(DateTime day);
}

/// <summary>Nothing has been downloaded.</summary>
public sealed class NoRates : IExchangeRates
{
    public static readonly NoRates Instance = new();

    public ExchangeRate? Find(string currency, DateTime day) => null;
    public bool Knows(DateTime day) => false;
}

/// <summary>One currency of a table, with the numbers of the source.</summary>
/// <param name="Code">The code of the currency.</param>
/// <param name="Name">Its name in Bulgarian.</param>
/// <param name="Units">How many units of it the rate is for.</param>
/// <param name="Rate">In a table in euro: units of the currency for one euro. In a table in leva: leva for the units.</param>
/// <param name="Reverse">The same the other way round; rounded by the source, so nothing is calculated with it.</param>
public sealed record RateRow(string Code, string Name, decimal Units, decimal Rate, decimal Reverse)
{
    /// <summary>Units of the currency for one euro.</summary>
    public decimal PerEuro(string tableCurrency) =>
        tableCurrency == Currencies.Euro ? Rate : Currencies.LevaPerEuro * Units / Rate;
}

/// <summary>The rates published on one day.</summary>
/// <param name="Date">The day the table itself says it is for.</param>
/// <param name="Currency">EUR since 01.01.2026, BGN before that.</param>
/// <param name="Rows">Its currencies.</param>
public sealed record RateTable(DateTime Date, string Currency, IReadOnlyList<RateRow> Rows);

/// <summary>Where the tables are kept.</summary>
public interface IRateStore : IExchangeRates
{
    /// <summary>The days whose table is known for good.</summary>
    IReadOnlySet<DateTime> Days();

    /// <summary>The table a day takes its rates from; null when it is not known.</summary>
    DateTime? TableOf(DateTime day);

    /// <summary>The newest table; null when there is none.</summary>
    DateTime? Latest();

    /// <param name="tables">Tables, new or again.</param>
    /// <param name="days">Every day that became known, with the date of its table.</param>
    void Save(IReadOnlyCollection<RateTable> tables, IReadOnlyDictionary<DateTime, DateTime> days);
}
