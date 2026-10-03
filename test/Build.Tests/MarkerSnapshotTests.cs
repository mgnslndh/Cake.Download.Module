using Build.RunnerTests;

namespace Build.Tests;

public sealed class MarkerSnapshotTests : IDisposable
{
    private readonly string _tools = Directory.CreateTempSubdirectory("MarkerSnapshotTests").FullName;

    public void Dispose() => Directory.Delete(_tools, recursive: true);

    [Fact]
    public void Take_Reads_Every_Install_Marker()
    {
        Write("jq.1.8.2", "one");
        Write("gh.2.62.0", "two");
        Directory.CreateDirectory(Path.Combine(_tools, "Modules"));

        var snapshot = MarkerSnapshot.Take(_tools);

        Assert.Equal(["gh.2.62.0", "jq.1.8.2"], snapshot.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("one", snapshot["jq.1.8.2"]);
    }

    [Fact]
    public void Compare_Reports_Changed_Missing_And_Empty_Snapshots()
    {
        var first = new Dictionary<string, string> { ["jq.1.8.2"] = "one", ["gh.2.62.0"] = "two" };
        var second = new Dictionary<string, string> { ["jq.1.8.2"] = "changed" };

        Assert.Equal(
            ["the second run removed gh.2.62.0", "the second run reinstalled jq.1.8.2"],
            MarkerSnapshot.Compare(first, second));
        Assert.Equal(["no .cake-download.json markers were written"], MarkerSnapshot.Compare(new Dictionary<string, string>(), second));
        Assert.Empty(MarkerSnapshot.Compare(first, first));
    }

    private void Write(string folder, string content)
    {
        Directory.CreateDirectory(Path.Combine(_tools, folder));
        File.WriteAllText(Path.Combine(_tools, folder, ".cake-download.json"), content);
    }
}
