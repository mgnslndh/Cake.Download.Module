using System.Runtime.InteropServices;
using Cake.Core;
using Cake.Download.Module.Platforms;
using Cake.Testing;

namespace Cake.Download.Module.Tests.Platforms;

public sealed class PlatformDetectorTests
{
    [Theory]
    [InlineData(PlatformFamily.Windows, Architecture.X64, "win-x64")]
    [InlineData(PlatformFamily.Windows, Architecture.X86, "win-x86")]
    [InlineData(PlatformFamily.Windows, Architecture.Arm64, "win-arm64")]
    [InlineData(PlatformFamily.Linux, Architecture.X64, "linux-x64")]
    [InlineData(PlatformFamily.Linux, Architecture.Arm64, "linux-arm64")]
    [InlineData(PlatformFamily.Linux, Architecture.Arm, "linux-arm")]
    [InlineData(PlatformFamily.OSX, Architecture.X64, "osx-x64")]
    [InlineData(PlatformFamily.OSX, Architecture.Arm64, "osx-arm64")]
    public void Detect_Maps_Family_And_Os_Architecture_To_A_Rid(PlatformFamily family, Architecture architecture, string rid)
    {
        var detector = new PlatformDetector(new FakePlatform(family), () => architecture);

        Assert.Equal(rid, detector.Detect().Rid);
    }

    [Theory]
    [InlineData(PlatformFamily.FreeBSD, Architecture.X64)]
    [InlineData(PlatformFamily.Unknown, Architecture.X64)]
    [InlineData(PlatformFamily.OSX, Architecture.X86)]
    [InlineData(PlatformFamily.Windows, Architecture.Arm)]
    [InlineData(PlatformFamily.Linux, Architecture.S390x)]
    public void Detect_Rejects_Unsupported_Platforms(PlatformFamily family, Architecture architecture)
    {
        var detector = new PlatformDetector(new FakePlatform(family), () => architecture);

        var exception = Assert.Throws<CakeException>(() => detector.Detect());

        Assert.Contains($"({family}, {architecture})", exception.Message);
        Assert.Contains("Supported platforms: win-x64, win-x86, win-arm64, linux-x64, linux-arm64, linux-arm, osx-x64, osx-arm64.", exception.Message);
    }
}
