using Cake.Core.IO;

namespace Build.RunnerTests;

/// <summary>
/// Everything a runner needs to know about the current runner-test run. Each runner works in
/// <c>&lt;run&gt;/&lt;runner&gt;/src</c> (working directory, so its tools land in <c>src/tools</c>) and writes to
/// <c>&lt;run&gt;/&lt;runner&gt;/out</c>.
/// </summary>
public sealed record RunnerTestContext(string CakeVersion, string ModuleVersion, DirectoryPath RepositoryRoot, DirectoryPath RunDirectory)
{
    public DirectoryPath RunnersDirectory => RepositoryRoot.Combine("test/runners");

    public DirectoryPath ArtifactsDirectory => RepositoryRoot.Combine("artifacts");

    public DirectoryPath NuGetPackagesDirectory => RunDirectory.Combine("nuget-packages");

    public DirectoryPath WorkDirectory(string runner) => RunDirectory.Combine(runner);

    public DirectoryPath SourceDirectory(string runner) => WorkDirectory(runner).Combine("src");

    public DirectoryPath OutputDirectory(string runner) => WorkDirectory(runner).Combine("out");
}
