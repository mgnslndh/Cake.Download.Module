using Cake.Core;
using Cake.Core.Annotations;
using Cake.Core.IO;

namespace Cake.Download.Module;

/// <summary>
/// Installs <c>download:</c> tools when the alias is called, typically inside a task, and registers them with Cake's
/// tool locator. Unlike <c>#tool</c> and <c>InstallTool</c>, runs that don't execute the call download nothing.
/// </summary>
[CakeAliasCategory("Download")]
[CakeNamespaceImport("Cake.Download.Module")]
public static class DownloadToolAliases
{
    /// <summary>
    /// Installs the tool described by a complete <c>download:</c> directive (the text <c>#tool</c> takes), unless an
    /// identical install is already there, and registers its files with <c>context.Tools</c>.
    /// </summary>
    /// <param name="context">The Cake context.</param>
    /// <param name="directive">The directive, starting with <c>download:</c>.</param>
    /// <returns>The registered files.</returns>
    /// <exception cref="CakeException">The directive is invalid, or downloading, verifying or extracting failed.</exception>
    /// <example>
    /// <code>
    /// Task("Sbom").Does(() =>
    /// {
    ///     DownloadTool("download:https://github.com/jqlang/jq/releases/download/jq-{version}/jq-{os}-{arch}{exe}?package=jq&amp;version=1.8.2&amp;os.darwin=macos&amp;checksums=sha256sum.txt&amp;checksums_sha256=dc86824a41c165ece971ff691aff6e08bbfe6e1d1f531688b47ee78c283a85cd");
    /// });
    /// </code>
    /// </example>
    [CakeMethodAlias]
    public static IReadOnlyCollection<FilePath> DownloadTool(this ICakeContext context, string directive)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(directive);

        return CreateRunner(context).Install(directive);
    }

    /// <summary>
    /// Installs the tool described by a complete <c>download:</c> directive, the same <see cref="Uri"/> Frosting's
    /// <c>CakeHost.InstallTool</c> takes, and registers its files with <c>context.Tools</c>.
    /// </summary>
    /// <param name="context">The Cake context.</param>
    /// <param name="directive">The directive; its <see cref="Uri.OriginalString"/> is used, so <c>{…}</c> placeholders survive.</param>
    /// <returns>The registered files.</returns>
    /// <exception cref="CakeException">The directive is invalid, or downloading, verifying or extracting failed.</exception>
    [CakeMethodAlias]
    public static IReadOnlyCollection<FilePath> DownloadTool(this ICakeContext context, Uri directive)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(directive);

        return CreateRunner(context).Install(directive.OriginalString);
    }

    /// <summary>
    /// Installs the tool described by <paramref name="settings"/>, which must set the package, version and URL, unless
    /// an identical install is already there, and registers its files with <c>context.Tools</c>.
    /// </summary>
    /// <param name="context">The Cake context.</param>
    /// <param name="settings">The settings, including <c>WithPackage</c>, <c>WithVersion</c> and <c>WithUrl</c>.</param>
    /// <returns>The registered files.</returns>
    /// <exception cref="CakeException">The settings are incomplete or invalid, or downloading, verifying or extracting failed.</exception>
    /// <example>
    /// <code>
    /// DownloadTool(new DownloadToolSettings()
    ///     .WithPackage("jq")
    ///     .WithVersion("1.8.2")
    ///     .WithUrl("https://github.com/jqlang/jq/releases/download/jq-{version}/jq-{os}-{arch}{exe}")
    ///     .WithOs("darwin", "macos")
    ///     .WithChecksums("sha256sum.txt", "dc86824a41c165ece971ff691aff6e08bbfe6e1d1f531688b47ee78c283a85cd"));
    /// </code>
    /// </example>
    [CakeMethodAlias]
    public static IReadOnlyCollection<FilePath> DownloadTool(this ICakeContext context, DownloadToolSettings settings)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(settings);

        return CreateRunner(context).Install(settings);
    }

    /// <summary>
    /// Installs <paramref name="package"/> <paramref name="version"/> from <paramref name="url"/>, with
    /// <paramref name="settings"/> for integrity and everything else, and registers its files with <c>context.Tools</c>.
    /// <paramref name="settings"/> is not modified; if it also sets the package, version or URL to a different value,
    /// the call fails.
    /// </summary>
    /// <param name="context">The Cake context.</param>
    /// <param name="package">The tool name: install folder, default file name and default include.</param>
    /// <param name="version">The exact version; fills <c>{version}</c>.</param>
    /// <param name="url">The URL template, e.g. <c>https://example.com/tool-{version}-{os}-{arch}{exe}</c>.</param>
    /// <param name="settings">Integrity (required) and optional overrides.</param>
    /// <returns>The registered files.</returns>
    /// <exception cref="CakeException">The settings are invalid or conflict with the arguments, or downloading, verifying or extracting failed.</exception>
    /// <example>
    /// <code>
    /// DownloadTool(
    ///     package: "cyclonedx",
    ///     version: "0.30.0",
    ///     url: "https://github.com/CycloneDX/cyclonedx-cli/releases/download/v{version}/cyclonedx-{rid}{exe}",
    ///     settings: new DownloadToolSettings()
    ///         .WithSha256("win-x64", "1f563ba9644d2f2966fc8029fd701ca4af4f388d44c017c1d60559a1ecc9114f")
    ///         .WithSha256("linux-x64", "f89876326620f5fc78a9b27cc1af57d6ed13d019aab87490e1246a44a910babb"));
    /// </code>
    /// </example>
    [CakeMethodAlias]
    public static IReadOnlyCollection<FilePath> DownloadTool(this ICakeContext context, string package, string version, string url, DownloadToolSettings settings)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(settings);

        return CreateRunner(context).Install(package, version, url, settings);
    }

    private static DownloadToolRunner CreateRunner(ICakeContext context) =>
        new(context, new DownloadPackageInstaller(context.Environment, context.FileSystem, context.Log));
}
