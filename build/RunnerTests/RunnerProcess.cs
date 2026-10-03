using Cake.Common;
using Cake.Core;
using Cake.Core.IO;

namespace Build.RunnerTests;

/// <summary>
/// Starts runner processes with an isolated NuGet package folder, so a locally rebuilt module that keeps its version is
/// never served from a stale cache.
/// </summary>
public static class RunnerProcess
{
    public static int Run(ICakeContext context, RunnerTestContext test, FilePath executable, ProcessArgumentBuilder arguments, DirectoryPath workingDirectory)
    {
        Directory.CreateDirectory(workingDirectory.FullPath);
        return context.StartProcess(executable, new ProcessSettings
        {
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["NUGET_PACKAGES"] = test.NuGetPackagesDirectory.FullPath,
            },
        });
    }
}
