using Cake.Core;
using Cake.Core.Diagnostics;
using Cake.Download.Module.Archives;
using Cake.Download.Module.Directives;
using Cake.Download.Module.Tests.Fakes;
using Cake.Testing;

namespace Cake.Download.Module.Tests.Archives;

public sealed class ArchiveExtractorTests : IDisposable
{
    private const UnixFileMode Executable =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    private readonly TestDirectory _directory = new();

    private readonly FakeLog _log = new();

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

    [Theory]
    [InlineData("zip")]
    [InlineData("tar.gz")]
    public void Extract_Rejects_A_Symlink_That_Leaves_The_Destination_And_Comes_Back(string format)
    {
        // The destination is renamed after extraction, so a target that climbs out and back in by its folder name
        // would point somewhere else once the install is published.
        var exception = Assert.Throws<CakeException>(() => Extract(
            ArchiveFormats.FromName(format),
            new ArchiveEntrySpec("x", "x"),
            ArchiveEntrySpec.Symlink("bin/tool", "../../content/x")));

        Assert.Equal("Refusing to extract archive entry 'bin/tool': its link target '../../content/x' is outside the target folder.", exception.Message);
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
    public void Extract_Follows_A_Symlink_Chain()
    {
        var target = Extract(
            ArchiveFormat.TarGz,
            new ArchiveEntrySpec("lib/libfoo.so.1.2.3", "library"),
            ArchiveEntrySpec.Symlink("lib/libfoo.so.1", "libfoo.so.1.2.3"),
            ArchiveEntrySpec.Symlink("lib/libfoo.so", "libfoo.so.1"));

        Assert.Equal("library", File.ReadAllText(Path.Combine(target, "lib", "libfoo.so")));
    }

    [Fact]
    public void Extract_Follows_A_Symlink_Chain_Listed_Before_Its_Targets()
    {
        var target = Extract(
            ArchiveFormat.TarGz,
            ArchiveEntrySpec.Symlink("lib/libfoo.so", "libfoo.so.1"),
            ArchiveEntrySpec.Symlink("lib/libfoo.so.1", "libfoo.so.1.2.3"),
            new ArchiveEntrySpec("lib/libfoo.so.1.2.3", "library"));

        Assert.Equal("library", File.ReadAllText(Path.Combine(target, "lib", "libfoo.so")));
    }

    [Fact]
    public void Extract_Materializes_A_Symlink_To_A_Directory()
    {
        var target = Extract(
            ArchiveFormat.TarGz,
            ArchiveEntrySpec.Symlink("current", "tool-1.2"),
            new ArchiveEntrySpec("tool-1.2/bin/tool.exe", "binary"),
            ArchiveEntrySpec.Symlink("tool-1.2/bin/tool", "tool.exe"));

        Assert.Equal("binary", File.ReadAllText(Path.Combine(target, "current", "bin", "tool.exe")));
        Assert.Equal("binary", File.ReadAllText(Path.Combine(target, "current", "bin", "tool")));
    }

    [Theory]
    [InlineData("a", "b", "b", "a")]
    [InlineData("a/loop", "..", "b", "a")]
    public void Extract_Rejects_A_Link_Cycle_When_Links_Become_Copies(string firstLink, string firstTarget, string secondLink, string secondTarget)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Links only become copies on Windows.");
            return;
        }

        var exception = Assert.Throws<CakeException>(() => Extract(
            ArchiveFormat.TarGz,
            ArchiveEntrySpec.Symlink(firstLink, firstTarget),
            ArchiveEntrySpec.Symlink(secondLink, secondTarget)));

        Assert.Contains("is part of a link cycle", exception.Message);
    }

    [Fact]
    public void Extract_Skips_And_Logs_A_Symlink_To_A_Missing_Target_When_Links_Become_Copies()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Links only become copies on Windows.");
            return;
        }

        var target = Extract(ArchiveFormat.TarGz, ArchiveEntrySpec.Symlink("bin/tool", "missing"));

        Assert.False(File.Exists(Path.Combine(target, "bin", "tool")));
        var entry = Assert.Single(_log.Entries);
        Assert.Equal(Verbosity.Verbose, entry.Verbosity);
        Assert.Equal("Skipping archive entry 'bin/tool': its link target 'missing' does not exist in the archive.", entry.Message);
    }

    [Fact]
    public void Extract_Skips_And_Logs_Special_Entries()
    {
        var target = Extract(ArchiveFormat.TarGz, ArchiveEntrySpec.Fifo("pipe"));

        Assert.False(File.Exists(Path.Combine(target, "pipe")));
        var entry = Assert.Single(_log.Entries);
        Assert.Equal(Verbosity.Verbose, entry.Verbosity);
        Assert.Equal("Skipping archive entry 'pipe': Fifo entries are not extracted.", entry.Message);
    }

    [Fact]
    public void Extract_Materializes_Zip_Symlinks()
    {
        var target = Extract(
            ArchiveFormat.Zip,
            ArchiveEntrySpec.Symlink("bin/tool", "../libexec/tool"),
            new ArchiveEntrySpec("libexec/tool", "binary"));

        var tool = Path.Combine(target, "bin", "tool");
        Assert.Equal("binary", File.ReadAllText(tool));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal("../libexec/tool", new FileInfo(tool).LinkTarget);
        }
    }

    [Fact]
    public void Extract_Rejects_A_Zip_Symlink_Pointing_Outside_The_Destination()
    {
        var exception = Assert.Throws<CakeException>(
            () => Extract(ArchiveFormat.Zip, ArchiveEntrySpec.Symlink("bin/tool", "../../../outside")));

        Assert.Equal("Refusing to extract archive entry 'bin/tool': its link target '../../../outside' is outside the target folder.", exception.Message);
    }

    [Fact]
    public void Extract_Rejects_An_Oversized_Zip_Symlink()
    {
        var exception = Assert.Throws<CakeException>(
            () => Extract(ArchiveFormat.Zip, ArchiveEntrySpec.Symlink("bin/tool", new string('a', 4097))));

        Assert.Equal("Refusing to extract archive entry 'bin/tool': its link target is longer than 4096 bytes.", exception.Message);
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
    public void Extract_Rejects_A_Link_Whose_Own_Path_Passes_Through_A_Symlink()
    {
        var exception = Assert.Throws<CakeException>(() => Extract(
            ArchiveFormat.TarGz,
            ArchiveEntrySpec.Symlink("d1/d2/s", "../.."),
            ArchiveEntrySpec.Symlink("d1/d2/s/foo", "../")));

        Assert.Contains("'d1/d2/s/foo': its path passes through the link 'd1/d2/s'", exception.Message);
    }

    [Fact]
    public void Extract_Rejects_A_File_Whose_Path_Passes_Through_A_Symlink()
    {
        var exception = Assert.Throws<CakeException>(() => Extract(
            ArchiveFormat.TarGz,
            new ArchiveEntrySpec("s/f", "x"),
            ArchiveEntrySpec.Symlink("s", "bin"),
            new ArchiveEntrySpec("bin/x", "x")));

        Assert.Contains("'s/f': its path passes through the link 's'", exception.Message);
    }

    [Fact]
    public void Extract_Rejects_A_Hard_Link_To_A_Directory()
    {
        var exception = Assert.Throws<CakeException>(() => Extract(
            ArchiveFormat.TarGz,
            ArchiveEntrySpec.Directory("bin"),
            ArchiveEntrySpec.Hardlink("h", "bin")));

        Assert.Contains("'h': its link target 'bin' does not exist in the archive.", exception.Message);
    }

    [Fact]
    public void Extract_Compares_Link_Paths_Case_Insensitively()
    {
        if (OperatingSystem.IsLinux())
        {
            Assert.Skip("Case-insensitive file systems only.");
            return;
        }

        var exception = Assert.Throws<CakeException>(() => Extract(
            ArchiveFormat.TarGz,
            ArchiveEntrySpec.Symlink("d1/d2/s", "../.."),
            ArchiveEntrySpec.Symlink("d1/d2/S/foo", "../")));

        Assert.Contains("'d1/d2/S/foo': its path passes through the link 'd1/d2/s'", exception.Message);
    }

    [Fact]
    public void Extract_Compares_Link_Paths_Unicode_Normalized()
    {
        var exception = Assert.Throws<CakeException>(() => Extract(
            ArchiveFormat.TarGz,
            new ArchiveEntrySpec("bin/x", "x"),
            ArchiveEntrySpec.Symlink("café", "bin"),
            new ArchiveEntrySpec("café/f", "x")));

        Assert.Contains("passes through the link", exception.Message);
    }

    [Fact]
    public void Extract_Rejects_Raw_Files()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ArchiveExtractor.Extract(_directory.Combine("x"), ArchiveFormat.File, _directory.Combine("out"), _log));
    }

    private string Extract(ArchiveFormat format, params ArchiveEntrySpec[] entries)
    {
        var archive = _directory.Combine("archive");
        File.WriteAllBytes(archive, format == ArchiveFormat.Zip ? TestArchives.Zip(entries) : TestArchives.Tar(format == ArchiveFormat.TarGz, entries));
        var target = _directory.Combine("sandbox", "content");
        ArchiveExtractor.Extract(archive, format, target, _log);
        return target;
    }
}
