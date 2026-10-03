using Cake.Core;
using Cake.Download.Module.Archives;
using Cake.Download.Module.Directives;
using Cake.Download.Module.Tests.Fakes;

namespace Cake.Download.Module.Tests.Archives;

public sealed class ArchiveExtractorTests : IDisposable
{
    private const UnixFileMode Executable =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    private readonly TestDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Theory]
    [InlineData("zip")]
    [InlineData("tar")]
    [InlineData("tar.gz")]
    public void Extract_Keeps_The_Archive_Layout_Verbatim(string format)
    {
        var target = Extract(
            ArchiveFormats.FromName(format),
            ArchiveEntrySpec.Directory("gh_2.62.0_linux_amd64"),
            new ArchiveEntrySpec("gh_2.62.0_linux_amd64/bin/gh", "binary"),
            new ArchiveEntrySpec("gh_2.62.0_linux_amd64/LICENSE", "MIT"));

        Assert.Equal("binary", File.ReadAllText(Path.Combine(target, "gh_2.62.0_linux_amd64", "bin", "gh")));
        Assert.Equal("MIT", File.ReadAllText(Path.Combine(target, "gh_2.62.0_linux_amd64", "LICENSE")));
    }

    [Theory]
    [InlineData("zip")]
    [InlineData("tar.gz")]
    public void Extract_Keeps_Unix_Execute_Bits(string format)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes only.");
            return;
        }

        var target = Extract(ArchiveFormats.FromName(format), new ArchiveEntrySpec("bin/tool", "binary", Executable));

        Assert.Equal(Executable, File.GetUnixFileMode(Path.Combine(target, "bin", "tool")));
    }

    [Theory]
    [InlineData("zip", "../evil.txt")]
    [InlineData("zip", "a/../../evil.txt")]
    [InlineData("tar", "../evil.txt")]
    [InlineData("tar.gz", "/etc/evil.txt")]
    public void Extract_Rejects_Entries_Outside_The_Destination(string format, string name)
    {
        var exception = Assert.Throws<CakeException>(() => Extract(ArchiveFormats.FromName(format), new ArchiveEntrySpec(name, "evil")));

        Assert.StartsWith($"Refusing to extract archive entry '{name}': ", exception.Message);
        Assert.False(File.Exists(_directory.Combine("sandbox", "evil.txt")));
    }

    [Fact]
    public void Extract_Rejects_A_Symlink_Pointing_Outside_The_Destination()
    {
        var exception = Assert.Throws<CakeException>(
            () => Extract(ArchiveFormat.TarGz, ArchiveEntrySpec.Symlink("bin/tool", "../../../outside")));

        Assert.Equal("Refusing to extract archive entry 'bin/tool': its link target '../../../outside' is outside the target folder.", exception.Message);
    }

    [Fact]
    public void Extract_Materializes_Links_Inside_The_Destination()
    {
        var target = Extract(
            ArchiveFormat.TarGz,
            new ArchiveEntrySpec("tool-1.0/bin/tool", "binary"),
            ArchiveEntrySpec.Symlink("tool-1.0/tool", "bin/tool"),
            ArchiveEntrySpec.Hardlink("tool-1.0/tool-copy", "tool-1.0/bin/tool"));

        Assert.Equal("binary", File.ReadAllText(Path.Combine(target, "tool-1.0", "tool")));
        Assert.Equal("binary", File.ReadAllText(Path.Combine(target, "tool-1.0", "tool-copy")));
    }

    [Fact]
    public void Extract_Rejects_A_Chained_Symlink_Escape()
    {
        var exception = Assert.Throws<CakeException>(() => Extract(
            ArchiveFormat.TarGz,
            new ArchiveEntrySpec("a/b/x", "x"),
            ArchiveEntrySpec.Symlink("a/b/l2", "../.."),
            ArchiveEntrySpec.Symlink("d", "a/b/l2/..")));

        Assert.Contains("'d': its link target 'a/b/l2/..' passes through another link", exception.Message);
    }

    [Fact]
    public void Extract_Rejects_A_Hard_Link_Through_A_Symlink()
    {
        var exception = Assert.Throws<CakeException>(() => Extract(
            ArchiveFormat.TarGz,
            new ArchiveEntrySpec("bin/tool", "binary"),
            ArchiveEntrySpec.Symlink("s", "bin"),
            ArchiveEntrySpec.Hardlink("h", "s/tool")));

        Assert.Contains("'h': its link target 's/tool' passes through another link", exception.Message);
    }

    [Fact]
    public void Extract_Rejects_A_Hard_Link_To_A_Missing_Target()
    {
        var exception = Assert.Throws<CakeException>(
            () => Extract(ArchiveFormat.TarGz, ArchiveEntrySpec.Hardlink("h", "missing")));

        Assert.Contains("'h': its link target 'missing' does not exist in the archive.", exception.Message);
    }

    [Fact]
    public void Extract_Rejects_Raw_Files()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ArchiveExtractor.Extract(_directory.Combine("x"), ArchiveFormat.File, _directory.Combine("out")));
    }

    private string Extract(ArchiveFormat format, params ArchiveEntrySpec[] entries)
    {
        var archive = _directory.Combine("archive");
        File.WriteAllBytes(archive, format == ArchiveFormat.Zip ? TestArchives.Zip(entries) : TestArchives.Tar(format == ArchiveFormat.TarGz, entries));
        var target = _directory.Combine("sandbox", "content");
        ArchiveExtractor.Extract(archive, format, target);
        return target;
    }
}
