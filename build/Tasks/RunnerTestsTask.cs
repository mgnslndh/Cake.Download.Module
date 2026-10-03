using Build.RunnerTests;
using Cake.Common;
using Cake.Common.Diagnostics;
using Cake.Common.IO;
using Cake.Core;
using Cake.Frosting;

namespace Build.Tasks;

/// <summary>
/// Runs the packed module in every Cake runner, twice each, against real GitHub releases. Usage:
/// <c>./build.ps1 --target RunnerTests [--cake-version 6.0.0|6.*]</c>.
/// </summary>
[TaskName("RunnerTests")]
[IsDependentOn(typeof(PackTask))]
public sealed class RunnerTestsTask : FrostingTask<BuildContext>
{
    private static readonly IRunner[] Runners = [new ScriptRunner(), new SdkRunner(), new FrostingRunner()];

    public override void Run(BuildContext context)
    {
        var root = context.Environment.WorkingDirectory;
        var runners = root.Combine("test/runners");
        ScriptTemplate.EnsureConsistent(
            File.ReadAllText(runners.CombineWithFilePath("script/build.cake").FullPath),
            File.ReadAllText(runners.CombineWithFilePath("sdk/cake.cs").FullPath),
            File.ReadAllText(runners.CombineWithFilePath("frosting/Program.cs").FullPath));

        if (!context.FileExists(context.PackageFile))
        {
            throw new CakeException($"Package '{context.PackageFile.FullPath}' was not found. Run the Pack target first.");
        }

        var cakeVersion = CakeVersionResolver.Resolve(context.Argument("cake-version", "6.*"));
        var runDirectory = root.Combine($"artifacts/runner-test/{cakeVersion}");
        context.EnsureDirectoryExists(runDirectory);
        context.CleanDirectory(runDirectory);

        var test = new RunnerTestContext(cakeVersion, ThisAssembly.PackageVersion, root, runDirectory);
        context.Information("Runner tests: Cake {0}, Cake.Download.Module {1}", cakeVersion, test.ModuleVersion);

        var results = Runners.Select(runner => RunOne(context, test, runner)).ToList();
        var reference = results.FirstOrDefault(result => result.Report is not null);
        if (reference is not null)
        {
            foreach (var result in results.Where(result => result.Report is not null && result != reference))
            {
                result.Failures.AddRange(ReportAssertions.Compare(reference.Name, reference.Report!, result.Report!));
            }
        }

        context.Information("=== Runner test summary ===");
        foreach (var result in results)
        {
            if (result.Passed)
            {
                context.Information("  {0} passed", result.Name.PadRight(10));
                continue;
            }

            context.Error("  {0} FAILED", result.Name.PadRight(10));
            foreach (var failure in result.Failures)
            {
                context.Error("      {0}", failure);
            }
        }

        if (results.Any(result => !result.Passed))
        {
            throw new CakeException("Runner tests failed. See the summary above.");
        }
    }

    private static RunnerResult RunOne(ICakeContext context, RunnerTestContext test, IRunner runner)
    {
        var result = new RunnerResult(runner.Name);
        var tools = test.SourceDirectory(runner.Name).Combine("tools").FullPath;
        context.EnsureDirectoryExists(test.SourceDirectory(runner.Name));
        context.EnsureDirectoryExists(test.OutputDirectory(runner.Name));
        context.Information("=== Runner: {0} ===", runner.Name);

        try
        {
            var prepared = runner.Prepare(context, test);
            if (prepared != 0)
            {
                result.Failures.Add($"preparing exited with code {prepared}");
                return result;
            }

            var first = runner.Run(context, test);
            if (first != 0)
            {
                result.Failures.Add($"the first run exited with code {first}");
                return result;
            }

            var before = MarkerSnapshot.Take(tools);
            var second = runner.Run(context, test);
            if (second != 0)
            {
                result.Failures.Add($"the second run exited with code {second}");
                return result;
            }

            result.Failures.AddRange(MarkerSnapshot.Compare(before, MarkerSnapshot.Take(tools)));
            result.Report = ToolReport.Read(test.OutputDirectory(runner.Name).CombineWithFilePath("report.json").FullPath);
            result.Failures.AddRange(ReportAssertions.Check(result.Report, context.IsRunningOnWindows()));
        }
        catch (Exception exception)
        {
            result.Failures.Add($"runner threw {exception.GetType().Name}: {exception.Message}");
        }

        return result;
    }
}
