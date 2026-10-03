using Cake.Common.Diagnostics;
using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Pack;
using Cake.Core;
using Cake.Frosting;

namespace Build.Tasks;

/// <summary>
/// Packs Cake.Download.Module into ./artifacts and verifies the package content for this version.
/// </summary>
[TaskName("Pack")]
[IsDependentOn(typeof(BuildTask))]
public sealed class PackTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        context.DotNetPack(BuildContext.Solution, new DotNetPackSettings
        {
            Configuration = "Release",
            Verbosity = DotNetVerbosity.Minimal,
            NoBuild = true,
            NoRestore = true,
            OutputDirectory = context.ArtifactsDirectory,
        });

        if (!context.FileExists(context.PackageFile))
        {
            throw new CakeException($"Pack did not produce {context.PackageFile.GetFilename()}.");
        }

        var problems = PackageVerifier.Verify(context.PackageFile.FullPath);
        if (problems.Count > 0)
        {
            throw new CakeException($"Package {context.PackageFile.GetFilename()} is invalid: {string.Join("; ", problems)}");
        }

        context.Information("Verified {0}", context.PackageFile.GetFilename());
    }
}
