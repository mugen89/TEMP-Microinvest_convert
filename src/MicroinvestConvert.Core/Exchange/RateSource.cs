namespace MicroinvestConvert.Core.Exchange;

public enum RateFailure
{
    /// <summary>The computer is not connected.</summary>
    NoInternet,

    /// <summary>The internet is there, the site does not answer.</summary>
    SiteDown,

    /// <summary>The site answers with something else than its table.</summary>
    BadPage,
}

/// <summary>Why rates could not be taken from the internet – in words for the user.</summary>
public sealed class RateException(RateFailure failure, string message, Exception? inner = null) : Exception(message, inner)
{
    public RateFailure Failure { get; } = failure;

    /// <summary>The title of the message.</summary>
    public string Title => Failure switch
    {
        RateFailure.NoInternet => "Няма връзка с интернет",
        RateFailure.SiteDown => "Сайтът с курсовете не е намерен",
        _ => "Страницата с курсовете не е разпозната",
    };
}

/// <summary>A place on the internet with the table of any day.</summary>
public interface IRateSource
{
    /// <summary>What the user is told it is.</summary>
    string Name { get; }

    /// <summary>
    /// The table in force on a day. It may be an earlier one – on weekends and holidays, or when
    /// the table of the day is not published yet; its own date says which.
    /// </summary>
    Task<RateTable> GetAsync(DateTime day, CancellationToken cancel);
}
