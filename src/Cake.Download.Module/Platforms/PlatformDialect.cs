namespace Cake.Download.Module.Platforms;

/// <summary>
/// A naming convention for platform placeholders, e.g. Go's <c>windows/amd64</c> or .NET's <c>win/x64</c>.
/// </summary>
internal sealed class PlatformDialect
{
    private readonly IReadOnlyDictionary<OsFamily, string> _os;
    private readonly IReadOnlyDictionary<CpuArchitecture, string> _cpu;
    private readonly IReadOnlyDictionary<string, string>? _triples;

    public PlatformDialect(
        string name,
        IReadOnlyDictionary<OsFamily, string> os,
        IReadOnlyDictionary<CpuArchitecture, string> cpu,
        IReadOnlyDictionary<string, string>? triples = null)
    {
        Name = name;
        _os = os;
        _cpu = cpu;
        _triples = triples;
    }

    public string Name { get; }

    public bool HasTriples => _triples is not null;

    public IReadOnlyCollection<string> OsValues => [.. _os.Values];

    public IReadOnlyCollection<string> ArchValues => [.. _cpu.Values];

    public string GetOs(OsFamily os) => _os[os];

    public string GetArch(CpuArchitecture cpu) => _cpu[cpu];

    public string GetTriple(PlatformInfo platform) => _triples is null
        ? throw new InvalidOperationException($"Dialect '{Name}' has no target triples.")
        : _triples[platform.Rid];
}

internal static class PlatformDialects
{
    public static PlatformDialect Go { get; } = new(
        "go",
        new Dictionary<OsFamily, string>
        {
            [OsFamily.Windows] = "windows",
            [OsFamily.Linux] = "linux",
            [OsFamily.MacOS] = "darwin",
        },
        new Dictionary<CpuArchitecture, string>
        {
            [CpuArchitecture.X64] = "amd64",
            [CpuArchitecture.Arm64] = "arm64",
            [CpuArchitecture.X86] = "386",
            [CpuArchitecture.Arm] = "arm",
        });

    public static PlatformDialect DotNet { get; } = new(
        "dotnet",
        new Dictionary<OsFamily, string>
        {
            [OsFamily.Windows] = "win",
            [OsFamily.Linux] = "linux",
            [OsFamily.MacOS] = "osx",
        },
        new Dictionary<CpuArchitecture, string>
        {
            [CpuArchitecture.X64] = "x64",
            [CpuArchitecture.Arm64] = "arm64",
            [CpuArchitecture.X86] = "x86",
            [CpuArchitecture.Arm] = "arm",
        });

    public static PlatformDialect Rust { get; } = new(
        "rust",
        new Dictionary<OsFamily, string>
        {
            [OsFamily.Windows] = "windows",
            [OsFamily.Linux] = "linux",
            [OsFamily.MacOS] = "darwin",
        },
        new Dictionary<CpuArchitecture, string>
        {
            [CpuArchitecture.X64] = "x86_64",
            [CpuArchitecture.Arm64] = "aarch64",
            [CpuArchitecture.X86] = "i686",
            [CpuArchitecture.Arm] = "armv7",
        },
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["win-x64"] = "x86_64-pc-windows-msvc",
            ["win-x86"] = "i686-pc-windows-msvc",
            ["win-arm64"] = "aarch64-pc-windows-msvc",
            ["linux-x64"] = "x86_64-unknown-linux-musl",
            ["linux-arm64"] = "aarch64-unknown-linux-musl",
            ["linux-arm"] = "armv7-unknown-linux-musleabihf",
            ["osx-x64"] = "x86_64-apple-darwin",
            ["osx-arm64"] = "aarch64-apple-darwin",
        });

    public static IReadOnlyList<PlatformDialect> All { get; } = [Go, DotNet, Rust];

    public static PlatformDialect? Find(string name) =>
        All.FirstOrDefault(dialect => string.Equals(dialect.Name, name, StringComparison.OrdinalIgnoreCase));
}
