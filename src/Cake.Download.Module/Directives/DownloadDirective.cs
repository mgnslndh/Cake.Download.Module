namespace Cake.Download.Module.Directives;

/// <summary>
/// A validated <c>download:</c> directive. Templates are kept unexpanded; <c>DownloadPlanner</c> expands them.
/// </summary>
internal sealed record DownloadDirective
{
    public required string OriginalString { get; init; }

    public required string Package { get; init; }

    public required string Version { get; init; }

    public string? UrlTemplate { get; init; }

    public string? Url { get; init; }

    public IReadOnlyDictionary<string, string> UrlByRid { get; init; } = Empty();

    public string Dialect { get; init; } = "go";

    public IReadOnlyDictionary<string, string> OsOverrides { get; init; } = Empty();

    public IReadOnlyDictionary<string, string> ArchOverrides { get; init; } = Empty();

    public IReadOnlyDictionary<string, string> ArchiveOverrides { get; init; } = Empty();

    public IReadOnlyDictionary<string, string> TripleOverrides { get; init; } = Empty();

    public string? Format { get; init; }

    public string? FileName { get; init; }

    public IReadOnlyList<string> Include { get; init; } = [];

    public IReadOnlyList<string> Exclude { get; init; } = [];

    public required IntegritySpec Integrity { get; init; }

    public DirectiveSource Source { get; init; } = DirectiveSource.Directive;

    private static Dictionary<string, string> Empty() => new(StringComparer.OrdinalIgnoreCase);
}

internal abstract record IntegritySpec;

internal sealed record Sha256Integrity(string Sha256) : IntegritySpec;

internal sealed record PerRidSha256Integrity(IReadOnlyDictionary<string, string> Sha256ByRid) : IntegritySpec;

internal sealed record ChecksumsFileIntegrity(string Reference, string? Sha256) : IntegritySpec;

internal sealed record SkipIntegrity : IntegritySpec;

internal sealed record MissingIntegrity : IntegritySpec;
