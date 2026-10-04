using Cake.Frosting;

namespace Build.Tasks;

[TaskName("Default")]
[IsDependentOn(typeof(BuildTask))]
public sealed class DefaultTask : FrostingTask
{
}
