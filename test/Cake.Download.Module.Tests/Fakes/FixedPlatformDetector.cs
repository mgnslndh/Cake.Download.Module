using Cake.Download.Module.Platforms;

namespace Cake.Download.Module.Tests.Fakes;

internal sealed class FixedPlatformDetector(PlatformInfo platform) : IPlatformDetector
{
    public PlatformInfo Detect() => platform;
}
