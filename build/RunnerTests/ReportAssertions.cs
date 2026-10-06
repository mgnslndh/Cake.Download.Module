using System.Text.RegularExpressions;

namespace Build.RunnerTests;

/// <summary>
/// What every runner must report for the scenario's four tools.
/// </summary>
public static partial class ReportAssertions
{
    /// <summary>Install folders of the tools the scenario installs with <c>DownloadTool</c> inside a task.</summary>
    public static readonly string[] OnDemandInstallFolders = ["jq.1.8.2", "cyclonedx.0.30.0"];

    public static IReadOnlyList<string> CheckDryRun(string toolsDirectory) =>
        OnDemandInstallFolders
            .Where(folder => Directory.Exists(Path.Combine(toolsDirectory, folder)))
            .Select(folder => $"--dryrun installed {folder}; DownloadTool must only install when its task runs")
            .ToList();

    public static IReadOnlyList<string> Check(IReadOnlyList<ToolReportEntry> report, bool isWindows)
    {
        var exe = isWindows ? ".exe" : string.Empty;
        var expectations = new (string Name, string Version, Func<string, bool> PathMatches, string PathDescription)[]
        {
            ("jq", "jq-1.8.2", path => path == $"jq.1.8.2/jq{exe}", $"jq.1.8.2/jq{exe}"),
            ("gh", "gh version 2.62.0", path => path.StartsWith("gh.2.62.0/", StringComparison.Ordinal) && path.EndsWith($"/bin/gh{exe}", StringComparison.Ordinal), $"gh.2.62.0/**/bin/gh{exe}"),
            ("rg", "ripgrep 14.1.1", path => path.StartsWith("rg.14.1.1/ripgrep-14.1.1-", StringComparison.Ordinal) && path.EndsWith($"/rg{exe}", StringComparison.Ordinal), $"rg.14.1.1/ripgrep-14.1.1-*/rg{exe}"),
            ("cyclonedx", "0.30.0", path => path == $"cyclonedx.0.30.0/cyclonedx{exe}", $"cyclonedx.0.30.0/cyclonedx{exe}"),
        };

        var failures = new List<string>();
        foreach (var expected in expectations)
        {
            var entry = report.FirstOrDefault(candidate => candidate.Name == expected.Name);
            if (entry is null)
            {
                failures.Add($"{expected.Name}: missing from the report");
                continue;
            }

            if (!expected.PathMatches(entry.Path))
            {
                failures.Add($"{expected.Name}: path '{entry.Path}' does not match '{expected.PathDescription}'");
            }

            if (!Sha256().IsMatch(entry.Sha256))
            {
                failures.Add($"{expected.Name}: sha256 '{entry.Sha256}' is not a SHA-256 hash");
            }

            if (!entry.Version.Contains(expected.Version, StringComparison.Ordinal))
            {
                failures.Add($"{expected.Name}: version output '{entry.Version}' does not contain '{expected.Version}'");
            }
        }

        return failures;
    }

    public static IReadOnlyList<string> Compare(string referenceName, IReadOnlyList<ToolReportEntry> reference, IReadOnlyList<ToolReportEntry> other)
    {
        var failures = new List<string>();
        foreach (var expected in reference)
        {
            var actual = other.FirstOrDefault(entry => entry.Name == expected.Name);
            if (actual is null)
            {
                continue;
            }

            if (actual.Path != expected.Path)
            {
                failures.Add($"{expected.Name}: path differs from the {referenceName} runner");
            }

            if (actual.Sha256 != expected.Sha256)
            {
                failures.Add($"{expected.Name}: sha256 differs from the {referenceName} runner");
            }

            if (actual.Version != expected.Version)
            {
                failures.Add($"{expected.Name}: version output differs from the {referenceName} runner");
            }
        }

        return failures;
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256();
}
