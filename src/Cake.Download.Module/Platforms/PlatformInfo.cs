namespace Cake.Download.Module.Platforms;

internal enum OsFamily
{
    Windows,
    Linux,
    MacOS,
}

internal enum CpuArchitecture
{
    X64,
    Arm64,
    X86,
    Arm,
}

/// <summary>
/// An operating system and CPU architecture pair; identifies a platform by its .NET RID.
/// </summary>
internal sealed record PlatformInfo(OsFamily Os, CpuArchitecture Cpu)
{
    public static IReadOnlyList<PlatformInfo> Supported { get; } =
    [
        new(OsFamily.Windows, CpuArchitecture.X64),
        new(OsFamily.Windows, CpuArchitecture.X86),
        new(OsFamily.Windows, CpuArchitecture.Arm64),
        new(OsFamily.Linux, CpuArchitecture.X64),
        new(OsFamily.Linux, CpuArchitecture.Arm64),
        new(OsFamily.Linux, CpuArchitecture.Arm),
        new(OsFamily.MacOS, CpuArchitecture.X64),
        new(OsFamily.MacOS, CpuArchitecture.Arm64),
    ];

    public static IReadOnlyList<string> SupportedRids { get; } = Supported.Select(platform => platform.Rid).ToList();

    public string Rid => $"{RidOs}-{RidCpu}";

    public bool IsWindows => Os == OsFamily.Windows;

    public string Exe => IsWindows ? ".exe" : string.Empty;

    public bool IsSupported => Supported.Contains(this);

    private string RidOs => Os switch
    {
        OsFamily.Windows => "win",
        OsFamily.Linux => "linux",
        _ => "osx",
    };

    private string RidCpu => Cpu switch
    {
        CpuArchitecture.X64 => "x64",
        CpuArchitecture.Arm64 => "arm64",
        CpuArchitecture.X86 => "x86",
        _ => "arm",
    };

    public static bool IsSupportedRid(string rid) => SupportedRids.Contains(rid, StringComparer.OrdinalIgnoreCase);

    public static PlatformInfo FromRid(string rid) =>
        Supported.FirstOrDefault(platform => string.Equals(platform.Rid, rid, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"'{rid}' is not a supported RID.", nameof(rid));
}
