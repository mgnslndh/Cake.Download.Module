namespace Build.RunnerTests;

public sealed class RunnerResult(string name)
{
    public string Name { get; } = name;

    public List<string> Failures { get; } = [];

    public IReadOnlyList<ToolReportEntry>? Report { get; set; }

    public bool Passed => Failures.Count == 0;
}
