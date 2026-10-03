using Cake.Core;
using Cake.Core.Packaging;
using Cake.Download.Module.Directives;

namespace Cake.Download.Module.Tests.Directives;

public sealed class DirectiveParserTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string Base = "download:https://example.com/tool-{version}-{os}-{arch}{exe}?package=tool&version=1.2.3";

    [Fact]
    public void Parse_Reads_The_Template_From_The_Original_String_With_Placeholders_Intact()
    {
        var directive = Parse(Base + "&sha256=" + Hash);

        Assert.Equal("https://example.com/tool-{version}-{os}-{arch}{exe}", directive.UrlTemplate);
        Assert.Equal("tool", directive.Package);
        Assert.Equal("1.2.3", directive.Version);
        Assert.Equal("go", directive.Dialect);
        Assert.Null(directive.Url);
        Assert.Equal(new Sha256Integrity(Hash), directive.Integrity);
    }

    [Fact]
    public void Parse_Accepts_Upper_Case_Scheme_And_Parameter_Keys()
    {
        var directive = Parse("DOWNLOAD:https://example.com/tool.zip?Package=tool&Version=1.0&SHA256=" + Hash.ToUpperInvariant() + "&Dialect=DotNet");

        Assert.Equal("tool", directive.Package);
        Assert.Equal("dotnet", directive.Dialect);
        Assert.Equal(new Sha256Integrity(Hash), directive.Integrity);
    }

    [Fact]
    public void Parse_Preserves_Plus_In_The_Version()
    {
        Assert.Equal("1.2.3+build.5", Parse("download:https://example.com/t.zip?package=t&version=1.2.3+build.5&sha256=skip").Version);
    }

    [Theory]
    [InlineData("download:https://example.com/t.zip?package=t&sha256=skip", "the 'version' parameter is required.")]
    [InlineData("download:https://example.com/t.zip?package=t&version=latest&sha256=skip", "'version=latest' is not supported; pin an exact version.")]
    [InlineData("download:https://example.com/t.zip?package=.&version=1&sha256=skip", "'package' must start with a letter or digit and may only contain letters, digits, '.', '_' and '-' (was '.').")]
    [InlineData("download:https://example.com/t.zip?package=..&version=1&sha256=skip", "'package' must start with a letter or digit and may only contain letters, digits, '.', '_' and '-' (was '..').")]
    [InlineData("download:https://example.com/t.zip?package=t&version=.&sha256=skip", "'version' must start with a letter or digit and may only contain letters, digits, '.', '_', '+' and '-' (was '.').")]
    [InlineData("download:https://example.com/t.zip?package=t&version=-1&sha256=skip", "'version' must start with a letter or digit and may only contain letters, digits, '.', '_', '+' and '-' (was '-1').")]
    [InlineData("download:https://example.com/t.zip?package=t&version=1%2F2&sha256=skip", "'version' must start with a letter or digit and may only contain letters, digits, '.', '_', '+' and '-' (was '1/2').")]
    [InlineData("download:https://example.com/t.zip?package=a%2Fb&version=1&sha256=skip", "'package' must start with a letter or digit and may only contain letters, digits, '.', '_' and '-' (was 'a/b').")]
    [InlineData("download:https://example.com/t.zip?package=jq%0A&version=1&sha256=skip", "'package' must start with a letter or digit and may only contain letters, digits, '.', '_' and '-' (was 'jq\n').")]
    [InlineData("download:https://example.com/t.zip?package=t&version=1.0%0A&sha256=skip", "'version' must start with a letter or digit and may only contain letters, digits, '.', '_', '+' and '-' (was '1.0\n').")]
    [InlineData("download:https://example.com/t.zip?package=nul&version=1&sha256=skip", "'package' must not be a reserved Windows device name (was 'nul').")]
    [InlineData("download:https://example.com/t.zip?package=Aux&version=1&sha256=skip", "'package' must not be a reserved Windows device name (was 'Aux').")]
    [InlineData("download:https://example.com/t.zip?package=t&version=1&sha256=skip&token=x", "unknown parameter 'token'.")]
    [InlineData("download:https://example.com/t.zip?package=t&version=1&sha256=skip&dialect=go&dialect=rust", "parameter 'dialect' may only be specified once.")]
    [InlineData("download:https://example.com/t.zip?package=t&version=1&sha256=", "parameter 'sha256' needs a value.")]
    [InlineData("download:https://example.com/t.zip?package=t&version=1&sha256=skip&dialect=python", "unknown dialect 'python'. Supported dialects: go, dotnet, rust.")]
    [InlineData("download:https://example.com/t.zip?package=t&version=1&sha256=skip&url=https%3A%2F%2Fexample.com%2Fother.zip", "specify the download URL either after 'download:' or with 'url=', not both.")]
    [InlineData("download:?package=t&version=1&sha256=skip", "no download URL. Put the URL after 'download:' or use 'url=' or 'url.<rid>='.")]
    [InlineData("download:https://example.com/t?package=t&version=1&sha256=skip&format=7z", "unknown format '7z'. Supported formats: file, zip, tar, tar.gz.")]
    [InlineData("download:https://example.com/t?package=t&version=1&sha256=skip&filename=bin%2Ft", "'filename' must be a file name, not a path (was 'bin/t').")]
    [InlineData("download:https://example.com/t?package=t&version=1&sha256=skip&filename=C%3Aevil.exe", "'filename' must be a file name, not a path (was 'C:evil.exe').")]
    [InlineData("download:https://example.com/t?package=t&version=1&sha256=skip&filename=jq%3Astream", "'filename' must be a file name, not a path (was 'jq:stream').")]
    [InlineData("download:https://example.com/t?package=t&version=1&sha256=skip&triple.linux-x64=x", "'triple.<rid>' parameters require 'dialect=rust'.")]
    [InlineData("download:https://example.com/t?package=t&version=1&sha256=skip&url.freebsd-x64=https%3A%2F%2Fexample.com%2Ft", "'url.freebsd-x64' does not name a supported platform. Supported platforms: win-x64, win-x86, win-arm64, linux-x64, linux-arm64, linux-arm, osx-x64, osx-arm64.")]
    [InlineData("download:https://example.com/t?package=t&version=1&sha256=skip&os.macos=x", "'os.macos' is not a valid override for dialect 'go'. Use one of: os.windows, os.linux, os.darwin.")]
    [InlineData("download:https://example.com/t?package=t&version=1&sha256=skip&arch.x64=x", "'arch.x64' is not a valid override for dialect 'go'. Use one of: arch.amd64, arch.arm64, arch.386, arch.arm.")]
    [InlineData("download:https://example.com/t?package=t&version=1&sha256=skip&archive.osx=zip", "'archive.osx' is not a valid override for dialect 'go'. Use one of: archive.windows, archive.linux, archive.darwin.")]
    public void Parse_Rejects_Invalid_Directives(string uri, string problem)
    {
        var exception = Assert.Throws<CakeException>(() => Parse(uri));

        Assert.StartsWith($"Invalid download directive '{uri}': ", exception.Message);
        Assert.Contains(problem, exception.Message);
    }

    [Fact]
    public void Parse_Lists_The_Known_Parameters_For_An_Unknown_One()
    {
        var exception = Assert.Throws<CakeException>(() => Parse("download:https://example.com/t.zip?package=t&version=1&sha256=skip&token=x"));

        Assert.Contains("Known parameters: package, version, sha256, sha256.<rid>, checksums, checksums_sha256, dialect, os.<os>, arch.<arch>, archive.<os>, triple.<rid>, url, url.<rid>, format, filename, include, exclude.", exception.Message);
    }

    [Fact]
    public void Parse_Reads_Overrides_And_Rid_Maps()
    {
        var directive = Parse(
            "download:https://example.com/rg-{triple}.{archive}?package=rg&version=14.1.1&dialect=rust&sha256=skip" +
            "&os.darwin=macos&arch.x86_64=amd64&archive.windows=7z.zip&triple.LINUX-ARM64=aarch64-unknown-linux-gnu" +
            "&url.win-x64=https%3A%2F%2Fexample.com%2Fwin.zip");

        Assert.Equal("rust", directive.Dialect);
        Assert.Equal("macos", directive.OsOverrides["DARWIN"]);
        Assert.Equal("amd64", directive.ArchOverrides["x86_64"]);
        Assert.Equal("7z.zip", directive.ArchiveOverrides["windows"]);
        Assert.Equal("aarch64-unknown-linux-gnu", directive.TripleOverrides["linux-arm64"]);
        Assert.Equal("https://example.com/win.zip", directive.UrlByRid["win-x64"]);
    }

    [Fact]
    public void Parse_Decodes_A_Percent_Encoded_Url_Parameter()
    {
        var directive = Parse("download:?package=t&version=1&sha256=skip&url=https%3A%2F%2Fexample.com%2Fa.zip%3Fsig%3Dabc%26x%3D1");

        Assert.Null(directive.UrlTemplate);
        Assert.Equal("https://example.com/a.zip?sig=abc&x=1", directive.Url);
    }

    [Fact]
    public void Parse_Accepts_Only_Url_For_Rid_Without_A_Default_Url()
    {
        var directive = Parse("download:?package=t&version=1&sha256=skip&url.linux-x64=https%3A%2F%2Fexample.com%2Ft");

        Assert.Equal("https://example.com/t", directive.UrlByRid["linux-x64"]);
    }

    [Fact]
    public void Parse_Reads_Format_Filename_Include_And_Exclude()
    {
        var directive = Parse(Base + "&sha256=skip&format=TGZ&include=**%2Fbin%2F*&include=**%2Ftool&exclude=**%2F*.txt");

        Assert.Equal("tar.gz", directive.Format);
        Assert.Equal(["**/bin/*", "**/tool"], directive.Include);
        Assert.Equal(["**/*.txt"], directive.Exclude);

        Assert.Equal("tool.jar", Parse(Base + "&sha256=skip&filename=tool.jar").FileName);
    }

    [Fact]
    public void Parse_Accepts_A_Package_That_Only_Resembles_A_Reserved_Device_Name()
    {
        Assert.Equal("auxtool", Parse("download:https://example.com/t.zip?package=auxtool&version=1&sha256=skip").Package);
    }

    [Theory]
    [InlineData("NUL", true)]
    [InlineData("nul.txt", true)]
    [InlineData("con.tar.gz", true)]
    [InlineData("Com1.exe", true)]
    [InlineData("COM0", true)]
    [InlineData("lpt9", true)]
    [InlineData("COM\u00B9", true)]
    [InlineData("lpt\u00B2.txt", true)]
    [InlineData("console.exe", false)]
    [InlineData("com10", false)]
    [InlineData("auxtool", false)]
    [InlineData("nul\n", false)]
    public void IsReservedDeviceName_Matches_The_Stem_Before_The_First_Dot(string name, bool expected)
    {
        Assert.Equal(expected, DirectiveParser.IsReservedDeviceName(name));
    }

    [Fact]
    public void Parse_Reads_Skip()
    {
        Assert.IsType<SkipIntegrity>(Parse(Base + "&sha256=SKIP").Integrity);
    }

    [Fact]
    public void Parse_Reads_Per_Rid_Hashes_With_Lower_Case_Keys_And_Values()
    {
        var integrity = Assert.IsType<PerRidSha256Integrity>(
            Parse(Base + "&sha256.WIN-X64=" + Hash.ToUpperInvariant() + "&sha256.linux-x64=" + Hash).Integrity);

        Assert.Equal(Hash, integrity.Sha256ByRid["win-x64"]);
        Assert.Equal(Hash, integrity.Sha256ByRid["linux-x64"]);
    }

    [Fact]
    public void Parse_Reads_Pinned_And_Unpinned_Checksums_Files()
    {
        Assert.Equal(
            new ChecksumsFileIntegrity("sha256sum.txt", Hash),
            Parse(Base + "&checksums=sha256sum.txt&checksums_sha256=" + Hash).Integrity);
        Assert.Equal(
            new ChecksumsFileIntegrity("tool-{version}.sha256", null),
            Parse(Base + "&checksums=tool-%7Bversion%7D.sha256").Integrity);
    }

    [Fact]
    public void Parse_Reports_Missing_Integrity_As_A_Value_Not_An_Error()
    {
        Assert.IsType<MissingIntegrity>(Parse(Base).Integrity);
    }

    [Theory]
    [InlineData("&sha256=" + Hash + "&sha256.win-x64=" + Hash)]
    [InlineData("&sha256=skip&checksums=sums.txt&checksums_sha256=" + Hash)]
    [InlineData("&sha256.win-x64=" + Hash + "&checksums=sums.txt")]
    public void Parse_Rejects_More_Than_One_Integrity_Option(string query)
    {
        var exception = Assert.Throws<CakeException>(() => Parse(Base + query));

        Assert.Contains("specify only one integrity option: 'sha256=', 'sha256.<rid>=', 'checksums=' with 'checksums_sha256=', or 'sha256=skip'.", exception.Message);
    }

    [Theory]
    [InlineData("&checksums_sha256=" + Hash, "'checksums_sha256' requires 'checksums'.")]
    [InlineData("&sha256=abc", "'sha256' must be a SHA-256 hash of 64 hexadecimal characters, or 'skip'.")]
    [InlineData("&sha256.win-x64=abc", "'sha256.win-x64' must be a SHA-256 hash of 64 hexadecimal characters.")]
    [InlineData("&checksums=s.txt&checksums_sha256=xyz", "'checksums_sha256' must be a SHA-256 hash of 64 hexadecimal characters.")]
    public void Parse_Rejects_Invalid_Integrity_Values(string query, string problem)
    {
        var exception = Assert.Throws<CakeException>(() => Parse(Base + query));

        Assert.Contains(problem, exception.Message);
    }

    private static DownloadDirective Parse(string uri) => DirectiveParser.Parse(new PackageReference(uri));
}
