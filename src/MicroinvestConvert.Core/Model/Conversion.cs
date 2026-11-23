namespace MicroinvestConvert.Core.Model;

/// <summary>Where the euro of an amount in another currency comes from.</summary>
public enum RateOrigin
{
    /// <summary>The file gives the amount in euro too.</summary>
    File,

    /// <summary>The official rate of a day.</summary>
    Official,

    /// <summary>A rate typed in.</summary>
    ByHand,

    /// <summary>The lev, whose rate does not change.</summary>
    Fixed,
}

/// <summary>How the amounts of a document in one currency become euro.</summary>
/// <param name="Currency">The currency of the amounts.</param>
/// <param name="Origin">Where the euro come from.</param>
/// <param name="PerEuro">Units of the currency for one euro; what the amounts come to when the file gives the euro.</param>
/// <param name="Day">The day of the rate; null when none is used.</param>
/// <param name="Published">The day of the table the rate is from; null when there is none.</param>
/// <param name="Official">The official rate of <paramref name="Day"/>, to compare with; null when it is not known.</param>
public sealed record Conversion(
    string Currency, RateOrigin Origin, decimal PerEuro, DateTime? Day = null, DateTime? Published = null, decimal? Official = null);
