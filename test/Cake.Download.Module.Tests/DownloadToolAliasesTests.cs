using Cake.Core;
using Cake.Core.IO;
using Cake.Core.Packaging;
using Cake.Download.Module.Directives;
using Cake.Download.Module.Http;
using Cake.Download.Module.Platforms;
using Cake.Download.Module.Tests.Fakes;
using Cake.Testing;
using Path = System.IO.Path;

namespace Cake.Download.Module.Tests;

public sealed class DownloadToolAliasesTests : IDisposable
{
    private const string RawContent = "#!/bin/sh\necho jq\n";
    private const string JqUrl = "https://example.com/jq-linux-amd64";

    private static readonly string JqDirective =
        "download:https://example.com/jq-{os}-{arch}?package=jq&version=1.8.2&sha256.linux-x64=" + TestHashes.Sha256(RawContent);

    private readonly TestDirectory _directory = new();
    private readonly FakeHttpHandler _handler = new();
    private readonly FakeLog _log = new();
    private readonly FakeConfiguration _configuration = new();
    private readonly TestCakeContext _context;

    public DownloadToolAliasesTests()
    {
        var environment = FakeEnvironment.CreateUnixEnvironment();
        environment.WorkingDirectory = new DirectoryPath(_directory.Root);
        _context = new TestCakeContext(environment, _log, _configuration);
        _handler.Respond(JqUrl, FakeHttpHandler.Ok(RawContent));
    }

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void Install_Registers_And_Returns_The_Files_In_The_Default_Tools_Folder()
    {
        var path = Assert.Single(CreateRunner().Install(JqDirective));

        Assert.Equal(_directory.Combine("tools", "jq.1.8.2", "jq"), Normalize(path));
        Assert.Equal([path.FullPath], _context.Tools.Registered.Select(registered => registered.FullPath));
        Assert.Equal(RawContent, File.ReadAllText(path.FullPath));
    }

    [Fact]
    public void Install_Uses_An_Absolute_Paths_Tools()
    {
        _configuration.SetValue("Paths_Tools", _directory.Combine("custom-tools"));

        var path = Assert.Single(CreateRunner().Install(JqDirective));

        Assert.Equal(_directory.Combine("custom-tools", "jq.1.8.2", "jq"), Normalize(path));
    }

    [Fact]
    public void Install_Resolves_A_Relative_Paths_Tools_Against_The_Working_Directory()
    {
        _configuration.SetValue("Paths_Tools", "./build-tools");

        var path = Assert.Single(CreateRunner().Install(JqDirective));

        Assert.Equal(_directory.Combine("build-tools", "jq.1.8.2", "jq"), Normalize(path));
    }

    // Cake paths use '/' and may keep a "./" segment; compare as normalized OS paths.
    private static string Normalize(FilePath path) => Path.GetFullPath(path.FullPath);

    [Fact]
    public void Install_Twice_Makes_No_Second_Request_And_Registers_Again()
    {
        CreateRunner().Install(JqDirective);
        var requests = _handler.RequestedUrls.Count;

        CreateRunner().Install(JqDirective);

        Assert.Equal(requests, _handler.RequestedUrls.Count);
        Assert.Equal(2, _context.Tools.Registered.Count);
    }

    [Fact]
    public void DownloadTool_Rejects_A_Value_That_Is_Not_A_Download_Directive()
    {
        var exception = Assert.Throws<CakeException>(() => _context.DownloadTool("nuget:?package=jq&version=1.8.2"));

        Assert.Equal("Invalid download directive 'nuget:?package=jq&version=1.8.2': it must start with 'download:'.", exception.Message);
        Assert.Empty(_context.Tools.Registered);
    }

    [Fact]
    public void DownloadTool_With_A_Uri_Uses_The_Original_String()
    {
        var exception = Assert.Throws<CakeException>(
            () => _context.DownloadTool(new Uri("download:https://example.com/{version}/jq?version=1.8.2")));

        Assert.StartsWith(
            "Invalid download directive 'download:https://example.com/{version}/jq?version=1.8.2':",
            exception.Message,
            StringComparison.Ordinal);
        Assert.IsAssignableFrom<ArgumentException>(exception.InnerException);
    }

    [Theory]
    [InlineData("foo")]
    [InlineData("nuget:?version=1")]
    public void DownloadTool_Names_The_Expected_Scheme_Before_Anything_Else(string value)
    {
        var exception = Assert.Throws<CakeException>(() => _context.DownloadTool(value));

        Assert.Equal($"Invalid download directive '{value}': it must start with 'download:'.", exception.Message);
    }

    [Fact]
    public void DownloadTool_Rejects_Null_Arguments()
    {
        Assert.Throws<ArgumentNullException>(() => DownloadToolAliases.DownloadTool(null!, JqDirective));
        Assert.Throws<ArgumentNullException>(() => _context.DownloadTool((string)null!));
        Assert.Throws<ArgumentNullException>(() => _context.DownloadTool((Uri)null!));
    }

    private static DownloadToolSettings JqSettings() =>
        new DownloadToolSettings().WithSha256("linux-x64", TestHashes.Sha256(RawContent));

    [Fact]
    public void Install_With_Identity_Arguments_Registers_And_Returns_The_Files()
    {
        var path = Assert.Single(CreateRunner().Install("jq", "1.8.2", "https://example.com/jq-{os}-{arch}", JqSettings()));

        Assert.EndsWith("/tools/jq.1.8.2/jq", path.FullPath, StringComparison.Ordinal);
        Assert.Equal([path.FullPath], _context.Tools.Registered.Select(registered => registered.FullPath));
    }

    [Fact]
    public void Install_With_Settings_Only_Uses_The_Identity_From_The_Settings()
    {
        var settings = JqSettings().WithPackage("jq").WithVersion("1.8.2").WithUrl("https://example.com/jq-{os}-{arch}");

        var path = Assert.Single(CreateRunner().Install(settings));

        Assert.EndsWith("/tools/jq.1.8.2/jq", path.FullPath, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_And_The_Equivalent_Directive_Share_One_Install()
    {
        CreateRunner().Install("jq", "1.8.2", "https://example.com/jq-{os}-{arch}", JqSettings());
        var requests = _handler.RequestedUrls.Count;

        CreateRunner().Install(JqDirective);

        Assert.Equal(requests, _handler.RequestedUrls.Count);
    }

    [Fact]
    public void One_Settings_Instance_Can_Be_Reused_For_Several_Tools()
    {
        _handler.Respond("https://example.com/yq-linux-amd64", FakeHttpHandler.Ok(RawContent));
        var settings = new DownloadToolSettings().WithSha256(TestHashes.Sha256(RawContent));

        CreateRunner().Install("jq", "1.8.2", "https://example.com/jq-linux-amd64", settings);
        var yq = Assert.Single(CreateRunner().Install("yq", "4.0.0", "https://example.com/yq-linux-amd64", settings));

        Assert.EndsWith("/tools/yq.4.0.0/yq", yq.FullPath, StringComparison.Ordinal);
        Assert.Null(settings.Package);
    }

    [Fact]
    public void Install_With_Settings_Without_Integrity_Suggests_A_Settings_Call()
    {
        var exception = Assert.Throws<CakeException>(
            () => CreateRunner().Install("jq", "1.8.2", "https://example.com/jq-{os}-{arch}", new DownloadToolSettings()));

        Assert.Contains($"Add .WithSha256(\"linux-x64\", \"{TestHashes.Sha256(RawContent)}\") to the DownloadToolSettings", exception.Message);
        Assert.Empty(_context.Tools.Registered);
    }

    [Fact]
    public void DownloadTool_With_Settings_Rejects_Null_Arguments()
    {
        var settings = new DownloadToolSettings();
        Assert.Throws<ArgumentNullException>(() => _context.DownloadTool((DownloadToolSettings)null!));
        Assert.Throws<ArgumentNullException>(() => DownloadToolAliases.DownloadTool(null!, settings));
        Assert.Throws<ArgumentNullException>(() => _context.DownloadTool(null!, "1.8.2", "https://example.com/jq", settings));
        Assert.Throws<ArgumentNullException>(() => _context.DownloadTool("jq", null!, "https://example.com/jq", settings));
        Assert.Throws<ArgumentNullException>(() => _context.DownloadTool("jq", "1.8.2", null!, settings));
        Assert.Throws<ArgumentNullException>(() => _context.DownloadTool("jq", "1.8.2", "https://example.com/jq", null!));
    }

    [Fact]
    public void DownloadTool_With_Incomplete_Settings_Fails_Before_Any_Request()
    {
        var exception = Assert.Throws<CakeException>(() => _context.DownloadTool(new DownloadToolSettings().WithPackage("jq")));

        Assert.StartsWith("DownloadToolSettings for 'jq' needs a version", exception.Message, StringComparison.Ordinal);
        Assert.Empty(_handler.RequestedUrls);
    }

    private DownloadToolRunner CreateRunner() => new(_context, new DownloadPackageInstaller(
        _context.Environment,
        _context.FileSystem,
        _log,
        new FixedPlatformDetector(PlatformInfo.FromRid("linux-x64")),
        _handler,
        new DownloadOptions { Delay = (_, _) => Task.CompletedTask },
        TimeProvider.System));
}
