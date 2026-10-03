using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Test;
using Cake.Frosting;

namespace Build.Tasks;

[TaskName("Test")]
[IsDependentOn(typeof(BuildTask))]
public sealed class TestTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        context.DotNetTest(BuildContext.Solution, new DotNetTestSettings
        {
            Configuration = "Release",
            Verbosity = DotNetVerbosity.Minimal,
            NoBuild = true,
            PathType = DotNetTestPathType.Solution,
        });

        context.DotNetTest("./test/Build.Tests/Build.Tests.csproj", new DotNetTestSettings
        {
            Configuration = "Release",
            Verbosity = DotNetVerbosity.Minimal,
            PathType = DotNetTestPathType.Project,
        });
    }
}
