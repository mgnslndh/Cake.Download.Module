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

    private static DownloadToolRunner CreateRunner(ICakeContext context) =>
        new(context, new DownloadPackageInstaller(context.Environment, context.FileSystem, context.Log));
}
