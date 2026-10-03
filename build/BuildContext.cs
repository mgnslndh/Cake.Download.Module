using Cake.Core;
using Cake.Core.IO;
using Cake.Frosting;

namespace Build;

public class BuildContext : FrostingContext
{
    public const string Solution = "./src/Cake.Download.Module.slnx";

    public BuildContext(ICakeContext context)
        : base(context)
    {
        ArtifactsDirectory = context.Environment.WorkingDirectory.Combine("artifacts");
    }

    /// <summary>Gets the directory packages are written to.</summary>
    public DirectoryPath ArtifactsDirectory { get; }

    /// <summary>Gets the package this build produces.</summary>
    public FilePath PackageFile => ArtifactsDirectory.CombineWithFilePath($"Cake.Download.Module.{ThisAssembly.PackageVersion}.nupkg");
}
