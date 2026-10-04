using System.Globalization;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using Cake.Core;
using Cake.Core.Diagnostics;

namespace Cake.Download.Module.Http;

/// <summary>
/// Streams a URL to a file while hashing it. Follows https-only redirects itself, retries transient failures and
/// aborts an attempt when no bytes arrive for <see cref="DownloadOptions.StallTimeout"/>. Never sends credentials.
/// </summary>
internal sealed class HttpDownloader
{
    private static readonly string UserAgent = $"Cake.Download.Module/{GetVersion()}";

    private readonly HttpClient _client;
    private readonly ICakeLog _log;
    private readonly DownloadOptions _options;

    public HttpDownloader(HttpMessageHandler handler, ICakeLog log, DownloadOptions options)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _client = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public DownloadResult Download(Uri url, string destinationPath, long? maxBytes = null) =>
        DownloadAsync(url, destinationPath, maxBytes).GetAwaiter().GetResult();

    private static string GetVersion()
    {
        var informational = typeof(HttpDownloader).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var plus = informational?.IndexOf('+', StringComparison.Ordinal) ?? -1;
        var version = plus >= 0 ? informational![..plus] : informational;
        return string.IsNullOrWhiteSpace(version) ? "0.0.0" : version;
    }

    private static bool IsRedirect(HttpStatusCode status) => (int)status is 301 or 302 or 303 or 307 or 308;

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
        {
            return delta;
        }

        return header?.Date is { } date ? date - DateTimeOffset.UtcNow : null;
    }

    private async Task<DownloadResult> DownloadAsync(Uri url, string destinationPath, long? maxBytes)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= _options.MaxAttempts; attempt++)
        {
            TimeSpan? retryAfter = null;
            try
            {
                return await DownloadOnceAsync(url, destinationPath, maxBytes).ConfigureAwait(false);
            }
            catch (RetryableException exception)
            {
                lastError = exception;
                retryAfter = exception.RetryAfter;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or TimeoutException)
            {
                lastError = exception;
            }

            if (attempt < _options.MaxAttempts)
            {
                var delay = retryAfter is { } wait
                    ? TimeSpan.FromTicks(Math.Clamp(wait.Ticks, 0, _options.MaxRetryAfter.Ticks))
                    : _options.Backoff(attempt);
                _log.Verbose(
                    "Attempt {0} of {1} to download {2} failed: {3} Retrying in {4:0.#} s.",
                    attempt,
                    _options.MaxAttempts,
                    url,
                    lastError.Message,
                    delay.TotalSeconds);
                await _options.Delay(delay, CancellationToken.None).ConfigureAwait(false);
            }
        }

        throw new CakeException($"Downloading {url} failed after {_options.MaxAttempts} attempts: {lastError!.Message}", lastError);
    }

    private async Task<DownloadResult> DownloadOnceAsync(Uri url, string destinationPath, long? maxBytes)
    {
        using var stall = new CancellationTokenSource();
        using var response = await SendFollowingRedirectsAsync(url, stall).ConfigureAwait(false);

        var status = (int)response.StatusCode;
        if (status is 408 or 429 or >= 500)
        {
            throw new RetryableException($"HTTP {status} ({response.ReasonPhrase}).", RetryAfter(response));
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new DownloadHttpException(url, response.StatusCode, $"Downloading {url} failed: HTTP {status} ({response.ReasonPhrase}).");
        }

        if (maxBytes is { } limit && response.Content.Headers.ContentLength > limit)
        {
            throw TooLarge(url, limit);
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long length = 0;
        {
            await using var source = await response.Content.ReadAsStreamAsync(stall.Token).ConfigureAwait(false);
            await using var target = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            while (true)
            {
                stall.CancelAfter(_options.StallTimeout);
                int read;
                try
                {
                    read = await source.ReadAsync(buffer, stall.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stall.IsCancellationRequested)
                {
                    throw Stalled(url);
                }

                if (read == 0)
                {
                    break;
                }

                length += read;
                if (maxBytes is { } max && length > max)
                {
                    throw TooLarge(url, max);
                }

                hash.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
            }
        }

        return new DownloadResult(destinationPath, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), length);
    }

    private async Task<HttpResponseMessage> SendFollowingRedirectsAsync(Uri url, CancellationTokenSource stall)
    {
        var current = url;
        for (var redirects = 0; ; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            stall.CancelAfter(_options.StallTimeout);

            HttpResponseMessage response;
            try
            {
                response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stall.IsCancellationRequested)
            {
                throw Stalled(current);
            }

            if (!IsRedirect(response.StatusCode))
            {
                return response;
            }

            var location = response.Headers.Location;
            response.Dispose();
            if (location is null)
            {
                throw new CakeException($"{current} redirected without a Location header.");
            }

            var next = location.IsAbsoluteUri ? location : new Uri(current, location);
            if (next.Scheme != Uri.UriSchemeHttps)
            {
                throw new CakeException($"{current} redirected to {next}, which is not https. Only https downloads are supported.");
            }

            if (redirects + 1 > _options.MaxRedirects)
            {
                throw new CakeException($"{url} redirected more than {_options.MaxRedirects} times.");
            }

            _log.Verbose("{0} redirected to {1}.", current.GetLeftPart(UriPartial.Path), next.GetLeftPart(UriPartial.Path));
            current = next;
        }
    }

    private TimeoutException Stalled(Uri url) =>
        new(string.Create(CultureInfo.InvariantCulture, $"No data received from {url} for {_options.StallTimeout.TotalSeconds:0.#} s."));

    private static CakeException TooLarge(Uri url, long limit) => new($"{url} is larger than the {limit} bytes allowed.");

    private sealed class RetryableException : Exception
    {
        public RetryableException(string message, TimeSpan? retryAfter)
            : base(message)
        {
            RetryAfter = retryAfter;
        }

        public TimeSpan? RetryAfter { get; }
    }
}
