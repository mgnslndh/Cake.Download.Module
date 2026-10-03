using Cake.Core;
using Cake.Core.Packaging;
using Cake.Download.Module.Directives;
using Cake.Download.Module.Platforms;

namespace Cake.Download.Module.Tests.Directives;

public sealed class DownloadPlannerTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string Jq = "download:https://github.com/jqlang/jq/releases/download/jq-{version}/jq-{os}-{arch}{exe}?package=jq&version=1.8.2&os.darwin=macos";

    [Theory]
    [InlineData("linux-x64", "https://github.com/jqlang/jq/releases/download/jq-1.8.2/jq-linux-amd64", "jq")]
    [InlineData("osx-arm64", "https://github.com/jqlang/jq/releases/download/jq-1.8.2/jq-macos-arm64", "jq")]
    [InlineData("win-x64", "https://github.com/jqlang/jq/releases/download/jq-1.8.2/jq-windows-amd64.exe", "jq.exe")]
    public void Create_Expands_Go_Placeholders_With_Overrides(string rid, string url, string fileName)
    {
        var plan = Plan(Jq + "&sha256=skip", rid);

        Assert.Equal(url, plan.Url.AbsoluteUri);
        Assert.Equal(ArchiveFormat.File, plan.Format);
        Assert.Equal(fileName, plan.FileName);
        Assert.Equal([fileName], plan.Include);
        Assert.Empty(plan.Exclude);
        Assert.Equal("jq.1.8.2", plan.FolderName);
        Assert.Equal(rid, plan.Platform.Rid);
        Assert.Equal("go", plan.Dialect);
    }

    [Fact]
    public void Create_Uses_Archive_Default_And_Override()
    {
        const string Gh = "download:https://github.com/cli/cli/releases/download/v{version}/gh_{version}_{os}_{arch}.{archive}?package=gh&version=2.62.0&os.darwin=macOS&archive.darwin=zip&sha256=skip";

        Assert.Equal("https://github.com/cli/cli/releases/download/v2.62.0/gh_2.62.0_linux_amd64.tar.gz", Plan(Gh, "linux-x64").Url.AbsoluteUri);
        Assert.Equal("https://github.com/cli/cli/releases/download/v2.62.0/gh_2.62.0_macOS_arm64.zip", Plan(Gh, "osx-arm64").Url.AbsoluteUri);
        Assert.Equal("https://github.com/cli/cli/releases/download/v2.62.0/gh_2.62.0_windows_amd64.zip", Plan(Gh, "win-x64").Url.AbsoluteUri);

        var linux = Plan(Gh, "linux-x64");
        Assert.Equal(ArchiveFormat.TarGz, linux.Format);
        Assert.Null(linux.FileName);
        Assert.Equal(["**/gh"], linux.Include);
        Assert.Equal(["**/gh.exe"], Plan(Gh, "win-x64").Include);
    }

    [Theory]
    [InlineData("linux-x64", "https://example.com/14.1.1/ripgrep-14.1.1-x86_64-unknown-linux-musl.tar.gz")]
    [InlineData("linux-arm64", "https://example.com/14.1.1/ripgrep-14.1.1-aarch64-unknown-linux-gnu.tar.gz")]
    [InlineData("win-x64", "https://example.com/14.1.1/ripgrep-14.1.1-x86_64-pc-windows-msvc.zip")]
    public void Create_Uses_Rust_Triples_And_Triple_Overrides(string rid, string url)
    {
        var plan = Plan("download:https://example.com/{version}/ripgrep-{version}-{triple}.{archive}?package=rg&version=14.1.1&dialect=rust&triple.linux-arm64=aarch64-unknown-linux-gnu&sha256=skip", rid);

        Assert.Equal(url, plan.Url.AbsoluteUri);
    }

    [Fact]
    public void Create_Rejects_Triple_Placeholder_Outside_The_Rust_Dialect()
    {
        var exception = Assert.Throws<CakeException>(() => Plan("download:https://example.com/t-{triple}?package=t&version=1&sha256=skip", "linux-x64"));

        Assert.StartsWith("Unknown placeholder '{triple}' in the download URL.", exception.Message);
    }

    [Fact]
    public void Create_Uses_The_Dotnet_Rid()
    {
        var plan = Plan("download:https://example.com/v{version}/cyclonedx-{rid}{exe}?package=cyclonedx&version=0.30.0&dialect=dotnet&sha256=skip", "win-arm64");

        Assert.Equal("https://example.com/v0.30.0/cyclonedx-win-arm64.exe", plan.Url.AbsoluteUri);
        Assert.Equal("cyclonedx.exe", plan.FileName);
    }

    [Fact]
    public void Create_Prefers_The_Url_For_The_Current_Rid()
    {
        const string Directive = "download:https://example.com/default.zip?package=t&version=1&sha256=skip&url.linux-x64=https%3A%2F%2Fmirror.example.com%2F%7Bversion%7D%2Flinux.zip";

        Assert.Equal("https://mirror.example.com/1/linux.zip", Plan(Directive, "linux-x64").Url.AbsoluteUri);
        Assert.Equal("https://example.com/default.zip", Plan(Directive, "osx-x64").Url.AbsoluteUri);
    }

    [Fact]
    public void Create_Fails_When_No_Url_Covers_The_Current_Rid()
    {
        var exception = Assert.Throws<CakeException>(
            () => Plan("download:?package=t&version=1&sha256=skip&url.win-x64=https%3A%2F%2Fexample.com%2Ft.zip", "linux-x64"));

        Assert.StartsWith("The download directive for 't' has no URL for linux-x64. Add 'url.linux-x64=<url>' or a default URL.", exception.Message);
    }

    [Fact]
    public void Create_Keeps_Percent_Encoding_In_The_Template_Verbatim()
    {
        var plan = Plan("download:https://example.com/a%2Bb/tool%20x-{version}.zip?package=t&version=1&sha256=skip", "linux-x64");

        Assert.Equal("https://example.com/a%2Bb/tool%20x-1.zip", plan.Url.AbsoluteUri);
        Assert.Equal("tool x-1.zip", plan.AssetName);
    }

    [Theory]
    [InlineData("https://example.com/t.zip", ArchiveFormat.Zip)]
    [InlineData("https://example.com/t.ZIP", ArchiveFormat.Zip)]
    [InlineData("https://example.com/t.tar.gz", ArchiveFormat.TarGz)]
    [InlineData("https://example.com/t.tgz", ArchiveFormat.TarGz)]
    [InlineData("https://example.com/t.tar", ArchiveFormat.Tar)]
    [InlineData("https://example.com/t.exe", ArchiveFormat.File)]
    [InlineData("https://example.com/t", ArchiveFormat.File)]
    [InlineData("https://example.com/t.tar.xz", ArchiveFormat.File)]
    public void Create_Detects_The_Format_From_The_Url_Path(string url, ArchiveFormat format)
    {
        Assert.Equal(format, Plan("download:" + url + "?package=t&version=1&sha256=skip", "linux-x64").Format);
    }

    [Fact]
    public void Create_Lets_An_Explicit_Format_Win()
    {
        Assert.Equal(ArchiveFormat.Zip, Plan("download:https://example.com/t?package=t&version=1&sha256=skip&format=zip", "linux-x64").Format);
    }

    [Fact]
    public void Create_Expands_Custom_Filename_Include_And_Exclude()
    {
        var raw = Plan("download:https://example.com/t.jar?package=t&version=3.1&sha256=skip&filename=t-%7Bversion%7D.jar", "linux-x64");
        Assert.Equal("t-3.1.jar", raw.FileName);
        Assert.Equal(["t-3.1.jar"], raw.Include);

        var archive = Plan("download:https://example.com/t.zip?package=t&version=2&sha256=skip&include=**%2Fbin%2F*%7Bexe%7D&exclude=**%2Ftest-%7Bversion%7D%7Bexe%7D", "win-x64");
        Assert.Equal(["**/bin/*.exe"], archive.Include);
        Assert.Equal(["**/test-2.exe"], archive.Exclude);
    }

    [Fact]
    public void Create_Rejects_A_Filename_For_Archives()
    {
        var exception = Assert.Throws<CakeException>(
            () => Plan("download:https://example.com/t.zip?package=t&version=1&sha256=skip&filename=t", "linux-x64"));

        Assert.StartsWith("'filename' only applies to raw file downloads, but https://example.com/t.zip is a zip archive.", exception.Message);
    }

    [Theory]
    [InlineData("download:http://example.com/t.zip?package=t&version=1&sha256=skip", "The download URL 'http://example.com/t.zip' for 't' is not an absolute https URL.")]
    [InlineData("download:https://example.com/t.zip?package=t&version=1&checksums=http%3A%2F%2Fexample.com%2Fs.txt&checksums_sha256=" + Hash, "The checksums URL 'http://example.com/s.txt' for 't' is not an absolute https URL.")]
    public void Create_Rejects_Non_Https_Urls(string directive, string message)
    {
        var exception = Assert.Throws<CakeException>(() => Plan(directive, "linux-x64"));

        Assert.StartsWith(message, exception.Message);
    }

    [Fact]
    public void Create_Resolves_Relative_And_Absolute_Checksums_References()
    {
        var relative = Assert.IsType<ChecksumsFilePlan>(Plan(Jq + "&checksums=sha256sum.txt&checksums_sha256=" + Hash, "linux-x64").Integrity);
        Assert.Equal("https://github.com/jqlang/jq/releases/download/jq-1.8.2/sha256sum.txt", relative.Url.AbsoluteUri);
        Assert.Equal(Hash, relative.Sha256);
        Assert.Equal("checksums:https://github.com/jqlang/jq/releases/download/jq-1.8.2/sha256sum.txt#" + Hash, relative.Fingerprint);

        var absolute = Assert.IsType<ChecksumsFilePlan>(Plan(Jq + "&checksums=https%3A%2F%2Fsums.example.com%2Fjq-%7Bversion%7D.txt", "linux-x64").Integrity);
        Assert.Equal("https://sums.example.com/jq-1.8.2.txt", absolute.Url.AbsoluteUri);
        Assert.Null(absolute.Sha256);
    }

    [Fact]
    public void Create_Selects_The_Hash_For_The_Current_Rid()
    {
        var plan = Plan(Jq + "&sha256.linux-x64=" + Hash, "linux-x64");

        Assert.Equal(new PinnedSha256(Hash, "sha256.linux-x64"), plan.Integrity);
        Assert.Equal("sha256:" + Hash, plan.Integrity.Fingerprint);
        Assert.Equal(new MissingIntegrityPlan("sha256.osx-arm64"), Plan(Jq + "&sha256.linux-x64=" + Hash, "osx-arm64").Integrity);
    }

    [Fact]
    public void Create_Suggests_A_Per_Rid_Hash_Only_For_Platform_Specific_Urls()
    {
        Assert.Equal(new MissingIntegrityPlan("sha256.linux-x64"), Plan(Jq, "linux-x64").Integrity);
        Assert.Equal(new MissingIntegrityPlan("sha256"), Plan("download:https://example.com/t-{version}.jar?package=t&version=1", "linux-x64").Integrity);
        Assert.Equal(new PinnedSha256(Hash, "sha256"), Plan("download:https://example.com/t.jar?package=t&version=1&sha256=" + Hash, "linux-x64").Integrity);
        Assert.Equal("skip", Plan(Jq + "&sha256=skip", "linux-x64").Integrity.Fingerprint);
    }

    [Fact]
    public void Create_Exposes_Placeholder_Values_For_Diagnostics()
    {
        var plan = Plan(Jq + "&sha256=skip", "osx-arm64");

        Assert.Equal("macos", plan.Placeholders["os"]);
        Assert.Equal("arm64", plan.Placeholders["arch"]);
        Assert.Equal("osx-arm64", plan.Placeholders["rid"]);
        Assert.Equal("tar.gz", plan.Placeholders["archive"]);
    }

    private static DownloadPlan Plan(string directive, string rid) =>
        DownloadPlanner.Create(DirectiveParser.Parse(new PackageReference(directive)), PlatformInfo.FromRid(rid));
}
