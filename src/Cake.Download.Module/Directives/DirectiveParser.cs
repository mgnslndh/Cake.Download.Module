using System.Text.RegularExpressions;
using Cake.Core;
using Cake.Core.Packaging;
using Cake.Download.Module.Platforms;

namespace Cake.Download.Module.Directives;

/// <summary>
/// Parses and validates a <c>download:</c> <see cref="PackageReference"/>. Cake's query parsing is case-insensitive
/// and does not decode values, so values are percent-decoded here, and the URL template is read from
/// <see cref="PackageReference.OriginalString"/> because <see cref="PackageReference.Address"/> escapes <c>{}</c>.
/// </summary>
internal static partial class DirectiveParser
{
    public const string Scheme = "download";

    private const string KnownParameters =
        "package, version, sha256, sha256.<rid>, checksums, checksums_sha256, dialect, os.<os>, arch.<arch>, " +
        "archive.<os>, triple.<rid>, url, url.<rid>, format, filename, include, exclude";

    private static readonly string[] SingleValued = ["package", "version", "sha256", "checksums", "checksums_sha256", "dialect", "url", "format", "filename"];
    private static readonly string[] MultiValued = ["include", "exclude"];
    private static readonly string[] Prefixes = ["sha256.", "url.", "os.", "arch.", "archive.", "triple."];
    private static readonly string[] Formats = ["file", "zip", "tar", "tar.gz"];

    public static DownloadDirective Parse(PackageReference reference, DirectiveSource source = DirectiveSource.Directive)
    {
        ArgumentNullException.ThrowIfNull(reference);

        var original = reference.OriginalString;
        CakeException Fail(string problem) => new($"Invalid download directive '{original}': {problem}");

        if (!original.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase))
        {
            throw Fail($"it must start with '{Scheme}:'.");
        }

        var parameters = ReadParameters(reference.Parameters, Fail);
        string? Single(string key) => parameters.TryGetValue(key, out var values) ? values[0] : null;

        var package = Single("package") ?? throw Fail("the 'package' parameter is required.");
        if (!PackagePattern().IsMatch(package))
        {
            throw Fail($"'package' must start with a letter or digit and may only contain letters, digits, '.', '_' and '-' (was '{package}').");
        }

        if (IsReservedDeviceName(package))
        {
            throw Fail($"'package' must not be a reserved Windows device name (was '{package}').");
        }

        var version = Single("version") ?? throw Fail("the 'version' parameter is required.");
        if (string.Equals(version, "latest", StringComparison.OrdinalIgnoreCase))
        {
            throw Fail("'version=latest' is not supported; pin an exact version.");
        }

        if (!VersionPattern().IsMatch(version))
        {
            throw Fail($"'version' must start with a letter or digit and may only contain letters, digits, '.', '_', '+' and '-' (was '{version}').");
        }

        var dialectName = Single("dialect") ?? PlatformDialects.Go.Name;
        var dialect = PlatformDialects.Find(dialectName)
            ?? throw Fail($"unknown dialect '{dialectName}'. Supported dialects: {string.Join(", ", PlatformDialects.All.Select(d => d.Name))}.");

        var osOverrides = Prefixed(parameters, "os.", ridKeys: false);
        CheckValues(osOverrides.Keys, dialect.OsValues, "os.", dialect, Fail);
        var archOverrides = Prefixed(parameters, "arch.", ridKeys: false);
        CheckValues(archOverrides.Keys, dialect.ArchValues, "arch.", dialect, Fail);
        var archiveOverrides = Prefixed(parameters, "archive.", ridKeys: false);
        CheckValues(archiveOverrides.Keys, dialect.OsValues, "archive.", dialect, Fail);

        var tripleOverrides = Prefixed(parameters, "triple.", ridKeys: true);
        if (tripleOverrides.Count > 0 && !dialect.HasTriples)
        {
            throw Fail("'triple.<rid>' parameters require 'dialect=rust'.");
        }

        CheckRids(tripleOverrides.Keys, "triple.", Fail);
        var urlByRid = Prefixed(parameters, "url.", ridKeys: true);
        CheckRids(urlByRid.Keys, "url.", Fail);
        var sha256ByRid = Prefixed(parameters, "sha256.", ridKeys: true);
        CheckRids(sha256ByRid.Keys, "sha256.", Fail);

        var template = ReadTemplate(original);
        var url = Single("url");
        if (template is not null && url is not null)
        {
            throw Fail("specify the download URL either after 'download:' or with 'url=', not both.");
        }

        if (template is null && url is null && urlByRid.Count == 0)
        {
            throw Fail("no download URL. Put the URL after 'download:' or use 'url=' or 'url.<rid>='.");
        }

        var format = Single("format")?.ToLowerInvariant();
        if (format == "tgz")
        {
            format = "tar.gz";
        }

        if (format is not null && !Formats.Contains(format))
        {
            throw Fail($"unknown format '{format}'. Supported formats: {string.Join(", ", Formats)}.");
        }

        var fileName = Single("filename");
        if (fileName is not null && (fileName.IndexOfAny(['/', '\\', ':']) >= 0 || fileName.Length == 0 || fileName.All(c => c == '.')))
        {
            throw Fail($"'filename' must be a file name, not a path (was '{fileName}').");
        }

        return new DownloadDirective
        {
            OriginalString = original,
            Package = package,
            Version = version,
            UrlTemplate = template,
            Url = url,
            UrlByRid = urlByRid,
            Dialect = dialect.Name,
            OsOverrides = osOverrides,
            ArchOverrides = archOverrides,
            ArchiveOverrides = archiveOverrides,
            TripleOverrides = tripleOverrides,
            Format = format,
            FileName = fileName,
            Include = parameters.TryGetValue("include", out var include) ? include : [],
            Exclude = parameters.TryGetValue("exclude", out var exclude) ? exclude : [],
            Integrity = ParseIntegrity(Single("sha256"), sha256ByRid, Single("checksums"), Single("checksums_sha256"), Fail),
            Source = source,
        };
    }

    private static Dictionary<string, IReadOnlyList<string>> ReadParameters(
        IReadOnlyDictionary<string, IReadOnlyList<string>> raw,
        Func<string, CakeException> fail)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, values) in raw)
        {
            var isMulti = MultiValued.Contains(key, StringComparer.OrdinalIgnoreCase);
            var isKnown = isMulti
                || SingleValued.Contains(key, StringComparer.OrdinalIgnoreCase)
                || Prefixes.Any(prefix => key.Length > prefix.Length && key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (!isKnown)
            {
                throw fail($"unknown parameter '{key}'. Known parameters: {KnownParameters}.");
            }

            var decoded = values.Select(value => Uri.UnescapeDataString(value)).ToList();
            if (decoded.Count == 0 || decoded.Any(string.IsNullOrWhiteSpace))
            {
                throw fail($"parameter '{key}' needs a value.");
            }

            if (!isMulti && decoded.Count > 1)
            {
                throw fail($"parameter '{key}' may only be specified once.");
            }

            result[key] = decoded;
        }

        return result;
    }

    private static string? ReadTemplate(string original)
    {
        var rest = original[(Scheme.Length + 1)..];
        var query = rest.IndexOf('?', StringComparison.Ordinal);
        var template = query < 0 ? rest : rest[..query];
        return template.Length == 0 ? null : template;
    }

    private static Dictionary<string, string> Prefixed(Dictionary<string, IReadOnlyList<string>> parameters, string prefix, bool ridKeys)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, values) in parameters)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var suffix = key[prefix.Length..];
                result[ridKeys ? suffix.ToLowerInvariant() : suffix] = values[0];
            }
        }

        return result;
    }

    private static void CheckRids(IEnumerable<string> rids, string prefix, Func<string, CakeException> fail)
    {
        foreach (var rid in rids)
        {
            if (!PlatformInfo.IsSupportedRid(rid))
            {
                throw fail($"'{prefix}{rid}' does not name a supported platform. Supported platforms: {string.Join(", ", PlatformInfo.SupportedRids)}.");
            }
        }
    }

    private static void CheckValues(
        IEnumerable<string> keys,
        IReadOnlyCollection<string> allowed,
        string prefix,
        PlatformDialect dialect,
        Func<string, CakeException> fail)
    {
        foreach (var key in keys)
        {
            if (!allowed.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                throw fail($"'{prefix}{key}' is not a valid override for dialect '{dialect.Name}'. Use one of: {string.Join(", ", allowed.Select(value => prefix + value))}.");
            }
        }
    }

    private static IntegritySpec ParseIntegrity(
        string? sha256,
        Dictionary<string, string> sha256ByRid,
        string? checksums,
        string? checksumsSha256,
        Func<string, CakeException> fail)
    {
        var modes = (sha256 is null ? 0 : 1) + (sha256ByRid.Count == 0 ? 0 : 1) + (checksums is null ? 0 : 1);
        if (modes > 1 || (sha256 is not null && checksumsSha256 is not null))
        {
            throw fail("specify only one integrity option: 'sha256=', 'sha256.<rid>=', 'checksums=' with 'checksums_sha256=', or 'sha256=skip'.");
        }

        if (checksumsSha256 is not null && checksums is null)
        {
            throw fail("'checksums_sha256' requires 'checksums'.");
        }

        if (sha256 is not null)
        {
            if (string.Equals(sha256, "skip", StringComparison.OrdinalIgnoreCase))
            {
                return new SkipIntegrity();
            }

            return new Sha256Integrity(Hex(sha256, "'sha256' must be a SHA-256 hash of 64 hexadecimal characters, or 'skip'.", fail));
        }

        if (sha256ByRid.Count > 0)
        {
            return new PerRidSha256Integrity(sha256ByRid.ToDictionary(
                entry => entry.Key,
                entry => Hex(entry.Value, $"'sha256.{entry.Key}' must be a SHA-256 hash of 64 hexadecimal characters.", fail),
                StringComparer.OrdinalIgnoreCase));
        }

        if (checksums is not null)
        {
            var pin = checksumsSha256 is null
                ? null
                : Hex(checksumsSha256, "'checksums_sha256' must be a SHA-256 hash of 64 hexadecimal characters.", fail);
            return new ChecksumsFileIntegrity(checksums, pin);
        }

        return new MissingIntegrity();
    }

    private static string Hex(string value, string problem, Func<string, CakeException> fail) =>
        Sha256Pattern().IsMatch(value) ? value.ToLowerInvariant() : throw fail(problem);

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*\z")]
    private static partial Regex PackagePattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._+-]*\z")]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._+-]*\z")]
    private static partial Regex FileNamePattern();

    internal static bool IsValidFileName(string fileName) => FileNamePattern().IsMatch(fileName);

    [GeneratedRegex(@"^(CON|PRN|AUX|NUL|COM[0-9\u00B9\u00B2\u00B3]|LPT[0-9\u00B9\u00B2\u00B3])(\.[\s\S]*)?\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReservedDeviceNamePattern();

    /// <summary>True when the part before the first dot is a Windows reserved device name such as <c>NUL</c> or <c>COM1</c>.</summary>
    internal static bool IsReservedDeviceName(string name) => ReservedDeviceNamePattern().IsMatch(name);

    [GeneratedRegex(@"^[0-9a-fA-F]{64}\z")]
    private static partial Regex Sha256Pattern();
}
