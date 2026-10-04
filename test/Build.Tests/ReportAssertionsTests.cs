using Build.RunnerTests;

namespace Build.Tests;

public sealed class ReportAssertionsTests : IDisposable
{
    private readonly string _tools = Directory.CreateTempSubdirectory("ReportAssertionsTests").FullName;

    private static readonly string Sha = new('a', 64);

    private static readonly ToolReportEntry[] Linux =
    [
        new("jq", "jq.1.8.2/jq", Sha, "jq-1.8.2"),
        new("gh", "gh.2.62.0/gh_2.62.0_linux_amd64/bin/gh", Sha, "gh version 2.62.0 (2024-11-14)\nhttps://github.com/cli/cli/releases/tag/v2.62.0"),
        new("rg", "rg.14.1.1/ripgrep-14.1.1-x86_64-unknown-linux-musl/rg", Sha, "ripgrep 14.1.1\n\nfeatures:+pcre2"),
        new("cyclonedx", "cyclonedx.0.30.0/cyclonedx", Sha, "0.30.0+abc"),
    ];

    public void Dispose() => Directory.Delete(_tools, recursive: true);

    [Fact]
    public void CheckDryRun_Accepts_A_Tools_Folder_Without_On_Demand_Installs()
    {
        Directory.CreateDirectory(Path.Combine(_tools, "gh.2.62.0"));

        Assert.Empty(ReportAssertions.CheckDryRun(_tools));
    }

    [Fact]
    public void CheckDryRun_Reports_An_On_Demand_Install()
    {
        Directory.CreateDirectory(Path.Combine(_tools, "jq.1.8.2"));

        Assert.Equal(["--dryrun installed jq.1.8.2; DownloadTool must only install when its task runs"], ReportAssertions.CheckDryRun(_tools));
    }

    [Fact]
    public void CheckDryRun_Reports_The_Settings_Install_Too()
    {
        Directory.CreateDirectory(Path.Combine(_tools, "cyclonedx.0.30.0"));

        Assert.Equal(["--dryrun installed cyclonedx.0.30.0; DownloadTool must only install when its task runs"], ReportAssertions.CheckDryRun(_tools));
    }

    [Fact]
    public void Check_Accepts_A_Complete_Report()
    {
        Assert.Empty(ReportAssertions.Check(Linux, isWindows: false));
    }

    [Fact]
    public void Check_Expects_Exe_Paths_On_Windows()
    {
        var failures = ReportAssertions.Check(Linux, isWindows: true);

        Assert.Contains("jq: path 'jq.1.8.2/jq' does not match 'jq.1.8.2/jq.exe'", failures);
    }

    [Fact]
    public void Check_Reports_Missing_Tools_Wrong_Versions_And_Bad_Hashes()
    {
        var report = new[]
        {
            Linux[0] with { Version = "jq-1.7.1" },
            Linux[1] with { Sha256 = "nope" },
            Linux[2],
        };

        Assert.Equal(
            [
                "jq: version output 'jq-1.7.1' does not contain 'jq-1.8.2'",
                "gh: sha256 'nope' is not a SHA-256 hash",
                "cyclonedx: missing from the report",
            ],
            ReportAssertions.Check(report, isWindows: false));
    }

    [Fact]
    public void Compare_Reports_Differences_Between_Runners()
    {
        var other = new[] { Linux[0], Linux[1] with { Sha256 = new string('b', 64) }, Linux[2], Linux[3] };

        Assert.Equal(["gh: sha256 differs from the script runner"], ReportAssertions.Compare("script", Linux, other));
    }
}
