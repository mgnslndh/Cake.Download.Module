using Cake.Core;
using Cake.Download.Module.Directives;
using Cake.Download.Module.Http;

namespace Cake.Download.Module.Integrity;

internal sealed record ExpectedHash(string Sha256, string Source);

/// <summary>
/// Turns a plan's integrity option into the SHA-256 the download must have, and verifies the download against it.
/// </summary>
internal sealed class IntegrityResolver
{
    private const long MaxChecksumsFileBytes = 1024 * 1024;

    private readonly HttpDownloader _downloader;

    public IntegrityResolver(HttpDownloader downloader)
    {
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
    }

    public static void Verify(DownloadPlan plan, ExpectedHash? expected, string actualSha256)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Integrity is SkippedIntegrity)
        {
            return;
        }

        if (plan.Integrity is MissingIntegrityPlan missing)
        {
            throw new CakeException(
                $"{Hints.NoIntegrity(plan.Source, plan.Package)} {plan.AssetName} ({plan.Platform.Rid}) has SHA-256 {actualSha256}. " +
                $"{Hints.AddPin(plan.Source, missing.Parameter, actualSha256)} " +
                "The download was discarded.");
        }

        if (expected is null)
        {
            throw new InvalidOperationException("An expected hash is required for pinned integrity.");
        }

        if (!string.Equals(expected.Sha256, actualSha256, StringComparison.Ordinal))
        {
            throw new CakeException(
                $"SHA-256 mismatch for {plan.Url.AbsoluteUri}.{Environment.NewLine}" +
                $"  Expected: {expected.Sha256} (from {expected.Source}){Environment.NewLine}" +
                $"  Actual:   {actualSha256}{Environment.NewLine}" +
                "The download was discarded.");
        }
    }

    public ExpectedHash? Resolve(DownloadPlan plan, string workDirectory)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return plan.Integrity switch
        {
            PinnedSha256 pinned => new ExpectedHash(pinned.Sha256, $"'{pinned.Parameter}'"),
            ChecksumsFilePlan checksums => FromChecksumsFile(plan, checksums, workDirectory),
            _ => null,
        };
    }

    private static string? Find(IReadOnlyDictionary<string, string> entries, string asset)
    {
        if (entries.TryGetValue(asset, out var exact))
        {
            return exact;
        }

        var bySuffix = entries
            .Where(entry => entry.Key.EndsWith("/" + asset, StringComparison.Ordinal))
            .Select(entry => entry.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return bySuffix.Count == 1 ? bySuffix[0] : null;
    }

    private ExpectedHash FromChecksumsFile(DownloadPlan plan, ChecksumsFilePlan checksums, string workDirectory)
    {
        var url = checksums.Url.AbsoluteUri;
        var download = _downloader.Download(checksums.Url, Path.Combine(workDirectory, "checksums.txt"), MaxChecksumsFileBytes);

        if (checksums.Sha256 is null)
        {
            throw new CakeException(
                $"The checksums file {url} is not pinned. Its SHA-256 is {download.Sha256}. " +
                Hints.PinChecksums(plan.Source, plan.Package, checksums.Reference, download.Sha256));
        }

        if (!string.Equals(checksums.Sha256, download.Sha256, StringComparison.Ordinal))
        {
            throw new CakeException(
                $"SHA-256 mismatch for checksums file {url}.{Environment.NewLine}" +
                $"  Expected: {checksums.Sha256} (from 'checksums_sha256'){Environment.NewLine}" +
                $"  Actual:   {download.Sha256}");
        }

        var entries = ChecksumsFileParser.Parse(File.ReadAllText(download.Path), url);
        var hash = Find(entries, plan.AssetName);
        if (hash is null)
        {
            var listed = string.Join(", ", entries.Keys.Take(20)) + (entries.Count > 20 ? ", …" : string.Empty);
            throw new CakeException($"'{plan.AssetName}' is not listed in the checksums file {url}. Listed files: {listed}.");
        }

        return new ExpectedHash(hash, "checksums file " + url);
    }
}
