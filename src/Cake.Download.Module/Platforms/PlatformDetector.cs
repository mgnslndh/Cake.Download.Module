using System.Runtime.InteropServices;
using Cake.Core;

namespace Cake.Download.Module.Platforms;

/// <summary>
/// Detects the platform the build is running on.
/// </summary>
internal interface IPlatformDetector
{
    /// <summary>
    /// Detects the current platform.
    /// </summary>
    /// <returns>The detected, supported platform.</returns>
    PlatformInfo Detect();
}

/// <summary>
/// Detects the platform from Cake's platform family and the operating system's (not the process's) architecture,
/// because a downloaded tool runs as its own process.
/// </summary>
internal sealed class PlatformDetector : IPlatformDetector
{
    private readonly ICakePlatform _platform;
    private readonly Func<Architecture> _osArchitecture;

    public PlatformDetector(ICakePlatform platform)
        : this(platform, () => RuntimeInformation.OSArchitecture)
    {
    }

    public PlatformDetector(ICakePlatform platform, Func<Architecture> osArchitecture)
    {
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _osArchitecture = osArchitecture ?? throw new ArgumentNullException(nameof(osArchitecture));
    }

    public PlatformInfo Detect()
    {
        var architecture = _osArchitecture();
        OsFamily? os = _platform.Family switch
        {
            PlatformFamily.Windows => OsFamily.Windows,
            PlatformFamily.Linux => OsFamily.Linux,
            PlatformFamily.OSX => OsFamily.MacOS,
            _ => null,
        };
        CpuArchitecture? cpu = architecture switch
        {
            Architecture.X64 => CpuArchitecture.X64,
            Architecture.Arm64 => CpuArchitecture.Arm64,
            Architecture.X86 => CpuArchitecture.X86,
            Architecture.Arm => CpuArchitecture.Arm,
            _ => null,
        };

        if (os is null || cpu is null || !new PlatformInfo(os.Value, cpu.Value).IsSupported)
        {
            throw new CakeException(
                $"Cake.Download.Module does not support this platform ({_platform.Family}, {architecture}). " +
                $"Supported platforms: {string.Join(", ", PlatformInfo.SupportedRids)}.");
        }

        return new PlatformInfo(os.Value, cpu.Value);
    }
}
