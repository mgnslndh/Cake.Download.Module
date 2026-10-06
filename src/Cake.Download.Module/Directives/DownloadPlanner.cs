using Cake.Core;
using Cake.Download.Module.Platforms;

namespace Cake.Download.Module.Directives;

internal static class DownloadPlanner
{
    private static readonly string[] PlatformPlaceholders = ["os", "arch", "rid", "exe", "archive", "triple"];

    public static DownloadPlan Create(DownloadDirective directive, PlatformInfo platform)
    {
        ArgumentNullException.ThrowIfNull(directive);
        ArgumentNullException.ThrowIfNull(platform);

        var dialect = PlatformDialects.Find(directive.Dialect)
            ?? throw new InvalidOperationException($"Unknown dialect '{directive.Dialect}'.");
        var rid = platform.Rid;
        var osDefault = dialect.GetOs(platform.Os);
        var archDefault = dialect.GetArch(platform.Cpu);

        var placeholders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["version"] = directive.Version,
            ["os"] = directive.OsOverrides.GetValueOrDefault(osDefault, osDefault),
            ["arch"] = directive.ArchOverrides.GetValueOrDefault(archDefault, archDefault),
            ["rid"] = rid,
            ["exe"] = platform.Exe,
            ["archive"] = directive.ArchiveOverrides.GetValueOrDefault(osDefault, platform.IsWindows ? "zip" : "tar.gz"),
        };
        if (dialect.HasTriples)
        {
            placeholders["triple"] = directive.TripleOverrides.GetValueOrDefault(rid, dialect.GetTriple(platform));
        }

        var template = directive.UrlByRid.GetValueOrDefault(rid) ?? directive.Url ?? directive.UrlTemplate
            ?? throw new CakeException(
                $"The download directive for '{directive.Package}' has no URL for {rid}. Add 'url.{rid}=<url>' or a default URL. " +
                $"Directive: {directive.OriginalString}");
        var url = ParseHttps(PlaceholderExpander.Expand(template, placeholders, "the download URL"), "download URL", directive);
        var format = directive.Format is null ? ArchiveFormats.Detect(url) : ArchiveFormats.FromName(directive.Format);

        string? fileName = null;
        if (format == ArchiveFormat.File)
        {
            fileName = directive.FileName is null
                ? directive.Package + platform.Exe
                : PlaceholderExpander.Expand(directive.FileName, placeholders, "'filename'");
            if (!DirectiveParser.IsValidFileName(fileName))
            {
                throw new CakeException(
                    $"'filename' must start with a letter or digit and contain only letters, digits, '.', '_', '+' and '-' (was '{fileName}'). Directive: {directive.OriginalString}");
            }

            if (DirectiveParser.IsReservedDeviceName(fileName))
            {
                throw new CakeException(
                    $"'filename' must not be a reserved Windows device name (was '{fileName}'). Directive: {directive.OriginalString}");
            }
        }
        else if (directive.FileName is not null)
        {
            throw new CakeException(
                $"'filename' only applies to raw file downloads, but {url.AbsoluteUri} is a {ArchiveFormats.ToName(format)} archive. " +
                $"Directive: {directive.OriginalString}");
        }

        IReadOnlyList<string> include = directive.Include.Count > 0
            ? directive.Include.Select(pattern => PlaceholderExpander.Expand(pattern, placeholders, "'include'")).ToList()
            : [fileName ?? $"**/{directive.Package}{platform.Exe}"];
        var exclude = directive.Exclude.Select(pattern => PlaceholderExpander.Expand(pattern, placeholders, "'exclude'")).ToList();

        var platformSpecific = directive.UrlByRid.Count > 0 || PlaceholderExpander.ContainsAny(template, PlatformPlaceholders);

        return new DownloadPlan
        {
            Package = directive.Package,
            Version = directive.Version,
            Platform = platform,
            Dialect = dialect.Name,
            Placeholders = placeholders,
            Url = url,
            Format = format,
            FileName = fileName,
            Include = include,
            Exclude = exclude,
            Integrity = PlanIntegrity(directive, rid, url, placeholders, platformSpecific),
            Source = directive.Source,
        };
    }

    private static IntegrityPlan PlanIntegrity(
        DownloadDirective directive,
        string rid,
        Uri url,
        IReadOnlyDictionary<string, string> placeholders,
        bool platformSpecific) => directive.Integrity switch
        {
            Sha256Integrity pinned => new PinnedSha256(pinned.Sha256, "sha256"),
            PerRidSha256Integrity perRid => perRid.Sha256ByRid.TryGetValue(rid, out var hash)
                ? new PinnedSha256(hash, $"sha256.{rid}")
                : new MissingIntegrityPlan($"sha256.{rid}"),
            ChecksumsFileIntegrity checksums => new ChecksumsFilePlan(
                ParseHttps(ResolveReference(url, PlaceholderExpander.Expand(checksums.Reference, placeholders, "'checksums'")), "checksums URL", directive),
                checksums.Sha256,
                checksums.Reference),
            SkipIntegrity => new SkippedIntegrity(),
            _ => new MissingIntegrityPlan(platformSpecific ? $"sha256.{rid}" : "sha256"),
        };

    private static string ResolveReference(Uri baseUri, string reference) =>
        Uri.TryCreate(baseUri, reference, out var resolved) ? resolved.AbsoluteUri : reference;

    private static Uri ParseHttps(string value, string what, DownloadDirective directive)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new CakeException(
                $"The {what} '{value}' for '{directive.Package}' is not an absolute https URL. Only https is supported. " +
                $"Directive: {directive.OriginalString}");
        }

        return uri;
    }
}
