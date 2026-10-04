using System.Text;
using Cake.Core;

namespace Cake.Download.Module;

/// <summary>
/// A typed description of a <c>download:</c> tool for the <c>DownloadTool</c> alias. Each property maps to one directive
/// parameter, and unset properties keep the directive's defaults. Nothing is validated until the tool is installed.
/// </summary>
public sealed class DownloadToolSettings
{
    /// <summary>Gets or sets the tool name (<c>package</c>): install folder, default file name and default include.</summary>
    public string? Package { get; set; }

    /// <summary>Gets or sets the exact version (<c>version</c>); fills <c>{version}</c>.</summary>
    public string? Version { get; set; }

    /// <summary>Gets or sets the URL template used on every platform without a <see cref="UrlByPlatform"/> entry.</summary>
    public string? Url { get; set; }

    /// <summary>Gets URL templates per .NET RID (<c>url.&lt;rid&gt;</c>).</summary>
    public IDictionary<string, string> UrlByPlatform { get; } = NewMap();

    /// <summary>Gets or sets the placeholder dialect (<c>dialect</c>); <see cref="DownloadDialect.Go"/> when not set.</summary>
    public DownloadDialect? Dialect { get; set; }

    /// <summary>Gets <c>{os}</c> overrides, keyed by the dialect's default value (<c>os.&lt;value&gt;</c>).</summary>
    public IDictionary<string, string> OsOverrides { get; } = NewMap();

    /// <summary>Gets <c>{arch}</c> overrides, keyed by the dialect's default value (<c>arch.&lt;value&gt;</c>).</summary>
    public IDictionary<string, string> ArchOverrides { get; } = NewMap();

    /// <summary>Gets <c>{archive}</c> overrides, keyed by the dialect's default <c>{os}</c> value (<c>archive.&lt;os&gt;</c>).</summary>
    public IDictionary<string, string> ArchiveOverrides { get; } = NewMap();

    /// <summary>Gets <c>{triple}</c> overrides per .NET RID (<c>triple.&lt;rid&gt;</c>, rust dialect only).</summary>
    public IDictionary<string, string> TripleOverrides { get; } = NewMap();

    /// <summary>Gets or sets the download format (<c>format</c>); detected from the URL when not set.</summary>
    public DownloadFormat? Format { get; set; }

    /// <summary>Gets or sets the file name for a raw download (<c>filename</c>).</summary>
    public string? FileName { get; set; }

    /// <summary>Gets the globs choosing the files to register (<c>include</c>).</summary>
    public IList<string> Include { get; } = new List<string>();

    /// <summary>Gets the globs removed from the <see cref="Include"/> result (<c>exclude</c>).</summary>
    public IList<string> Exclude { get; } = new List<string>();

    /// <summary>Gets or sets the SHA-256 of the download on every platform (<c>sha256</c>).</summary>
    public string? Sha256 { get; set; }

    /// <summary>Gets the SHA-256 of the download per .NET RID (<c>sha256.&lt;rid&gt;</c>).</summary>
    public IDictionary<string, string> Sha256ByPlatform { get; } = NewMap();

    /// <summary>Gets or sets the checksums file, relative to the download URL or absolute (<c>checksums</c>).</summary>
    public string? ChecksumsFile { get; set; }

    /// <summary>Gets or sets the SHA-256 of the checksums file itself (<c>checksums_sha256</c>).</summary>
    public string? ChecksumsFileSha256 { get; set; }

    /// <summary>Gets or sets a value indicating whether to install without verification (<c>sha256=skip</c>). Not recommended.</summary>
    public bool SkipVerification { get; set; }

    /// <summary>Sets <see cref="Package"/>.</summary>
    /// <param name="package">The tool name.</param>
    /// <returns>This instance.</returns>
    public DownloadToolSettings WithPackage(string package) => Set(() => Package = package, package);

    /// <summary>Sets <see cref="Version"/>.</summary>
    /// <param name="version">The exact version.</param>
    /// <returns>This instance.</returns>
    public DownloadToolSettings WithVersion(string version) => Set(() => Version = version, version);

    /// <summary>Sets <see cref="Url"/>.</summary>
    /// <param name="template">The URL template.</param>
    /// <returns>This instance.</returns>
    public DownloadToolSettings WithUrl(string template) => Set(() => Url = template, template);

    /// <summary>Sets the URL template for one platform (<c>url.&lt;rid&gt;</c>).</summary>
    /// <param name="rid">The .NET RID, e.g. <c>linux-x64</c>.</param>
    /// <param name="template">The URL template.</param>
    /// <returns>This instance.</returns>
    public DownloadToolSettings WithUrl(string rid, string template) => Put(UrlByPlatform, rid, template);

    /// <summary>Sets <see cref="Dialect"/>.</summary>
    /// <param name="dialect">The dialect.</param>
    /// <returns>This instance.</returns>
    public DownloadToolSettings WithDialect(DownloadDialect dialect)
    {
        Dialect = dialect;
        return this;
    }

    /// <summary>Overrides one <c>{os}</c> value (<c>os.&lt;value&gt;</c>), e.g. <c>WithOs("darwin", "macOS")</c>.</summary>
    /// <param name="defaultValue">The dialect's default value.</param>
    /// <param name="value">The value to use instead.</param>
    /// <returns>This instance.</returns>
    public DownloadToolSettings WithOs(string defaultValue, string value) => Put(OsOverrides, defaultValue, value);

    /// <summary>Overrides one <c>{arch}</c> value (<c>arch.&lt;value&gt;</c>), e.g. <c>WithArch("amd64", "x86_64")</c>.</summary>
    /// <param name="defaultValue">The dialect's default value.</param>
    /// <param name="value">The value to use instead.</param>
    /// <returns>This instance.</returns>
    public DownloadToolSettings WithArch(string defaultValue, string value) => Put(ArchOverrides, defaultValue, value);

    /// <summary>Overrides <c>{archive}</c> for one OS (<c>archive.&lt;os&gt;</c>), e.g. <c>WithArchive("darwin", "zip")</c>.</summary>
    /// <param name="os">The dialect's default <c>{os}</c> value.</param>
    /// <param name="extension">The archive extension.</param>
    /// <returns>This instance.</returns>
    public DownloadToolSettings WithArchive(string os, string extension) => Put(ArchiveOverrides, os, extension);

    /// <summary>Overrides <c>{triple}</c> for one platform (<c>triple.&lt;rid&gt;</c>, rust dialect only).</summary>
    /// <param name="rid">The .NET RID.</param>
    /// <param name="triple">The Rust target triple.</param>
    /// <returns>This instance.</returns>
    public DownloadToolSettings WithTriple(string rid, string triple) => Put(TripleOverrides, rid, triple);

    /// <summary>Sets <see cref="Format"/>.</summary>
    /// <param name="format">The format.</param>
    /// <returns>This instance.</returns>
    public DownloadToolSettings WithFormat(DownloadFormat format)
    {
        Format = format;
        return this;
    }

    /// <summary>Sets <see cref="FileName"/>.</summary>
    /// <param name="fileName">The file name for a raw download.</param>
    /// <returns>This instance.</returns>
    public DownloadToolSettings WithFileName(string fileName) => Set(() => FileName = fileName, fileName);

    /// <summary>Adds an <c>include</c> glob.</summary>
    /// <param name="glob">The glob, relative to the install folder.</param>
    /// <returns>This instance.</returns>
    public DownloadToolSettings WithInclude(string glob) => Set(() => Include.Add(glob), glob);

    /// <summary>Adds an <c>exclude</c> glob.</summary>
    /// <param name="glob">The glob, relative to the install folder.</param>
    /// <returns>This instance.</returns>
    public DownloadToolSettings WithExclude(string glob) => Set(() => Exclude.Add(glob), glob);

    /// <summary>Pins the download's SHA-256 on every platform (<c>sha256</c>).</summary>
    /// <param name="sha256">64 hexadecimal characters.</param>
    /// <returns>This instance.</returns>
    public DownloadToolSettings WithSha256(string sha256) => Set(() => Sha256 = sha256, sha256);

    /// <summary>Pins the download's SHA-256 for one platform (<c>sha256.&lt;rid&gt;</c>).</summary>
    /// <param name="rid">The .NET RID.</param>
    /// <param name="sha256">64 hexadecimal characters.</param>
    /// <returns>This instance.</returns>
    public DownloadToolSettings WithSha256(string rid, string sha256) => Put(Sha256ByPlatform, rid, sha256);

    /// <summary>Verifies the download against a pinned checksums file (<c>checksums</c> and <c>checksums_sha256</c>).</summary>
    /// <param name="file">The checksums file, relative to the download URL or absolute.</param>
    /// <param name="sha256">The SHA-256 of the checksums file itself.</param>
    /// <returns>This instance.</returns>
    public DownloadToolSettings WithChecksums(string file, string sha256)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(sha256);
        ChecksumsFile = file;
        ChecksumsFileSha256 = sha256;
        return this;
    }

    /// <summary>Installs without verification (<c>sha256=skip</c>). A warning is logged on every fresh install. Not recommended.</summary>
    /// <returns>This instance.</returns>
    public DownloadToolSettings WithoutVerification()
    {
        SkipVerification = true;
        return this;
    }

    /// <summary>Writes these settings as a <c>download:</c> directive, for <c>#tool</c>-style eager installs such as Cake.Sdk's <c>InstallTool</c>.</summary>
    /// <returns>The directive.</returns>
    /// <exception cref="CakeException"><see cref="Package"/> or <see cref="Version"/> is missing, there is no URL, or <see cref="SkipVerification"/> is combined with <see cref="Sha256"/>.</exception>
    public string ToDirective()
    {
        const string Overloads = "DownloadTool(package, version, url, settings)";
        if (string.IsNullOrEmpty(Package))
        {
            throw new CakeException($"DownloadToolSettings needs a package: use WithPackage(…) or {Overloads}.");
        }

        if (string.IsNullOrEmpty(Version))
        {
            throw new CakeException($"DownloadToolSettings for '{Package}' needs a version: use WithVersion(…) or {Overloads}.");
        }

        if (string.IsNullOrEmpty(Url) && UrlByPlatform.Count == 0)
        {
            throw new CakeException($"DownloadToolSettings for '{Package}' needs a URL: use WithUrl(…), WithUrl(rid, …) or {Overloads}.");
        }

        if (SkipVerification && Sha256 is not null)
        {
            throw new CakeException(
                $"DownloadToolSettings for '{Package}': specify only one integrity option: WithSha256(…), WithSha256(rid, …), WithChecksums(…) or WithoutVerification().");
        }

        var leading = !string.IsNullOrEmpty(Url) && Url.IndexOfAny(['?', '#', '&']) < 0 ? Url : null;
        var builder = new StringBuilder("download:").Append(leading);
        var separator = '?';

        void Add(string key, string value)
        {
            builder.Append(separator).Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
            separator = '&';
        }

        void AddMap(string prefix, IDictionary<string, string> map)
        {
            foreach (var (key, value) in map.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                Add(prefix + key, value);
            }
        }

        Add("package", Package);
        Add("version", Version);
        if (!string.IsNullOrEmpty(Url) && leading is null)
        {
            Add("url", Url);
        }

        AddMap("url.", UrlByPlatform);
        if (Dialect is { } dialect)
        {
            Add("dialect", DialectName(dialect));
        }

        AddMap("os.", OsOverrides);
        AddMap("arch.", ArchOverrides);
        AddMap("archive.", ArchiveOverrides);
        AddMap("triple.", TripleOverrides);
        if (Format is { } format)
        {
            Add("format", FormatName(format));
        }

        if (FileName is not null)
        {
            Add("filename", FileName);
        }

        foreach (var glob in Include)
        {
            Add("include", glob);
        }

        foreach (var glob in Exclude)
        {
            Add("exclude", glob);
        }

        if (SkipVerification)
        {
            Add("sha256", "skip");
        }

        if (Sha256 is not null)
        {
            Add("sha256", Sha256);
        }

        AddMap("sha256.", Sha256ByPlatform);
        if (ChecksumsFile is not null)
        {
            Add("checksums", ChecksumsFile);
        }

        if (ChecksumsFileSha256 is not null)
        {
            Add("checksums_sha256", ChecksumsFileSha256);
        }

        return builder.ToString();
    }

    /// <summary>Writes these settings as a <c>download:</c> directive <see cref="Uri"/>, for Frosting's <c>CakeHost.InstallTool</c>.</summary>
    /// <returns>The directive; its <see cref="Uri.OriginalString"/> equals <see cref="ToDirective"/>.</returns>
    /// <exception cref="CakeException">See <see cref="ToDirective"/>.</exception>
    public Uri ToDirectiveUri() => new(ToDirective());

    internal DownloadToolSettings Clone()
    {
        var clone = new DownloadToolSettings
        {
            Package = Package,
            Version = Version,
            Url = Url,
            Dialect = Dialect,
            Format = Format,
            FileName = FileName,
            Sha256 = Sha256,
            ChecksumsFile = ChecksumsFile,
            ChecksumsFileSha256 = ChecksumsFileSha256,
            SkipVerification = SkipVerification,
        };
        Copy(UrlByPlatform, clone.UrlByPlatform);
        Copy(OsOverrides, clone.OsOverrides);
        Copy(ArchOverrides, clone.ArchOverrides);
        Copy(ArchiveOverrides, clone.ArchiveOverrides);
        Copy(TripleOverrides, clone.TripleOverrides);
        Copy(Sha256ByPlatform, clone.Sha256ByPlatform);
        foreach (var glob in Include)
        {
            clone.Include.Add(glob);
        }

        foreach (var glob in Exclude)
        {
            clone.Exclude.Add(glob);
        }

        return clone;
    }

    internal DownloadToolSettings WithIdentity(string package, string version, string url)
    {
        var filled = Clone();
        filled.Package = Merge("package", package, nameof(Package), Package);
        filled.Version = Merge("version", version, nameof(Version), Version);
        filled.Url = Merge("url", url, nameof(Url), Url);
        return filled;
    }

    private static string Merge(string argument, string value, string property, string? current) =>
        current is null || string.Equals(current, value, StringComparison.Ordinal)
            ? value
            : throw new CakeException($"The {argument} argument '{value}' conflicts with DownloadToolSettings.{property} '{current}'. Set it in one place.");

    private static Dictionary<string, string> NewMap() => new(StringComparer.OrdinalIgnoreCase);

    private static void Copy(IDictionary<string, string> from, IDictionary<string, string> to)
    {
        foreach (var (key, value) in from)
        {
            to[key] = value;
        }
    }

    private static string DialectName(DownloadDialect dialect) => dialect switch
    {
        DownloadDialect.Go => "go",
        DownloadDialect.DotNet => "dotnet",
        DownloadDialect.Rust => "rust",
        _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, "Unknown dialect."),
    };

    private static string FormatName(DownloadFormat format) => format switch
    {
        DownloadFormat.File => "file",
        DownloadFormat.Zip => "zip",
        DownloadFormat.Tar => "tar",
        DownloadFormat.TarGz => "tar.gz",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown format."),
    };

    private DownloadToolSettings Set(Action set, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        set();
        return this;
    }

    private DownloadToolSettings Put(IDictionary<string, string> map, string key, string value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        map[key] = value;
        return this;
    }
}
