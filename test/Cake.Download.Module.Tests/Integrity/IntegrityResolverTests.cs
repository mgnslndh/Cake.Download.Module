using Cake.Core;
using Cake.Core.Packaging;
using Cake.Download.Module.Directives;
using Cake.Download.Module.Http;
using Cake.Download.Module.Integrity;
using Cake.Download.Module.Platforms;
using Cake.Download.Module.Tests.Fakes;
using Cake.Testing;

namespace Cake.Download.Module.Tests.Integrity;

public sealed class IntegrityResolverTests : IDisposable
{
    private const string AssetHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string OtherHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string SumsUrl = "https://example.com/releases/v1/SHA256SUMS";

    private readonly TestDirectory _directory = new();
    private readonly FakeHttpHandler _handler = new();
    private readonly IntegrityResolver _resolver;

    public IntegrityResolverTests()
    {
        _resolver = new IntegrityResolver(new HttpDownloader(_handler, new FakeLog(), new DownloadOptions { Delay = (_, _) => Task.CompletedTask }));
    }

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void Resolve_Returns_A_Pinned_Hash_Without_Network()
    {
        var expected = _resolver.Resolve(Plan("sha256=" + AssetHash), _directory.Root);

        Assert.Equal(new ExpectedHash(AssetHash, "'sha256'"), expected);
        Assert.Empty(_handler.RequestedUrls);
    }

    [Fact]
    public void Resolve_Returns_Null_For_Skip_And_Missing()
    {
        Assert.Null(_resolver.Resolve(Plan("sha256=skip"), _directory.Root));
        Assert.Null(_resolver.Resolve(Plan(string.Empty), _directory.Root));
    }

    [Fact]
    public void Resolve_Looks_Up_The_Asset_In_A_Pinned_Checksums_File()
    {
        var sums = $"{OtherHash}  tool-darwin-arm64\n{AssetHash}  tool-linux-amd64\n";
        _handler.Respond(SumsUrl, FakeHttpHandler.Ok(sums));

        var expected = _resolver.Resolve(Plan("checksums=SHA256SUMS&checksums_sha256=" + TestHashes.Sha256(sums)), _directory.Root);

        Assert.Equal(new ExpectedHash(AssetHash, "checksums file " + SumsUrl), expected);
    }

    [Fact]
    public void Resolve_Finds_An_Asset_Listed_With_A_Directory_Prefix()
    {
        var sums = $"{AssetHash}  deployment/m2/tool-linux-amd64\n";
        _handler.Respond(SumsUrl, FakeHttpHandler.Ok(sums));

        var expected = _resolver.Resolve(Plan("checksums=SHA256SUMS&checksums_sha256=" + TestHashes.Sha256(sums)), _directory.Root);

        Assert.Equal(AssetHash, expected!.Sha256);
    }

    [Fact]
    public void Resolve_Fails_For_An_Unpinned_Checksums_File_With_A_Paste_Ready_Hash()
    {
        var sums = $"{AssetHash}  tool-linux-amd64\n";
        _handler.Respond(SumsUrl, FakeHttpHandler.Ok(sums));

        var exception = Assert.Throws<CakeException>(() => _resolver.Resolve(Plan("checksums=SHA256SUMS"), _directory.Root));

        Assert.Equal(
            $"The checksums file {SumsUrl} is not pinned. Its SHA-256 is {TestHashes.Sha256(sums)}. Add '&checksums_sha256={TestHashes.Sha256(sums)}' to the directive for 'tool'.",
            exception.Message);
    }

    [Fact]
    public void Resolve_Fails_When_The_Checksums_File_Hash_Differs()
    {
        var sums = $"{AssetHash}  tool-linux-amd64\n";
        _handler.Respond(SumsUrl, FakeHttpHandler.Ok(sums));

        var exception = Assert.Throws<CakeException>(() => _resolver.Resolve(Plan("checksums=SHA256SUMS&checksums_sha256=" + OtherHash), _directory.Root));

        Assert.Contains($"SHA-256 mismatch for checksums file {SumsUrl}.", exception.Message);
        Assert.Contains($"Expected: {OtherHash} (from 'checksums_sha256')", exception.Message);
        Assert.Contains($"Actual:   {TestHashes.Sha256(sums)}", exception.Message);
    }

    [Fact]
    public void Resolve_Fails_When_The_Asset_Is_Not_Listed()
    {
        var sums = $"{OtherHash}  tool-darwin-arm64\n";
        _handler.Respond(SumsUrl, FakeHttpHandler.Ok(sums));

        var exception = Assert.Throws<CakeException>(() => _resolver.Resolve(Plan("checksums=SHA256SUMS&checksums_sha256=" + TestHashes.Sha256(sums)), _directory.Root));

        Assert.Equal($"'tool-linux-amd64' is not listed in the checksums file {SumsUrl}. Listed files: tool-darwin-arm64.", exception.Message);
    }

    [Fact]
    public void Verify_Accepts_A_Matching_Hash_And_Skip()
    {
        IntegrityResolver.Verify(Plan("sha256=" + AssetHash), new ExpectedHash(AssetHash, "'sha256'"), AssetHash);
        IntegrityResolver.Verify(Plan("sha256=skip"), null, OtherHash);
    }

    [Fact]
    public void Verify_Rejects_A_Mismatch()
    {
        var exception = Assert.Throws<CakeException>(
            () => IntegrityResolver.Verify(Plan("sha256=" + AssetHash), new ExpectedHash(AssetHash, "'sha256'"), OtherHash));

        Assert.Contains("SHA-256 mismatch for https://example.com/releases/v1/tool-linux-amd64.", exception.Message);
        Assert.Contains($"Expected: {AssetHash} (from 'sha256')", exception.Message);
        Assert.Contains($"Actual:   {OtherHash}", exception.Message);
        Assert.Contains("The download was discarded.", exception.Message);
    }

    [Fact]
    public void Verify_Explains_Missing_Integrity_With_The_Exact_Parameter_To_Add()
    {
        var exception = Assert.Throws<CakeException>(() => IntegrityResolver.Verify(Plan(string.Empty), null, OtherHash));

        Assert.Equal(
            $"The download directive for 'tool' has no integrity check. tool-linux-amd64 (linux-x64) has SHA-256 {OtherHash}. " +
            $"Add '&sha256.linux-x64={OtherHash}' to the directive, or '&sha256=skip' to install without verification (not recommended). " +
            "The download was discarded.",
            exception.Message);
    }

    private static DownloadPlan Plan(string query) => DownloadPlanner.Create(
        DirectiveParser.Parse(new PackageReference("download:https://example.com/releases/v1/tool-{os}-{arch}?package=tool&version=1&" + query)),
        PlatformInfo.FromRid("linux-x64"));
}
