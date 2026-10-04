using Cake.Download.Module.Platforms;

namespace Cake.Download.Module.Directives;

internal enum ArchiveFormat
{
    File,
    Zip,
    Tar,
    TarGz,
}

internal static class ArchiveFormats
{
    public static string ToName(ArchiveFormat format) => format switch
    {
        ArchiveFormat.File => "file",
        ArchiveFormat.Zip => "zip",
        ArchiveFormat.Tar => "tar",
        _ => "tar.gz",
    };

    public static ArchiveFormat FromName(string name) => name switch
    {
        "file" => ArchiveFormat.File,
        "zip" => ArchiveFormat.Zip,
        "tar" => ArchiveFormat.Tar,
        "tar.gz" => ArchiveFormat.TarGz,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown archive format."),
    };

    public static ArchiveFormat Detect(Uri url)
    {
        var path = url.AbsolutePath;
        if (path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            return ArchiveFormat.TarGz;
        }

        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return ArchiveFormat.Zip;
        }

        return path.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) ? ArchiveFormat.Tar : ArchiveFormat.File;
    }
}

/// <summary>
/// A directive resolved for one platform: every template expanded, the expected integrity chosen.
/// </summary>
internal sealed record DownloadPlan
{
    public required string Package { get; init; }

    public required string Version { get; init; }

    public required PlatformInfo Platform { get; init; }

    public required string Dialect { get; init; }

    public required IReadOnlyDictionary<string, string> Placeholders { get; init; }

    public required Uri Url { get; init; }

    public required ArchiveFormat Format { get; init; }

    public string? FileName { get; init; }

    public required IReadOnlyList<string> Include { get; init; }

    public required IReadOnlyList<string> Exclude { get; init; }

    public required IntegrityPlan Integrity { get; init; }

    public string FolderName => $"{Package}.{Version}";

    public string AssetName => Uri.UnescapeDataString(Url.Segments[^1]);
}

internal abstract record IntegrityPlan
{
    public abstract string Fingerprint { get; }
}

internal sealed record PinnedSha256(string Sha256, string Parameter) : IntegrityPlan
{
    public override string Fingerprint => "sha256:" + Sha256;
}

internal sealed record ChecksumsFilePlan(Uri Url, string? Sha256) : IntegrityPlan
{
    public override string Fingerprint => $"checksums:{Url.AbsoluteUri}#{Sha256}";
}

internal sealed record SkippedIntegrity : IntegrityPlan
{
    public override string Fingerprint => "skip";
}

internal sealed record MissingIntegrityPlan(string Parameter) : IntegrityPlan
{
    public override string Fingerprint => "missing";
}
