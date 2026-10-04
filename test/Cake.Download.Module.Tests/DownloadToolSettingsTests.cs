using Cake.Core;
using Cake.Core.Packaging;
using Cake.Download.Module.Directives;

namespace Cake.Download.Module.Tests;

public sealed class DownloadToolSettingsTests
{
    private static readonly string HexA = new('a', 64);
    private static readonly string HexB = new('b', 64);

    [Fact]
    public void ToDirective_Writes_The_Url_As_The_Leading_Template()
    {
        var settings = new DownloadToolSettings()
            .WithPackage("jq").WithVersion("1.8.2").WithUrl("https://example.com/jq-{os}-{arch}{exe}");

        Assert.Equal("download:https://example.com/jq-{os}-{arch}{exe}?package=jq&version=1.8.2", settings.ToDirective());
    }

    [Fact]
    public void ToDirective_Writes_Every_Parameter_In_A_Fixed_Order()
    {
        var settings = new DownloadToolSettings()
            .WithSha256("win-x64", HexB)
            .WithSha256("linux-x64", HexA)
            .WithExclude("**/doc/**")
            .WithInclude("**/rg{exe}")
            .WithFileName("rg")
            .WithFormat(DownloadFormat.TarGz)
            .WithTriple("linux-x64", "x86_64-unknown-linux-gnu")
            .WithArchive("darwin", "zip")
            .WithArch("x86_64", "x64")
            .WithOs("darwin", "macos")
            .WithDialect(DownloadDialect.Rust)
            .WithUrl("win-x64", "https://e.com/win.zip")
            .WithUrl("https://e.com/rg-{triple}.{archive}")
            .WithVersion("14.1.1")
            .WithPackage("rg");

        Assert.Equal(
            "download:https://e.com/rg-{triple}.{archive}?package=rg&version=14.1.1&url.win-x64=https%3A%2F%2Fe.com%2Fwin.zip" +
            "&dialect=rust&os.darwin=macos&arch.x86_64=x64&archive.darwin=zip&triple.linux-x64=x86_64-unknown-linux-gnu" +
            "&format=tar.gz&filename=rg&include=%2A%2A%2Frg%7Bexe%7D&exclude=%2A%2A%2Fdoc%2F%2A%2A" +
            $"&sha256.linux-x64={HexA}&sha256.win-x64={HexB}",
            settings.ToDirective());
    }

    [Fact]
    public void ToDirective_Round_Trips_Through_The_Parser()
    {
        var settings = new DownloadToolSettings()
            .WithPackage("rg").WithVersion("14.1.1").WithUrl("https://e.com/rg-{triple}.{archive}")
            .WithUrl("win-x64", "https://e.com/win.zip").WithDialect(DownloadDialect.Rust)
            .WithOs("darwin", "macos").WithArch("x86_64", "x64").WithArchive("darwin", "zip")
            .WithTriple("linux-x64", "x86_64-unknown-linux-gnu").WithFormat(DownloadFormat.TarGz)
            .WithInclude("**/rg{exe}").WithInclude("**/rg-extra").WithExclude("**/doc/**")
            .WithSha256("linux-x64", HexA);

        var directive = Parse(settings);

        Assert.Equal("rg", directive.Package);
        Assert.Equal("14.1.1", directive.Version);
        Assert.Equal("https://e.com/rg-{triple}.{archive}", directive.UrlTemplate);
        Assert.Null(directive.Url);
        Assert.Equal("https://e.com/win.zip", directive.UrlByRid["win-x64"]);
        Assert.Equal("rust", directive.Dialect);
        Assert.Equal("macos", directive.OsOverrides["darwin"]);
        Assert.Equal("x64", directive.ArchOverrides["x86_64"]);
        Assert.Equal("zip", directive.ArchiveOverrides["darwin"]);
        Assert.Equal("x86_64-unknown-linux-gnu", directive.TripleOverrides["linux-x64"]);
        Assert.Equal("tar.gz", directive.Format);
        Assert.Equal(["**/rg{exe}", "**/rg-extra"], directive.Include);
        Assert.Equal(["**/doc/**"], directive.Exclude);
        Assert.Equal(HexA, Assert.IsType<PerRidSha256Integrity>(directive.Integrity).Sha256ByRid["linux-x64"]);
    }

    [Theory]
    [InlineData("https://example.com/dl?file=jq-{os}")]
    [InlineData("https://example.com/jq#{os}")]
    [InlineData("https://example.com/a&b-{os}")]
    public void ToDirective_Moves_A_Url_With_Query_Characters_To_The_Url_Parameter(string url)
    {
        var settings = new DownloadToolSettings().WithPackage("jq").WithVersion("1.8.2").WithUrl(url).WithSha256(HexA);

        var directive = Parse(settings);

        Assert.StartsWith("download:?package=jq&version=1.8.2&url=", settings.ToDirective(), StringComparison.Ordinal);
        Assert.Null(directive.UrlTemplate);
        Assert.Equal(url, directive.Url);
    }

    [Theory]
    [InlineData("https://example.com/my%20tool-{version}.zip")]
    [InlineData("https://example.com/dl?name=my%20tool")]
    public void ToDirective_Keeps_Percent_Sequences_In_The_Url(string url)
    {
        var directive = Parse(new DownloadToolSettings().WithPackage("jq").WithVersion("1.8.2").WithUrl(url).WithSha256(HexA));

        Assert.Equal(url, directive.UrlTemplate ?? directive.Url);
    }

    [Fact]
    public void ToDirective_Encodes_Special_Characters_In_Values()
    {
        var directive = Parse(new DownloadToolSettings()
            .WithPackage("jq").WithVersion("1.8.2").WithUrl("https://example.com/jq").WithSha256(HexA)
            .WithInclude("a&b=c?d#e"));

        Assert.Equal(["a&b=c?d#e"], directive.Include);
    }

    [Fact]
    public void ToDirective_Matches_Mixed_Case_Rid_Keys()
    {
        var directive = Parse(new DownloadToolSettings()
            .WithPackage("jq").WithVersion("1.8.2").WithUrl("https://example.com/jq-{rid}")
            .WithSha256("Linux-X64", HexA));

        Assert.Equal(HexA, Assert.IsType<PerRidSha256Integrity>(directive.Integrity).Sha256ByRid["linux-x64"]);
    }

    [Theory]
    [InlineData(DownloadDialect.Go, "dialect=go")]
    [InlineData(DownloadDialect.DotNet, "dialect=dotnet")]
    [InlineData(DownloadDialect.Rust, "dialect=rust")]
    public void ToDirective_Writes_Dialect_Names(DownloadDialect dialect, string expected)
    {
        Assert.Contains("&" + expected, Minimal().WithDialect(dialect).ToDirective(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DownloadFormat.File, "format=file")]
    [InlineData(DownloadFormat.Zip, "format=zip")]
    [InlineData(DownloadFormat.Tar, "format=tar")]
    [InlineData(DownloadFormat.TarGz, "format=tar.gz")]
    public void ToDirective_Writes_Format_Names(DownloadFormat format, string expected)
    {
        Assert.Contains("&" + expected, Minimal().WithFormat(format).ToDirective(), StringComparison.Ordinal);
    }

    [Fact]
    public void ToDirective_Writes_Each_Integrity_Mode()
    {
        Assert.IsType<Sha256Integrity>(Parse(Minimal().WithSha256(HexA)).Integrity);
        Assert.IsType<SkipIntegrity>(Parse(Minimal().WithoutVerification()).Integrity);
        var checksums = Assert.IsType<ChecksumsFileIntegrity>(Parse(Minimal().WithChecksums("sha256sum.txt", HexA)).Integrity);
        Assert.Equal("sha256sum.txt", checksums.Reference);
        Assert.Equal(HexA, checksums.Sha256);
        Assert.IsType<MissingIntegrity>(Parse(Minimal()).Integrity);
    }

    [Fact]
    public void ToDirective_Lets_The_Parser_Reject_Conflicting_Integrity_Options()
    {
        var exception = Assert.Throws<CakeException>(() => Parse(Minimal().WithSha256(HexA).WithSha256("linux-x64", HexB)));

        Assert.Contains("specify only one integrity option", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToDirective_Rejects_Skip_Together_With_A_Hash()
    {
        var exception = Assert.Throws<CakeException>(() => Minimal().WithSha256(HexA).WithoutVerification().ToDirective());

        Assert.Equal(
            "DownloadToolSettings for 'jq': specify only one integrity option: WithSha256(…), WithSha256(rid, …), WithChecksums(…) or WithoutVerification().",
            exception.Message);
    }

    [Fact]
    public void ToDirective_Allows_Per_Platform_Urls_Only()
    {
        var directive = Parse(new DownloadToolSettings()
            .WithPackage("jq").WithVersion("1.8.2").WithUrl("linux-x64", "https://example.com/jq").WithSha256(HexA));

        Assert.Null(directive.UrlTemplate);
        Assert.Null(directive.Url);
        Assert.Equal("https://example.com/jq", directive.UrlByRid["linux-x64"]);
    }

    [Theory]
    [InlineData(null, "1.8.2", "https://example.com/jq", "DownloadToolSettings needs a package: use WithPackage(…) or DownloadTool(package, version, url, settings).")]
    [InlineData("", "1.8.2", "https://example.com/jq", "DownloadToolSettings needs a package: use WithPackage(…) or DownloadTool(package, version, url, settings).")]
    [InlineData("jq", null, "https://example.com/jq", "DownloadToolSettings for 'jq' needs a version: use WithVersion(…) or DownloadTool(package, version, url, settings).")]
    [InlineData("jq", "", "https://example.com/jq", "DownloadToolSettings for 'jq' needs a version: use WithVersion(…) or DownloadTool(package, version, url, settings).")]
    [InlineData("jq", "1.8.2", null, "DownloadToolSettings for 'jq' needs a URL: use WithUrl(…), WithUrl(rid, …) or DownloadTool(package, version, url, settings).")]
    [InlineData("jq", "1.8.2", "", "DownloadToolSettings for 'jq' needs a URL: use WithUrl(…), WithUrl(rid, …) or DownloadTool(package, version, url, settings).")]
    public void ToDirective_Rejects_A_Missing_Identity(string? package, string? version, string? url, string message)
    {
        var settings = new DownloadToolSettings { Package = package, Version = version, Url = url };

        Assert.Equal(message, Assert.Throws<CakeException>(() => settings.ToDirective()).Message);
    }

    [Fact]
    public void ToDirectiveUri_Keeps_The_Directive_As_Its_Original_String()
    {
        var settings = Minimal().WithSha256(HexA);

        Assert.Equal(settings.ToDirective(), settings.ToDirectiveUri().OriginalString);
    }

    [Fact]
    public void Fluent_Methods_Return_The_Same_Instance_And_Append_Globs()
    {
        var settings = new DownloadToolSettings();

        Assert.Same(settings, settings.WithInclude("a").WithInclude("b").WithExclude("c"));
        Assert.Equal(["a", "b"], settings.Include);
        Assert.Equal(["c"], settings.Exclude);
    }

    [Fact]
    public void Platform_Keys_Are_Case_Insensitive()
    {
        var settings = new DownloadToolSettings().WithSha256("LINUX-X64", HexA).WithSha256("linux-x64", HexB);

        Assert.Equal(HexB, Assert.Single(settings.Sha256ByPlatform).Value);
    }

    [Fact]
    public void Clone_Copies_Every_Property_Independently()
    {
        var original = Minimal().WithSha256("linux-x64", HexA).WithInclude("a").WithDialect(DownloadDialect.DotNet);

        var clone = original.Clone();
        Assert.Equal(original.ToDirective(), clone.ToDirective());

        clone.WithInclude("b").WithSha256("win-x64", HexB);

        Assert.Equal(["a"], original.Include);
        Assert.Single(original.Sha256ByPlatform);
        Assert.Equal(["a", "b"], clone.Include);
        Assert.Equal(DownloadDialect.DotNet, clone.Dialect);
        Assert.Equal("jq", clone.Package);
    }

    private static DownloadToolSettings Minimal() =>
        new DownloadToolSettings().WithPackage("jq").WithVersion("1.8.2").WithUrl("https://example.com/jq-{os}");

    private static DownloadDirective Parse(DownloadToolSettings settings) =>
        DirectiveParser.Parse(new PackageReference(settings.ToDirective()), DirectiveSource.Settings);
}
