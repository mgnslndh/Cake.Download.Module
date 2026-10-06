namespace Cake.Download.Module;

/// <summary>
/// The names <c>{os}</c>, <c>{arch}</c> and <c>{triple}</c> expand to (the directive's <c>dialect</c> parameter).
/// </summary>
public enum DownloadDialect
{
    /// <summary>Go names, the default: <c>windows</c>/<c>linux</c>/<c>darwin</c> and <c>amd64</c>/<c>arm64</c>/<c>386</c>/<c>arm</c>.</summary>
    Go,

    /// <summary>.NET names: <c>win</c>/<c>linux</c>/<c>osx</c> and <c>x64</c>/<c>arm64</c>/<c>x86</c>/<c>arm</c>.</summary>
    DotNet,

    /// <summary>Rust names: <c>windows</c>/<c>linux</c>/<c>darwin</c> and <c>x86_64</c>/<c>aarch64</c>/<c>i686</c>/<c>armv7</c>, plus <c>{triple}</c>.</summary>
    Rust,
}
