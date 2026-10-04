using System.Net;
using Microsoft.Extensions.Logging;
using Octokit;

namespace MantisGithubMigrator.GitHub;

public sealed class RateLimiter
{
    // GitHub allows 500 content-creating requests per hour, stay a bit under that
    private const int MaxWritesPerHour = 450;
    private const int MaxRetries = 5;

    // GitHub recommends at least a second between writes, which also keeps us under the 80 per minute limit
    private static readonly TimeSpan WriteInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RetryBuffer = TimeSpan.FromSeconds(5);

    private readonly List<DateTimeOffset> _writes = [];
    private readonly ILogger<RateLimiter> _logger;

    public RateLimiter(ILogger<RateLimiter> logger)
    {
        _logger = logger;
    }

    // Any request that hits a rate limit (primary or secondary) is retried once the limit allows it again.
    // On top of that, enforceWriteLimit paces content-creating requests like creating the issues under GitHub's content creation limit.
    // This is the only other rate limit the tool might run into.
    // Reads are too few to need pacing.
    public async Task<T> RunAsync<T>(Func<Task<T>> request, bool enforceWriteLimit)
    {
        for (var attempt = 1; ; attempt++)
        {
            if (enforceWriteLimit)
                await WaitForWriteAsync();

            try
            {
                return await request();
            }
            catch (ApiException ex) when (attempt <= MaxRetries && GetRetryDelay(ex, attempt) is { } delay)
            {
                // A rate limited request is never processed, so it's safe to send it again
                _logger.LogWarning("Rate limited ({StatusCode}), retrying in {Delay:hh\\:mm\\:ss} (attempt {Attempt}/{MaxRetries}).",
                    (int)ex.StatusCode, delay, attempt, MaxRetries);
                await Task.Delay(delay);
            }
        }
    }

    private async Task WaitForWriteAsync()
    {
        var now = DateTimeOffset.UtcNow;
        _writes.RemoveAll(w => w < now.AddHours(-1));

        var waitUntil = _writes.Count > 0 ? _writes[^1] + WriteInterval : now;

        if (_writes.Count >= MaxWritesPerHour)
        {
            // Wait until the oldest write we still count drops out of the hour
            waitUntil = _writes[^MaxWritesPerHour].AddHours(1);
            _logger.LogInformation("Hourly write limit of {MaxWritesPerHour} reached, waiting {Delay:hh\\:mm\\:ss}.", MaxWritesPerHour, waitUntil - now);
        }

        if (waitUntil > now)
            await Task.Delay(waitUntil - now);

        _writes.Add(DateTimeOffset.UtcNow);
    }

    private static TimeSpan? GetRetryDelay(ApiException ex, int attempt)
    {
        // GitHub rate limits with a 403 or a 429, but Octokit only turns a 403 into a rate limit exception
        var isRateLimit = ex is RateLimitExceededException or SecondaryRateLimitExceededException or AbuseException
            || ex.StatusCode == HttpStatusCode.TooManyRequests;

        if (!isRateLimit)
            return null;

        if (int.TryParse(GetHeader(ex, "Retry-After"), out var retryAfter))
            return TimeSpan.FromSeconds(retryAfter) + RetryBuffer;

        if (GetHeader(ex, "X-RateLimit-Remaining") == "0" && long.TryParse(GetHeader(ex, "X-RateLimit-Reset"), out var reset))
        {
            var untilReset = DateTimeOffset.FromUnixTimeSeconds(reset) - DateTimeOffset.UtcNow;
            return untilReset > TimeSpan.Zero ? untilReset + RetryBuffer : RetryBuffer;
        }

        // No hint from GitHub, wait a minute and double it on every retry
        return TimeSpan.FromMinutes(Math.Pow(2, attempt - 1));
    }

    // Header casing differs between HTTP/1.1 and HTTP/2
    private static string? GetHeader(ApiException ex, string name)
        => ex.HttpResponse?.Headers.FirstOrDefault(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
}
