using Cake.Core;
using Cake.Core.IO;
using Cake.Core.Packaging;
using Cake.Download.Module.Directives;

namespace Cake.Download.Module;

/// <summary>
/// What the <see cref="DownloadToolAliases"/> do: install into Cake's tools folder and register the files with
/// <c>context.Tools</c>. The installer is passed in so tests can fake the network and the platform.
/// </summary>
internal sealed class DownloadToolRunner(ICakeContext context, DownloadPackageInstaller installer)
{
    public IReadOnlyCollection<FilePath> Install(string directive)
    {
        if (!directive.StartsWith(DirectiveParser.Scheme + ":", StringComparison.OrdinalIgnoreCase))
        {
            throw new CakeException($"Invalid download directive '{directive}': it must start with '{DirectiveParser.Scheme}:'.");
        }

        PackageReference reference;
        try
        {
            reference = new PackageReference(directive);
        }
        catch (ArgumentException exception)
        {
            throw new CakeException($"Invalid download directive '{directive}': {exception.Message}", exception);
        }

        return Install(reference, DirectiveSource.Directive);
    }

    public IReadOnlyCollection<FilePath> Install(DownloadToolSettings settings) =>
        Install(new PackageReference(settings.ToDirective()), DirectiveSource.Settings);

    public IReadOnlyCollection<FilePath> Install(string package, string version, string url, DownloadToolSettings settings) =>
        Install(settings.WithIdentity(package, version, url));

    internal IReadOnlyCollection<FilePath> Install(PackageReference reference, DirectiveSource source)
    {
        var toolsPath = context.Configuration.GetToolPath(context.Environment.WorkingDirectory, context.Environment);
        var files = installer.Install(reference, PackageType.Tool, toolsPath, source);

        var paths = new List<FilePath>(files.Count);
        foreach (var file in files)
        {
            context.Tools.RegisterFile(file.Path);
            paths.Add(file.Path);
        }

        return paths;
    }
}
