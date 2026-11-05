using System.Net;
using System.Net.Http.Json;
using IndexNow.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IndexNow.Submission;

internal sealed class IndexNowClient(HttpClient httpClient, IOptions<IndexNowOptions> options, ILogger<IndexNowClient> logger)
    : IIndexNowClient
{
    private static readonly TimeSpan _maxRetryAfter = TimeSpan.FromMinutes(1);

    /// <summary>The waits between attempts on a temporary failure.</summary>
    internal TimeSpan[] RetryDelays { get; set; } = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)];

    public async Task<bool> SubmitAsync(IndexNowBatch batch, CancellationToken cancellationToken)
    {
        var payload = new { host = batch.Host, key = options.Value.Key, urlList = batch.Urls };

        for (var attempt = 0; ; attempt++)
        {
            TimeSpan? retryAfter = null;
            string failure;
            try
            {
                using HttpResponseMessage response = await httpClient.PostAsJsonAsync(options.Value.Endpoint, payload, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    logger.LogInformation("IndexNow accepted {Count} URLs for {Host} ({StatusCode})", batch.Urls.Count, batch.Host, (int)response.StatusCode);
                    logger.LogDebug("IndexNow URLs for {Host}: {Urls}", batch.Host, batch.Urls);
                    return true;
                }

                if (!IsTemporary(response.StatusCode))
                {
                    logger.LogWarning(
                        "IndexNow refused {Count} URLs for {Host}: {StatusCode}. {Hint}",
                        batch.Urls.Count,
                        batch.Host,
                        (int)response.StatusCode,
                        HintFor(response.StatusCode));
                    return false;
                }

                retryAfter = response.Headers.RetryAfter?.Delta;
                failure = $"HTTP {(int)response.StatusCode}";
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                failure = exception.GetType().Name;
            }

            if (attempt >= RetryDelays.Length)
            {
                logger.LogWarning("IndexNow gave up on {Count} URLs for {Host}: {Failure}", batch.Urls.Count, batch.Host, failure);
                return false;
            }

            TimeSpan delay = retryAfter is { } wait && wait > RetryDelays[attempt]
                ? (wait < _maxRetryAfter ? wait : _maxRetryAfter)
                : RetryDelays[attempt];
            await Task.Delay(delay, cancellationToken);
        }
    }

    private static bool IsTemporary(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;

    private static string HintFor(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.BadRequest => "The request is malformed.",
        HttpStatusCode.Forbidden => "The key was not valid: check that /{key}.txt on this host answers 200 with the key, without a redirect.",
        HttpStatusCode.UnprocessableEntity => "A URL does not belong to the host, or the key does not match the key file.",
        _ => "See https://www.indexnow.org/documentation.",
    };
}
