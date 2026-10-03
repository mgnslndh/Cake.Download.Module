using Cake.Core;

namespace Build.RunnerTests;

/// <summary>
/// A Cake runner under test. <see cref="Prepare"/> runs once; <see cref="Run"/> runs twice to prove the cache hit.
/// </summary>
public interface IRunner
{
    string Name { get; }

    /// <returns>The exit code; 0 means success.</returns>
    int Prepare(ICakeContext context, RunnerTestContext test);

    /// <returns>The exit code; 0 means success.</returns>
    int Run(ICakeContext context, RunnerTestContext test);
}
