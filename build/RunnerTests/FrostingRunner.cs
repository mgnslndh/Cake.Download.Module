using Cake.Core;
using Cake.Core.IO;

namespace Build.RunnerTests;

/// <summary>
/// Builds <c>test/runners/frosting</c> against the requested Cake.Frosting and the packed module, then runs it.
/// </summary>
public sealed class FrostingRunner : IRunner
{
    public string Name => "frosting";

    public int Prepare(ICakeContext context, RunnerTestContext test)
    {
        var nugetConfig = NuGetConfig.Write(test.WorkDirectory(Name), test.ArtifactsDirectory);
        return RunnerProcess.Run(
            context,
            test,
            "dotnet",
            new ProcessArgumentBuilder()
                .Append("build")
                .AppendQuoted(test.RunnersDirectory.CombineWithFilePath("frosting/Frosting.csproj").FullPath)
                .Append("--force")
                .AppendSwitch("--configuration", "Release")
                .AppendSwitchQuoted("--output", BinDirectory(test).FullPath)
                .Append($"--property:CakeVersion={test.CakeVersion}")
                .Append($"--property:ModuleVersion={test.ModuleVersion}")
                .AppendQuoted($"--property:RestoreConfigFile={nugetConfig.FullPath}"),
            test.WorkDirectory(Name));
    }

    public int Run(ICakeContext context, RunnerTestContext test) => RunnerProcess.Run(
        context,
        test,
        "dotnet",
        new ProcessArgumentBuilder()
            .AppendQuoted(BinDirectory(test).CombineWithFilePath("Frosting.dll").FullPath)
            .AppendSwitchQuoted("--output", "=", test.OutputDirectory(Name).FullPath),
        test.SourceDirectory(Name));

    private DirectoryPath BinDirectory(RunnerTestContext test) => test.WorkDirectory(Name).Combine("bin");
}
