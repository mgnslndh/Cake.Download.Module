using Cake.Core.Packaging;
using Cake.Download.Module.Directives;
using Cake.Download.Module.Installation;
using Cake.Download.Module.Platforms;
using Cake.Download.Module.Tests.Fakes;

namespace Cake.Download.Module.Tests.Installation;

public sealed class InstallStoreTests : IDisposable
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly TestDirectory _directory = new();
    private readonly InstallStore _store;
    private readonly DownloadPlan _plan = DownloadPlanner.Create(
        DirectiveParser.Parse(new PackageReference("download:https://example.com/jq-{os}-{arch}?package=jq&version=1.8.2&sha256=" + Hash)),
        PlatformInfo.FromRid("linux-x64"));

    public InstallStoreTests()
    {
        _store = new InstallStore(_directory.Root, new FixedTimeProvider(Now), _ => { });
    }

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void GetInstallDirectory_Is_Package_Dot_Version_Under_The_Tools_Folder()
    {
        Assert.Equal(_directory.Combine("jq.1.8.2"), _store.GetInstallDirectory(_plan));
    }

    [Fact]
    public void IsCurrent_Is_False_Without_A_Marker_And_True_After_Publish()
    {
        Assert.False(_store.IsCurrent(_plan));

        PublishWithFile(_plan, "jq", "v1");

        Assert.True(_store.IsCurrent(_plan));
        Assert.Equal("v1", File.ReadAllText(_directory.Combine("jq.1.8.2", "jq")));
    }

    [Fact]
    public void Publish_Writes_The_Marker()
    {
        PublishWithFile(_plan, "jq", "v1");

        var marker = InstallStore.ReadMarker(_directory.Combine("jq.1.8.2"))!;
        Assert.Equal(new InstallMarker(1, "jq", "1.8.2", "linux-x64", "https://example.com/jq-linux-amd64", "file", "jq", "sha256:" + Hash, Hash, Now), marker);

        var json = File.ReadAllText(_directory.Combine("jq.1.8.2", ".cake-download.json"));
        Assert.Contains("\"schema\": 1", json);
        Assert.Contains("\"integrity\": \"sha256:" + Hash + "\"", json);
    }

    public static TheoryData<string> ChangedFields => ["url", "integrity", "rid", "fileName", "format"];

    [Theory]
    [MemberData(nameof(ChangedFields))]
    public void IsCurrent_Is_False_When_A_Compared_Field_Changes(string field)
    {
        PublishWithFile(_plan, "jq", "v1");

        var changed = field switch
        {
            "url" => _plan with { Url = new Uri("https://example.com/other") },
            "integrity" => _plan with { Integrity = new PinnedSha256(new string('f', 64), "sha256") },
            "rid" => _plan with { Platform = PlatformInfo.FromRid("linux-arm64") },
            "fileName" => _plan with { FileName = "jq2" },
            _ => _plan with { Format = ArchiveFormat.Zip, FileName = null },
        };

        Assert.False(_store.IsCurrent(changed));
    }

    [Fact]
    public void IsCurrent_Is_False_For_An_Unreadable_Marker()
    {
        Directory.CreateDirectory(_directory.Combine("jq.1.8.2"));
        File.WriteAllText(_directory.Combine("jq.1.8.2", ".cake-download.json"), "{ not json");

        Assert.False(_store.IsCurrent(_plan));
    }

    [Fact]
    public void Publish_Replaces_A_Stale_Install()
    {
        Directory.CreateDirectory(_directory.Combine("jq.1.8.2"));
        File.WriteAllText(_directory.Combine("jq.1.8.2", "old-file"), "stale");

        PublishWithFile(_plan, "jq", "v1");

        Assert.False(File.Exists(_directory.Combine("jq.1.8.2", "old-file")));
        Assert.True(_store.IsCurrent(_plan));
    }

    [Fact]
    public void Publish_Keeps_A_Matching_Install_Published_By_Another_Build()
    {
        PublishWithFile(_plan, "jq", "first");
        PublishWithFile(_plan, "jq", "second");

        Assert.Equal("first", File.ReadAllText(_directory.Combine("jq.1.8.2", "jq")));
    }

    [Fact]
    public void Publish_With_ReplaceCurrent_Replaces_A_Matching_Install()
    {
        PublishWithFile(_plan, "jq", "first");
        PublishWithFile(_plan, "jq", "second", replaceCurrent: true);

        Assert.Equal("second", File.ReadAllText(_directory.Combine("jq.1.8.2", "jq")));
    }

    [Fact]
    public void Staging_Is_Removed_On_Dispose_And_Stale_Staging_Is_Cleaned_Up()
    {
        var stale = Directory.CreateDirectory(_directory.Combine(".jq.1.8.2.tmp-old")).FullName;
        Directory.SetLastWriteTimeUtc(stale, Now.UtcDateTime.AddHours(-2));
        var fresh = Directory.CreateDirectory(_directory.Combine(".jq.1.8.2.tmp-fresh")).FullName;
        Directory.SetLastWriteTimeUtc(fresh, Now.UtcDateTime.AddMinutes(-10));

        string root;
        using (var staging = _store.CreateStaging(_plan))
        {
            root = staging.Root;
            Assert.True(Directory.Exists(staging.DownloadDirectory));
            Assert.True(Directory.Exists(staging.ContentDirectory));
            Assert.StartsWith(_directory.Combine(".jq.1.8.2.tmp-"), root);
        }

        Assert.False(Directory.Exists(root));
        Assert.False(Directory.Exists(stale));
        Assert.True(Directory.Exists(fresh));
    }

    private void PublishWithFile(DownloadPlan plan, string fileName, string content, bool replaceCurrent = false)
    {
        using var staging = _store.CreateStaging(plan);
        File.WriteAllText(Path.Combine(staging.ContentDirectory, fileName), content);
        _store.Publish(plan, staging, _store.CreateMarker(plan, Hash), replaceCurrent);
    }
}
