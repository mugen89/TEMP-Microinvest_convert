using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;

namespace MicroinvestConvert.Core.Exchange;

/// <summary>
/// The tables of kik-info.com – the rates of БНБ, one page per day. Anonymous: the only thing
/// sent is the day.
/// </summary>
public sealed class KikInfoSource : IRateSource, IDisposable
{
    public const string Host = "kik-info.com";
    private const string Page = "https://" + Host + "/spravochnik/currency-rates.php?date=";

    /// <summary>Asked only when the site does not answer, to know which of the two is missing.</summary>
    private const string SomewhereElse = "https://github.com/";

    private const string Client = "Mozilla/5.0 (compatible; currency-rates)";

    private readonly HttpClient _http;
    private readonly Func<CancellationToken, Task<bool>> _online;

    public string Name => Host;

    public KikInfoSource(HttpMessageHandler? handler = null, Func<CancellationToken, Task<bool>>? online = null)
    {
        _http = new HttpClient(handler ?? new SocketsHttpHandler
        {
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10),
        })
        {
            Timeout = TimeSpan.FromSeconds(25),
            MaxResponseContentBufferSize = 4_000_000,   // the page is 100 KB
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(Client);
        _online = online ?? IsOnlineAsync;
    }

    public async Task<RateTable> GetAsync(DateTime day, CancellationToken cancel)
    {
        RateTable table;
        try
        {
            using var response = await _http.GetAsync(Page + day.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture), cancel).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new RateException(RateFailure.SiteDown, $"{Host} отговори с грешка {(int)response.StatusCode}. Опитайте отново по-късно.");
            table = KikInfoPage.Parse(await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false));
        }
        catch (HttpRequestException ex)
        {
            throw await WhyAsync(ex, cancel).ConfigureAwait(false);
        }
        catch (TaskCanceledException ex) when (!cancel.IsCancellationRequested)
        {
            throw await WhyAsync(ex, cancel).ConfigureAwait(false);   // it took too long
        }

        // It shows the last table there is, never a later one.
        return table.Date <= day.Date
            ? table
            : throw new RateException(RateFailure.BadPage, $"{Host} отговори с курсове за {table.Date:dd.MM.yyyy}, а са поискани за {day:dd.MM.yyyy}.");
    }

    private async Task<RateException> WhyAsync(Exception ex, CancellationToken cancel) =>
        await _online(cancel).ConfigureAwait(false)
            ? new RateException(RateFailure.SiteDown, $"{Host} не отговаря. Опитайте отново по-късно.", ex)
            : new RateException(RateFailure.NoInternet, "Курсовете не могат да бъдат изтеглени. Проверете връзката и опитайте отново.", ex);

    private static async Task<bool> IsOnlineAsync(CancellationToken cancel)
    {
        if (!NetworkInterface.GetIsNetworkAvailable()) return false;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(Client);
            using var request = new HttpRequestMessage(HttpMethod.Head, SomewhereElse);
            using var response = await http.SendAsync(request, cancel).ConfigureAwait(false);
            return true;   // any answer is an answer
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested)
        {
            return false;
        }
    }

    public void Dispose() => _http.Dispose();
}
