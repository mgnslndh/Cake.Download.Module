using Cake.Common;
using Cake.Core;
using Cake.Core.IO;

namespace Build.RunnerTests;

/// <summary>
/// Runs <c>test/runners/script/build.cake</c> with a private Cake .NET Tool install of the requested version.
/// </summary>
public sealed class ScriptRunner : IRunner
{
    public string Name => "script";

    public int Prepare(ICakeContext context, RunnerTestContext test)
    {
        var source = test.SourceDirectory(Name);
        NuGetConfig.Write(source, test.ArtifactsDirectory);
        ScriptTemplate.Render(
            test.RunnersDirectory.CombineWithFilePath("script/build.cake"),
            source.CombineWithFilePath("build.cake"),
            new Dictionary<string, string>
            {
                [@"^#module nuget:\?package=Cake\.Download\.Module&version=[^\r\n]*"] =
                    $"#module nuget:?package=Cake.Download.Module&version={test.ModuleVersion}&prerelease",
            });

        return RunnerProcess.Run(
            context,
            test,
            "dotnet",
            new ProcessArgumentBuilder()
                .Append("tool")
                .Append("install")
                .Append("Cake.Tool")
                .AppendSwitch("--version", test.CakeVersion)
                .AppendSwitchQuoted("--tool-path", ToolDirectory(test).FullPath),
            test.WorkDirectory(Name));
    }

    public int Run(ICakeContext context, RunnerTestContext test)
    {
        var executable = ToolDirectory(test).CombineWithFilePath(context.IsRunningOnWindows() ? "dotnet-cake.exe" : "dotnet-cake");
        return RunnerProcess.Run(
            context,
            test,
            executable,
            new ProcessArgumentBuilder()
                .Append("build.cake")
                .AppendSwitchQuoted("--output", "=", test.OutputDirectory(Name).FullPath),
            test.SourceDirectory(Name));
    }

    private DirectoryPath ToolDirectory(RunnerTestContext test) => test.WorkDirectory(Name).Combine("cake-tool");
}
