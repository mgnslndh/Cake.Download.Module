using System.Text.Json;
using Cake.Common;
using Cake.Common.IO;
using Cake.Common.Security;
using Cake.Core;
using Cake.Core.IO;
using Cake.Frosting;

namespace Frosting;

[TaskName("Default")]
[IsDependentOn(typeof(OnDemandTask))]
public sealed class DefaultTask : FrostingTask<ScenarioContext>
{
    private static readonly string[] ToolNames = ["jq", "gh", "rg", "cyclonedx"];

    public override void Run(ScenarioContext context)
    {
        context.EnsureDirectoryExists(context.Output);
        var toolsDirectory = context.MakeAbsolute(new DirectoryPath("./tools"));
        var report = new List<Dictionary<string, string>>();
        foreach (var name in ToolNames)
        {
            var path = context.Tools.Resolve(context.IsRunningOnWindows() ? name + ".exe" : name)
                ?? throw new CakeException($"Cake did not resolve the tool '{name}'.");
            var exitCode = context.StartProcess(path, new ProcessSettings { Arguments = "--version", RedirectStandardOutput = true }, out var lines);
            if (exitCode != 0)
            {
                throw new CakeException($"'{name} --version' exited with code {exitCode}.");
            }

            report.Add(new Dictionary<string, string>
            {
                ["name"] = name,
                ["path"] = toolsDirectory.GetRelativePath(path).FullPath,
                ["sha256"] = context.CalculateFileHash(path).ToHex(),
                ["version"] = string.Join("\n", lines).Trim(),
            });
        }

        File.WriteAllText(
            context.Output.CombineWithFilePath("report.json").FullPath,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }
}
