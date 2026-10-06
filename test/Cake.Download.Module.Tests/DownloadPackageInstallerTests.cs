using System.Net;
using Cake.Core;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using Cake.Core.Packaging;
using Cake.Download.Module.Directives;
using Cake.Download.Module.Http;
using Cake.Download.Module.Platforms;
using Cake.Download.Module.Tests.Fakes;
using Cake.Testing;
using Path = System.IO.Path;

namespace Cake.Download.Module.Tests;

public sealed class DownloadPackageInstallerTests : IDisposable
{
    private const string RawContent = "#!/bin/sh\necho jq\n";
    private const string JqUrl = "https://example.com/jq-linux-amd64";
    private const string JqDirective = "download:https://example.com/jq-{os}-{arch}?package=jq&version=1.8.2";

    private readonly TestDirectory _directory = new();
    private readonly FakeHttpHandler _handler = new();
    private readonly FakeLog _log = new();
    private readonly string _tools;

    public DownloadPackageInstallerTests()
    {
        _tools = _directory.Combine("tools");
        _handler.Respond(JqUrl, FakeHttpHandler.Ok(RawContent));
    }

    public void Dispose() => _directory.Dispose();

    [Theory]
    [InlineData("download:https://example.com/a?package=a", PackageType.Tool, true)]
    [InlineData("DOWNLOAD:https://example.com/a?package=a", PackageType.Tool, true)]
    [InlineData("download:https://example.com/a?package=a", PackageType.Addin, false)]
    [InlineData("download:https://example.com/a?package=a", PackageType.Module, false)]
    [InlineData("nuget:?package=a", PackageType.Tool, false)]
    public void CanInstall_Accepts_Only_Download_Tools(string uri, PackageType type, bool expected)
    {
        Assert.Equal(expected, CreateInstaller().CanInstall(new PackageReference(uri), type));
    }

    [Fact]
    public void Install_Registers_A_Raw_Download_Under_The_Package_Name()
    {
        var file = Assert.Single(Install(JqDirective + "&sha256=" + TestHashes.Sha256(RawContent)));

        Assert.EndsWith("/tools/jq.1.8.2/jq", file.Path.FullPath, StringComparison.Ordinal);
        Assert.Equal(RawContent, File.ReadAllText(file.Path.FullPath));
        Assert.True(File.Exists(Path.Combine(_tools, "jq.1.8.2", ".cake-download.json")));
        Assert.Contains(_log.Entries, entry => entry.Level == LogLevel.Information && entry.Message == $"Downloading jq 1.8.2 (linux-x64) from {JqUrl}");
    }

    [Fact]
    public void Install_Uses_The_Exe_Name_On_Windows()
    {
        _handler.Respond("https://example.com/jq-windows-amd64.exe", FakeHttpHandler.Ok(RawContent));

        var file = Assert.Single(Install("download:https://example.com/jq-{os}-{arch}{exe}?package=jq&version=1.8.2&sha256=" + TestHashes.Sha256(RawContent), "win-x64"));

        Assert.EndsWith("/jq.1.8.2/jq.exe", file.Path.FullPath, StringComparison.Ordinal);
    }

    [Fact]
    public void Install_Wraps_An_Extraction_Failure_In_A_CakeException()
    {
        const string NotAnArchive = "this is not a zip file";
        _handler.Respond("https://example.com/bad.zip", FakeHttpHandler.Ok(NotAnArchive));

        var exception = Assert.Throws<CakeException>(
            () => Install("download:https://example.com/bad.zip?package=jq&version=1.8.2&sha256=" + TestHashes.Sha256(NotAnArchive)));

        Assert.StartsWith("Could not extract jq 1.8.2 from https://example.com/bad.zip as zip:", exception.Message);
        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    public void Install_Extracts_An_Archive_And_Registers_The_Default_Include()
    {
        var archive = TestArchives.Tar(
            gzip: true,
            new ArchiveEntrySpec("tool-1.0/bin/tool", "binary"),
            new ArchiveEntrySpec("tool-1.0/README.md", "docs"));
        _handler.Respond("https://example.com/tool-1.0.tar.gz", FakeHttpHandler.Ok(archive));

        var file = Assert.Single(Install("download:https://example.com/tool-{version}.tar.gz?package=tool&version=1.0&sha256=" + TestHashes.Sha256(archive)));

        Assert.EndsWith("/tools/tool.1.0/tool-1.0/bin/tool", file.Path.FullPath, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(_tools, "tool.1.0", "tool-1.0", "README.md")));
    }

    [Fact]
    public void Install_Twice_Makes_No_Second_Request()
    {
        var directive = JqDirective + "&sha256=" + TestHashes.Sha256(RawContent);
        Install(directive);
        var requests = _handler.RequestedUrls.Count;

        var file = Assert.Single(Install(directive));

        Assert.Equal(requests, _handler.RequestedUrls.Count);
        Assert.True(File.Exists(file.Path.FullPath));
        Assert.Contains(_log.Entries, entry => entry.Message.StartsWith("jq 1.8.2 (linux-x64) is already installed in ", StringComparison.Ordinal));
    }

    [Fact]
    public void Install_Reinstalls_When_The_Integrity_Option_Changes()
    {
        Install(JqDirective + "&sha256=skip");
        Install(JqDirective + "&sha256=" + TestHashes.Sha256(RawContent));

        Assert.Equal(2, _handler.RequestedUrls.Count);
    }

    [Fact]
    public void Install_Reinstalls_When_The_Registered_File_Was_Deleted()
    {
        var directive = JqDirective + "&sha256=" + TestHashes.Sha256(RawContent);
        var path = Assert.Single(Install(directive)).Path.FullPath;
        File.Delete(path);

        Install(directive);

        Assert.Equal(2, _handler.RequestedUrls.Count);
        Assert.Equal(RawContent, File.ReadAllText(path));
    }

    [Fact]
    public void Install_Works_With_Spaces_In_The_Tools_Path()
    {
        var tools = _directory.Combine("My Tools");

        var file = Assert.Single(Install(JqDirective + "&sha256=" + TestHashes.Sha256(RawContent), tools: tools));

        Assert.EndsWith("/My Tools/jq.1.8.2/jq", file.Path.FullPath, StringComparison.Ordinal);
    }

    [Fact]
    public void Install_Rejects_A_Hash_Mismatch_And_Leaves_Nothing_Behind()
    {
        var exception = Assert.Throws<CakeException>(() => Install(JqDirective + "&sha256=" + new string('f', 64)));

        Assert.StartsWith($"SHA-256 mismatch for {JqUrl}.", exception.Message);
        Assert.Empty(Directory.GetFileSystemEntries(_tools));
    }

    [Theory]
    [InlineData(JqDirective, "sha256.linux-x64")]
    [InlineData("download:https://example.com/jq-linux-amd64?package=jq&version=1.8.2", "sha256")]
    public void Install_Without_Integrity_Fails_With_The_Parameter_To_Paste(string directive, string parameter)
    {
        var exception = Assert.Throws<CakeException>(() => Install(directive));

        Assert.Contains($"Add '&{parameter}={TestHashes.Sha256(RawContent)}' to the directive", exception.Message);
        Assert.False(Directory.Exists(Path.Combine(_tools, "jq.1.8.2")));
    }

    [Fact]
    public void Install_With_Skip_Logs_A_Warning()
    {
        Install(JqDirective + "&sha256=skip");

        Assert.Contains(_log.Entries, entry => entry.Level == LogLevel.Warning
            && entry.Message == $"Integrity verification is disabled for jq 1.8.2 (sha256=skip). The downloaded file has SHA-256 {TestHashes.Sha256(RawContent)}.");
    }

    [Fact]
    public void Install_Explains_A_404_With_The_Expanded_Placeholders()
    {
        var exception = Assert.Throws<CakeException>(() => Install(JqDirective + "&sha256=skip", "osx-arm64"));

        Assert.StartsWith("https://example.com/jq-darwin-arm64 was not found (HTTP 404). Detected platform osx-arm64; dialect 'go' expanded {os}='darwin', {arch}='arm64', {rid}='osx-arm64', {exe}='', {archive}='tar.gz'.", exception.Message);
        Assert.Contains("'url.osx-arm64='", exception.Message);
    }

    [Fact]
    public void Install_Wraps_A_Checksums_File_404_In_A_CakeException()
    {
        var directive = JqDirective + "&checksums=SHA256SUMS&checksums_sha256=" + new string('a', 64);

        var exception = Assert.Throws<CakeException>(() => Install(directive));

        Assert.StartsWith("Downloading https://example.com/SHA256SUMS failed: HTTP 404", exception.Message);
        Assert.IsType<DownloadHttpException>(exception.InnerException);
    }

    [Fact]
    public async Task Concurrent_Installs_Both_Succeed_With_One_Install_Folder()
    {
        var directive = JqDirective + "&sha256=" + TestHashes.Sha256(RawContent);

        var results = await Task.WhenAll(
            Task.Run(() => Install(directive, log: new FakeLog())),
            Task.Run(() => Install(directive, log: new FakeLog())));

        Assert.All(results, files => Assert.True(File.Exists(Assert.Single(files).Path.FullPath)));
        Assert.Equal([Path.Combine(_tools, "jq.1.8.2")], Directory.GetDirectories(_tools));
    }

    [Theory]
    [InlineData(JqDirective, ".WithSha256(\"linux-x64\", \"{0}\")")]
    [InlineData("download:https://example.com/jq-linux-amd64?package=jq&version=1.8.2", ".WithSha256(\"{0}\")")]
    public void Install_From_Settings_Without_Integrity_Suggests_A_Settings_Call(string directive, string call)
    {
        var exception = Assert.Throws<CakeException>(() => Install(directive, source: DirectiveSource.Settings));

        Assert.StartsWith("The DownloadToolSettings for 'jq' have no integrity check.", exception.Message);
        Assert.Contains($"Add {string.Format(call, TestHashes.Sha256(RawContent))} to the DownloadToolSettings, or .WithoutVerification()", exception.Message);
        Assert.DoesNotContain("&sha256", exception.Message);
    }

    [Fact]
    public void Install_From_Settings_With_An_Unpinned_Checksums_File_Suggests_WithChecksums()
    {
        const string Sums = "0000000000000000000000000000000000000000000000000000000000000000  jq-linux-amd64\n";
        _handler.Respond("https://example.com/SHA256SUMS", FakeHttpHandler.Ok(Sums));

        var exception = Assert.Throws<CakeException>(() => Install(JqDirective + "&checksums=SHA256SUMS", source: DirectiveSource.Settings));

        Assert.Contains($"Use .WithChecksums(\"SHA256SUMS\", \"{TestHashes.Sha256(Sums)}\") in the DownloadToolSettings for 'jq'.", exception.Message);
    }

    [Fact]
    public void Install_From_Settings_Keeps_The_Checksums_Reference_Unexpanded_In_The_Hint()
    {
        const string Sums = "0000000000000000000000000000000000000000000000000000000000000000  jq-linux-amd64\n";
        _handler.Respond("https://example.com/jq_1.8.2_checksums.txt", FakeHttpHandler.Ok(Sums));

        var exception = Assert.Throws<CakeException>(() => Install(JqDirective + "&checksums=jq_{version}_checksums.txt", source: DirectiveSource.Settings));

        Assert.Contains($"Use .WithChecksums(\"jq_{{version}}_checksums.txt\", \"{TestHashes.Sha256(Sums)}\") in the DownloadToolSettings for 'jq'.", exception.Message);
    }

    [Fact]
    public void Install_From_A_Directive_With_An_Unpinned_Checksums_File_Suggests_The_Parameter()
    {
        const string Sums = "0000000000000000000000000000000000000000000000000000000000000000  jq-linux-amd64\n";
        _handler.Respond("https://example.com/SHA256SUMS", FakeHttpHandler.Ok(Sums));

        var exception = Assert.Throws<CakeException>(() => Install(JqDirective + "&checksums=SHA256SUMS"));

        Assert.Contains($"Add '&checksums_sha256={TestHashes.Sha256(Sums)}' to the directive for 'jq'.", exception.Message);
    }

    [Fact]
    public void Install_From_Settings_Explains_A_404_With_Settings_Overrides()
    {
        var exception = Assert.Throws<CakeException>(() => Install(JqDirective + "&sha256=skip", "osx-arm64", source: DirectiveSource.Settings));

        Assert.StartsWith("https://example.com/jq-darwin-arm64 was not found (HTTP 404).", exception.Message);
        Assert.Contains(".WithOs(…), .WithArch(…), .WithArchive(…) or .WithUrl(\"osx-arm64\", …) to the DownloadToolSettings.", exception.Message);
        Assert.DoesNotContain("'url.osx-arm64='", exception.Message);
    }

    private IReadOnlyCollection<IFile> Install(
        string directive,
        string rid = "linux-x64",
        string? tools = null,
        FakeLog? log = null,
        DirectiveSource source = DirectiveSource.Directive) =>
        CreateInstaller(rid, log).Install(new PackageReference(directive), PackageType.Tool, new DirectoryPath(tools ?? _tools), source);

    private DownloadPackageInstaller CreateInstaller(string rid = "linux-x64", FakeLog? log = null) => new(
        FakeEnvironment.CreateUnixEnvironment(),
        new FileSystem(),
        log ?? _log,
        new FixedPlatformDetector(PlatformInfo.FromRid(rid)),
        _handler,
        new DownloadOptions { Delay = (_, _) => Task.CompletedTask },
        TimeProvider.System);
}
