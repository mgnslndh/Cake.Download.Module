using Cake.Download.Module.Platforms;

namespace Cake.Download.Module.Tests.Platforms;

public sealed class PlatformDialectTests
{
    [Theory]
    [InlineData("go", "win-x64", "windows", "amd64")]
    [InlineData("go", "win-x86", "windows", "386")]
    [InlineData("go", "linux-arm64", "linux", "arm64")]
    [InlineData("go", "linux-arm", "linux", "arm")]
    [InlineData("go", "osx-arm64", "darwin", "arm64")]
    [InlineData("dotnet", "win-x64", "win", "x64")]
    [InlineData("dotnet", "win-x86", "win", "x86")]
    [InlineData("dotnet", "linux-arm", "linux", "arm")]
    [InlineData("dotnet", "osx-x64", "osx", "x64")]
    [InlineData("dotnet", "osx-arm64", "osx", "arm64")]
    [InlineData("rust", "win-x64", "windows", "x86_64")]
    [InlineData("rust", "win-x86", "windows", "i686")]
    [InlineData("rust", "linux-arm64", "linux", "aarch64")]
    [InlineData("rust", "linux-arm", "linux", "armv7")]
    [InlineData("rust", "osx-arm64", "darwin", "aarch64")]
    public void Dialect_Maps_Platform_To_Os_And_Arch(string dialect, string rid, string os, string arch)
    {
        var platform = PlatformInfo.FromRid(rid);
        var target = PlatformDialects.Find(dialect)!;

        Assert.Equal(os, target.GetOs(platform.Os));
        Assert.Equal(arch, target.GetArch(platform.Cpu));
    }

    [Theory]
    [InlineData("win-x64", "x86_64-pc-windows-msvc")]
    [InlineData("win-x86", "i686-pc-windows-msvc")]
    [InlineData("win-arm64", "aarch64-pc-windows-msvc")]
    [InlineData("linux-x64", "x86_64-unknown-linux-musl")]
    [InlineData("linux-arm64", "aarch64-unknown-linux-musl")]
    [InlineData("linux-arm", "armv7-unknown-linux-musleabihf")]
    [InlineData("osx-x64", "x86_64-apple-darwin")]
    [InlineData("osx-arm64", "aarch64-apple-darwin")]
    public void Rust_Dialect_Maps_Every_Supported_Rid_To_A_Triple(string rid, string triple)
    {
        Assert.Equal(triple, PlatformDialects.Rust.GetTriple(PlatformInfo.FromRid(rid)));
    }

    [Fact]
    public void Only_Rust_Has_Triples()
    {
        Assert.True(PlatformDialects.Rust.HasTriples);
        Assert.False(PlatformDialects.Go.HasTriples);
        Assert.False(PlatformDialects.DotNet.HasTriples);
        Assert.Throws<InvalidOperationException>(() => PlatformDialects.Go.GetTriple(PlatformInfo.FromRid("linux-x64")));
    }

    [Fact]
    public void Find_Is_Case_Insensitive_And_Returns_Null_For_Unknown_Dialects()
    {
        Assert.Same(PlatformDialects.DotNet, PlatformDialects.Find("DotNet"));
        Assert.Null(PlatformDialects.Find("python"));
    }

    [Fact]
    public void Values_Expose_The_Dialect_Vocabulary_For_Override_Validation()
    {
        Assert.Equal(["windows", "linux", "darwin"], PlatformDialects.Go.OsValues);
        Assert.Equal(["amd64", "arm64", "386", "arm"], PlatformDialects.Go.ArchValues);
    }
}
