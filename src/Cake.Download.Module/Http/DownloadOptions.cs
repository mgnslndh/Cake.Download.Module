namespace Cake.Download.Module.Http;

internal sealed record DownloadOptions
{
    public static DownloadOptions Default { get; } = new();

    public TimeSpan StallTimeout { get; init; } = TimeSpan.FromSeconds(60);

    public int MaxAttempts { get; init; } = 3;

    public int MaxRedirects { get; init; } = 10;

    public TimeSpan MaxRetryAfter { get; init; } = TimeSpan.FromSeconds(30);

    public Func<int, TimeSpan> Backoff { get; init; } = attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));

    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = (delay, cancellationToken) => Task.Delay(delay, cancellationToken);
}
