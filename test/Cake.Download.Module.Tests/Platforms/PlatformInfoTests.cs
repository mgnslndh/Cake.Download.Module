using Cake.Download.Module.Platforms;

namespace Cake.Download.Module.Tests.Platforms;

public sealed class PlatformInfoTests
{
    [Fact]
    public void SupportedRids_Lists_The_Eight_Supported_Platforms_In_Order()
    {
        Assert.Equal(
            ["win-x64", "win-x86", "win-arm64", "linux-x64", "linux-arm64", "linux-arm", "osx-x64", "osx-arm64"],
            PlatformInfo.SupportedRids);
    }

    [Theory]
    [InlineData("win-x64", ".exe")]
    [InlineData("win-arm64", ".exe")]
    [InlineData("linux-x64", "")]
    [InlineData("osx-arm64", "")]
    public void Exe_Is_Only_Set_On_Windows(string rid, string exe)
    {
        Assert.Equal(exe, PlatformInfo.FromRid(rid).Exe);
    }

    [Fact]
    public void FromRid_Is_Case_Insensitive_And_Round_Trips()
    {
        Assert.Equal("linux-arm64", PlatformInfo.FromRid("LINUX-ARM64").Rid);
        Assert.True(PlatformInfo.IsSupportedRid("OSX-X64"));
    }

    [Fact]
    public void FromRid_Rejects_Unsupported_Rids()
    {
        Assert.Throws<ArgumentException>(() => PlatformInfo.FromRid("freebsd-x64"));
        Assert.False(PlatformInfo.IsSupportedRid("osx-x86"));
    }
}
