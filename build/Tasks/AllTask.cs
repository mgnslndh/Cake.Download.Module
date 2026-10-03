using Cake.Frosting;

namespace Build.Tasks;

[TaskName("All")]
[IsDependentOn(typeof(BuildTask))]
[IsDependentOn(typeof(TestTask))]
[IsDependentOn(typeof(PackTask))]
public sealed class AllTask : FrostingTask
{
}
