using Cake.Core;
using Cake.Core.IO;

namespace Build.RunnerTests;

/// <summary>
/// Runs <c>test/runners/sdk/cake.cs</c> as a Cake.Sdk file-based app at the requested Cake version.
/// </summary>
public sealed class SdkRunner : IRunner
{
    public string Name => "sdk";

    public int Prepare(ICakeContext context, RunnerTestContext test)
    {
        var source = test.SourceDirectory(Name);
        NuGetConfig.Write(source, test.ArtifactsDirectory);
        ScriptTemplate.Render(
            test.RunnersDirectory.CombineWithFilePath("sdk/cake.cs"),
            source.CombineWithFilePath("cake.cs"),
            new Dictionary<string, string>
            {
                [@"^#:sdk Cake\.Sdk@[^\r\n]*"] = $"#:sdk Cake.Sdk@{test.CakeVersion}",
                [@"^#:package Cake\.Download\.Module@[^\r\n]*"] = $"#:package Cake.Download.Module@{test.ModuleVersion}",
            });
        return 0;
    }

    public int Run(ICakeContext context, RunnerTestContext test) => RunnerProcess.Run(
        context,
        test,
        "dotnet",
        new ProcessArgumentBuilder()
            .Append("run")
            .Append("--no-cache")
            .AppendSwitchQuoted("--file", test.SourceDirectory(Name).CombineWithFilePath("cake.cs").FullPath)
            .Append("--")
            .AppendSwitchQuoted("--output", "=", test.OutputDirectory(Name).FullPath),
        test.SourceDirectory(Name));
}
