# Cake.Download.Module Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `Cake.Download.Module`, a Cake module adding a `download:` scheme to `#tool` / `InstallTool`. It downloads a tool over HTTPS, verifies its SHA-256, extracts it, and registers it with Cake's tool locator.

**Architecture:** One `IPackageInstaller` (`DownloadPackageInstaller`), registered by `DownloadModule`, runs a pipeline of small internal units:
1. parse the directive
2. detect the platform
3. expand templates into a `DownloadPlan`
4. resolve integrity
5. download with retries
6. verify the hash
7. extract
8. publish atomically into `<tools>/<package>.<version>/`
9. select the files to register

A Frosting `build/` project builds, tests and packs the module, then runs it in all three Cake runners (Cake.Tool script, Cake.Sdk, Frosting) against real GitHub releases.

**Tech Stack:** C# (LangVersion preview), .NET SDK 10.0.100, module targets net8.0/net9.0/net10.0, Cake.Core 6.0.0 (`PrivateAssets=all`), BCL only at runtime (`System.Net.Http`, `System.Text.Json`, `System.IO.Compression`, `System.Formats.Tar`), xUnit v3 on Microsoft Testing Platform, Cake.Testing 6.0.0, Cake.Frosting 6.3.0 for `build/`, MinVer, StyleCop.Analyzers.

**Spec:** `docs/superpowers/specs/2026-10-02-cake-download-module-design.md` (read it before starting any task).

## Global Constraints

- Package ID and assembly name: `Cake.Download.Module`. The assembly carries `[assembly: CakeModule(typeof(DownloadModule))]`.
- Scheme `download` (case-insensitive), `PackageType.Tool` only.
- `Cake.Core` is referenced at **6.0.0** with `PrivateAssets="all"`. The nupkg must declare **zero** dependencies, and only BCL APIs may be used at runtime.
- **HTTPS only:** download URL, `checksums` URL and every redirect target.
- **Supported RIDs:** `win-x64`, `win-x86`, `win-arm64`, `linux-x64`, `linux-arm64`, `linux-arm`, `osx-x64`, `osx-arm64`.
- **Install folder:** `<tools>/<package>.<version>/`. Staging is `<tools>/.<package>.<version>.tmp-<guid>/`. The marker is `.cake-download.json` with `schema` 1.
- **HTTP:** stall timeout 60 s, 3 attempts with backoff 1 s / 2 s, `Retry-After` capped at 30 s, at most 10 redirects. The checksums file is capped at 1 MiB (1048576 bytes).
- Every failure the user can fix is a `CakeException` with the actionable content listed in spec §9.
- **Release build must stay clean:** `src/Directory.Build.props` sets `TreatWarningsAsErrors` in Release and enables StyleCop. Always use braces, put each enum member on its own line, and guard every Unix-only API call (`File.SetUnixFileMode`, `File.GetUnixFileMode`) with an `OperatingSystem.IsWindows()` check in the same method, so the platform analyzer (CA1416) stays quiet.
- **Commits:** work on branch `feature/download-module`, created from `design/download-module`. Every commit message ends with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

### Deliberate deviations from the spec (approved by the plan, not by the spec)

| Spec | Plan | Why |
|---|---|---|
| Runner tests and build tests in `tests/` (Cake.CycloneDX layout) | everything test-related under `test/` | One test folder. `test/Directory.*` import the `src/` ones; `test/Build.Tests` and `test/runners` opt out with their own `Directory.*` files. |
| `net8.0` | `net8.0;net9.0;net10.0` | Cake's addin and module best practices and the repo conventions (`AGENTS.md`) list all three. |
| Synchronous `HttpClient.Send` | async internals with one `GetAwaiter().GetResult()` at the boundary | Needed for the stall timeout (cancellable reads). Cake runners have no `SynchronizationContext`, so blocking is safe. |
| `FileSelector` uses Cake `IGlobber` | small internal `GlobMatcher` (`**`, `*`, `?`) | Matching relative paths deterministically is simpler and fully testable. |
| Cache hit regardless of files | cache hit only if `include` still matches a file; otherwise reinstall | A user who deleted the tool expects it back (see Review Focus). |
| `Cake.Testing.Xunit.v3` helper project | xUnit v3 `Assert.Skip` for Unix-only tests | Avoids a helper project. |

## Review Focus

These cases are not exercised by the spec's own test list but are likely to hit real users. Each one has a pinned test in the task named.

1. **Percent-encoded characters in the URL template** (e.g. `a%2Bb`) must reach the server exactly as written, not decoded or encoded twice. Test in Task 4.
2. **Upper-case scheme and parameter keys** (`DOWNLOAD:…?Package=…&SHA256=…`) must work, because Cake's keys are case-insensitive. Test in Task 3.
3. **`+` in a version** (`1.2.3+build.5`) must survive decoding (it must not become a space). Test in Task 3.
4. **The registered tool file was deleted** from a valid install folder. The next build must reinstall it rather than fail. Test in Task 10.
5. **A tools path containing spaces** (`My Tools/`) must install and register normally. Test in Task 10.

## File Map

```
global.json                                   SDK 10 + MTP test runner
icon.png                                      package icon (cake-contrib module icon)
README.md, CHANGELOG.md, AGENTS.md            docs (Task 13)
build.ps1, build.sh                           run the Frosting build
build/                                        Frosting build project (Tasks 11–13)
  MinVer.props                                (Task 1)
src/
  Directory.Build.props/.targets, Directory.Packages.props, CodeAnalysis.ruleset, StyleCop.json
  Cake.Download.Module.slnx
  Cake.Download.Module/
    DownloadModule.cs                         public ICakeModule + [assembly: CakeModule]
    DownloadPackageInstaller.cs               public IPackageInstaller, orchestration
    Platforms/PlatformInfo.cs                 OsFamily, CpuArchitecture, PlatformInfo (RIDs)
    Platforms/PlatformDetector.cs             IPlatformDetector + ICakePlatform/OSArchitecture detection
    Platforms/PlatformDialect.cs              PlatformDialect + PlatformDialects (go/dotnet/rust)
    Directives/PlaceholderExpander.cs         {placeholder} expansion
    Directives/DownloadDirective.cs           parsed directive + IntegritySpec records
    Directives/DirectiveParser.cs             PackageReference → DownloadDirective (validation)
    Directives/DownloadPlan.cs                ArchiveFormat, DownloadPlan, IntegrityPlan records
    Directives/DownloadPlanner.cs             directive + platform → DownloadPlan
    Http/DownloadOptions.cs, DownloadResult.cs, DownloadHttpException.cs, HttpDownloader.cs
    Integrity/ChecksumsFileParser.cs, IntegrityResolver.cs
    Archives/ArchiveExtractor.cs
    Installation/GlobMatcher.cs, FileSelector.cs, InstallMarker.cs, StagingArea.cs, InstallStore.cs
test/
  Directory.Build.props/.targets, Directory.Packages.props   import the src/ ones (StyleCop, CPM)
  Cake.Download.Module.Tests/                 xUnit v3 (MTP), mirrors the src folders above + Fakes/
  Build.Tests/                                tests for build/ helpers (own Directory.* files: no CPM, no StyleCop)
  runners/script/build.cake, runners/sdk/cake.cs, runners/frosting/*   (own Directory.* files: isolated)
  Release.Tests.ps1                           Pester tests for release.ps1 (Task 13)
.github/                                      setup action + CI/PR/release workflows (Task 13)
```

Run unit tests for one class with:
`dotnet test --project test/Cake.Download.Module.Tests/Cake.Download.Module.Tests.csproj --filter-class "*<ClassName>"`

---

### Task 1: Repository scaffold and platform model

**Files:**
- Create: `global.json`, `icon.png`, `build/MinVer.props`
- Create: `src/Directory.Build.props`, `src/Directory.Build.targets`, `src/Directory.Packages.props`, `src/CodeAnalysis.ruleset`, `src/StyleCop.json`, `src/Cake.Download.Module.slnx`
- Create: `src/Cake.Download.Module/Cake.Download.Module.csproj`, `src/Cake.Download.Module/Platforms/PlatformInfo.cs`, `src/Cake.Download.Module/Platforms/PlatformDetector.cs`
- Create: `test/Directory.Build.props`, `test/Directory.Build.targets`, `test/Directory.Packages.props`
- Create: `test/Cake.Download.Module.Tests/Cake.Download.Module.Tests.csproj`, `test/Cake.Download.Module.Tests/Platforms/PlatformInfoTests.cs`, `test/Cake.Download.Module.Tests/Platforms/PlatformDetectorTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `enum OsFamily { Windows, Linux, MacOS }`, `enum CpuArchitecture { X64, Arm64, X86, Arm }`
  - `sealed record PlatformInfo(OsFamily Os, CpuArchitecture Cpu)` with:
    - `string Rid`, `bool IsWindows`, `string Exe`, `bool IsSupported`
    - static `IReadOnlyList<PlatformInfo> Supported`, `IReadOnlyList<string> SupportedRids`
    - static `bool IsSupportedRid(string)`, `PlatformInfo FromRid(string)`
  - `interface IPlatformDetector { PlatformInfo Detect(); }`
  - `sealed class PlatformDetector(ICakePlatform platform[, Func<Architecture> osArchitecture]) : IPlatformDetector`
  - Namespace for all of these: `Cake.Download.Module.Platforms`.

- [ ] **Step 1: Create the branch and the repo-level files**

```bash
git checkout design/download-module
git checkout -b feature/download-module
curl -sSfL -o icon.png https://cdn.jsdelivr.net/gh/cake-contrib/graphics/png/module/cake-contrib-module-medium.png
mkdir -p src build
cp ../Cake.CycloneDX/src/CodeAnalysis.ruleset src/CodeAnalysis.ruleset
cp ../Cake.CycloneDX/src/StyleCop.json src/StyleCop.json
sed -i 's/Cake.CycloneDX Ruleset/Cake.Download.Module Ruleset/' src/CodeAnalysis.ruleset
```

`global.json`:

```json
{
    "projects": [
        "src"
    ],
    "sdk": {
        "version": "10.0.100",
        "rollForward": "latestFeature"
    },
    "test": {
        "runner": "Microsoft.Testing.Platform"
    }
}
```

`build/MinVer.props`:

```xml
<Project>
    <PropertyGroup>
        <MinVerTagPrefix>v</MinVerTagPrefix>
        <MinVerDefaultPreReleaseIdentifiers>alpha.0</MinVerDefaultPreReleaseIdentifiers>
    </PropertyGroup>
</Project>
```

- [ ] **Step 2: Create the shared MSBuild files**

`src/Directory.Build.props`:

```xml
<Project>

  <Import Project="$(MSBuildThisFileDirectory)..\build\MinVer.props" />

  <PropertyGroup>
    <Deterministic>true</Deterministic>
    <LangVersion>preview</LangVersion>

    <NuGetAuditMode>all</NuGetAuditMode>
    <NuGetAuditLevel>low</NuGetAuditLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>

    <DebugSymbols>true</DebugSymbols>
    <DebugType>embedded</DebugType>
  </PropertyGroup>

  <PropertyGroup Label="Repository Information">
    <RepositoryType>git</RepositoryType>
    <RepositoryUrl>https://github.com/mgnslndh/Cake.Download.Module</RepositoryUrl>
  </PropertyGroup>

  <PropertyGroup Label="Package Information">
    <IsPackable>false</IsPackable>
    <Copyright>Copyright © Magnus Lindhe</Copyright>
    <PackageTags>cake,cake-module,cake-build,download,tool,github,release,binary</PackageTags>
    <PackageIcon>icon.png</PackageIcon>
    <PackageProjectUrl>https://github.com/mgnslndh/Cake.Download.Module</PackageProjectUrl>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <Authors>Magnus Lindhe</Authors>
    <Description>Cake module that installs tools from HTTPS URLs and GitHub release assets with the download: scheme: platform placeholders, mandatory SHA-256 verification, zip/tar.gz extraction.</Description>
  </PropertyGroup>

  <ItemGroup>
    <None Include="$(MSBuildThisFileDirectory)..\icon.png" Pack="true" PackagePath="\" Visible="false"/>
    <None Include="$(MSBuildThisFileDirectory)..\README.md" Pack="true" PackagePath="\" Visible="false"/>
  </ItemGroup>

  <PropertyGroup Label="Deterministic Build" Condition="'$(GITHUB_ACTIONS)' == 'true'">
    <ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>
  </PropertyGroup>

  <PropertyGroup Label="Source Link">
    <PublishRepositoryUrl>true</PublishRepositoryUrl>
    <EmbedUntrackedSources>true</EmbedUntrackedSources>
  </PropertyGroup>

  <PropertyGroup Condition=" '$(Configuration)' == 'Release' ">
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>

  <PropertyGroup>
    <CodeAnalysisRuleSet>$(MSBuildThisFileDirectory)CodeAnalysis.ruleset</CodeAnalysisRuleSet>
  </PropertyGroup>
  <ItemGroup>
    <AdditionalFiles Include="$(MSBuildThisFileDirectory)StyleCop.json" Link="StyleCop.json" />
  </ItemGroup>
</Project>
```

`src/Directory.Build.targets`:

```xml
<Project>

  <!-- Documentation rules only apply to projects that ship an XML documentation file (the module). -->
  <PropertyGroup Condition=" '$(GenerateDocumentationFile)' != 'true' ">
    <NoWarn>$(NoWarn);SA0001;SA1600;SA1601;SA1602</NoWarn>
  </PropertyGroup>

</Project>
```

`src/Directory.Packages.props`:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>
  <ItemGroup>
    <GlobalPackageReference Include="Microsoft.SourceLink.GitHub" Version="10.0.401" PrivateAssets="All" />
    <GlobalPackageReference Include="StyleCop.Analyzers" Version="1.2.0-beta.556" PrivateAssets="All" />
    <PackageVersion Include="Cake.Core" Version="6.0.0" />
    <PackageVersion Include="Cake.Testing" Version="6.0.0" />
    <PackageVersion Include="MinVer" Version="8.0.0" />
    <PackageVersion Include="xunit.v3" Version="4.0.1" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create the two projects and the solution**

`src/Cake.Download.Module/Cake.Download.Module.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net10.0;net9.0;net8.0</TargetFrameworks>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>true</IsPackable>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Cake.Core" PrivateAssets="all" />
    <PackageReference Include="MinVer" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Cake.Download.Module.Tests" />
  </ItemGroup>

</Project>
```

`test/Cake.Download.Module.Tests/Cake.Download.Module.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
    <TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>
    <TestingPlatformShowTestsFailure>true</TestingPlatformShowTestsFailure>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Cake.Download.Module\Cake.Download.Module.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Cake.Core" />
    <PackageReference Include="Cake.Testing" />
    <PackageReference Include="xunit.v3" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

</Project>
```

The test project lives under `test/`, so `test/` gets its own `Directory.*` files that pull in the `src/` ones (StyleCop, analyzers, central package versions). `test/Build.Tests` and `test/runners` opt out again in Tasks 11 and 12.

`test/Directory.Build.props`:

```xml
<Project>
  <Import Project="$(MSBuildThisFileDirectory)..\src\Directory.Build.props" />
</Project>
```

`test/Directory.Build.targets`:

```xml
<Project>
  <Import Project="$(MSBuildThisFileDirectory)..\src\Directory.Build.targets" />
</Project>
```

`test/Directory.Packages.props`:

```xml
<Project>
  <Import Project="$(MSBuildThisFileDirectory)..\src\Directory.Packages.props" />
</Project>
```

```bash
dotnet new sln --name Cake.Download.Module --output src --format slnx
dotnet sln src/Cake.Download.Module.slnx add src/Cake.Download.Module/Cake.Download.Module.csproj test/Cake.Download.Module.Tests/Cake.Download.Module.Tests.csproj
```

- [ ] **Step 4: Write the failing tests**

`test/Cake.Download.Module.Tests/Platforms/PlatformInfoTests.cs`:

```csharp
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
```

`test/Cake.Download.Module.Tests/Platforms/PlatformDetectorTests.cs`:

```csharp
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
```

- [ ] **Step 5: Run the tests to verify they fail**

Run: `dotnet test --project test/Cake.Download.Module.Tests/Cake.Download.Module.Tests.csproj --filter-class "*Platform*Tests"`
Expected: build fails with `CS0246: The type or namespace name 'PlatformInfo' could not be found` (and `PlatformDetector`).

- [ ] **Step 6: Implement the platform model**

`src/Cake.Download.Module/Platforms/PlatformInfo.cs`:

```csharp
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
```

`src/Cake.Download.Module/Platforms/PlatformDetector.cs`:

```csharp
using System.Runtime.InteropServices;
using Cake.Core;

namespace Cake.Download.Module.Platforms;

internal interface IPlatformDetector
{
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
```

- [ ] **Step 7: Run the tests to verify they pass, then build Release**

Run: `dotnet test --project test/Cake.Download.Module.Tests/Cake.Download.Module.Tests.csproj --filter-class "*Platform*Tests"`
Expected: all tests in `PlatformInfoTests` and `PlatformDetectorTests` pass.

Run: `dotnet build src/Cake.Download.Module.slnx -c Release`
Expected: `Build succeeded` with 0 warnings. Fix any StyleCop findings before continuing.

- [ ] **Step 8: Commit**

```bash
git add global.json icon.png build/MinVer.props src/ test/
git commit -F - <<'EOF'
Scaffold solution and add platform detection

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 2: Platform dialects and placeholder expansion

**Files:**
- Create: `src/Cake.Download.Module/Platforms/PlatformDialect.cs`
- Create: `src/Cake.Download.Module/Directives/PlaceholderExpander.cs`
- Test: `test/Cake.Download.Module.Tests/Platforms/PlatformDialectTests.cs`, `test/Cake.Download.Module.Tests/Directives/PlaceholderExpanderTests.cs`

**Interfaces:**
- Consumes: `PlatformInfo`, `OsFamily`, `CpuArchitecture` (Task 1).
- Produces:
  - `sealed class PlatformDialect`:
    - `string Name`, `bool HasTriples`
    - `IReadOnlyCollection<string> OsValues`, `IReadOnlyCollection<string> ArchValues`
    - `string GetOs(OsFamily)`, `string GetArch(CpuArchitecture)`, `string GetTriple(PlatformInfo)`
  - `static class PlatformDialects`: `Go`, `DotNet`, `Rust`, `IReadOnlyList<PlatformDialect> All`, `PlatformDialect? Find(string name)`.
  - `static class PlaceholderExpander` (namespace `Cake.Download.Module.Directives`):
    - `string Expand(string template, IReadOnlyDictionary<string, string> values, string context)`
    - `bool ContainsAny(string template, IEnumerable<string> names)`

- [ ] **Step 1: Write the failing tests**

`test/Cake.Download.Module.Tests/Platforms/PlatformDialectTests.cs`:

```csharp
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
```

`test/Cake.Download.Module.Tests/Directives/PlaceholderExpanderTests.cs`:

```csharp
using Cake.Core;
using Cake.Download.Module.Directives;

namespace Cake.Download.Module.Tests.Directives;

public sealed class PlaceholderExpanderTests
{
    private static readonly Dictionary<string, string> Values = new()
    {
        ["version"] = "1.8.2",
        ["os"] = "linux",
        ["exe"] = string.Empty,
    };

    [Fact]
    public void Expand_Replaces_Every_Placeholder()
    {
        Assert.Equal(
            "https://example.com/jq-1.8.2/jq-linux",
            PlaceholderExpander.Expand("https://example.com/jq-{version}/jq-{os}{exe}", Values, "the download URL"));
    }

    [Fact]
    public void Expand_Leaves_Text_Without_Placeholders_Untouched()
    {
        Assert.Equal("a%2Bb/c", PlaceholderExpander.Expand("a%2Bb/c", Values, "the download URL"));
    }

    [Fact]
    public void Expand_Rejects_Unknown_Placeholders_And_Lists_The_Available_Ones()
    {
        var exception = Assert.Throws<CakeException>(
            () => PlaceholderExpander.Expand("tool-{platform}", Values, "the download URL"));

        Assert.Equal(
            "Unknown placeholder '{platform}' in the download URL. Available placeholders: {version}, {os}, {exe}.",
            exception.Message);
    }

    [Fact]
    public void ContainsAny_Detects_Named_Placeholders()
    {
        Assert.True(PlaceholderExpander.ContainsAny("tool-{os}.zip", ["os", "arch"]));
        Assert.False(PlaceholderExpander.ContainsAny("tool-{version}.zip", ["os", "arch"]));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project test/Cake.Download.Module.Tests/Cake.Download.Module.Tests.csproj --filter-class "*PlatformDialectTests"`
Expected: build fails with `CS0103: The name 'PlatformDialects' does not exist` and `CS0103 ... 'PlaceholderExpander'`.

- [ ] **Step 3: Implement the dialects and the expander**

`src/Cake.Download.Module/Platforms/PlatformDialect.cs`:

```csharp
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
```

`src/Cake.Download.Module/Directives/PlaceholderExpander.cs`:

```csharp
using System.Text.RegularExpressions;
using Cake.Core;

namespace Cake.Download.Module.Directives;

/// <summary>
/// Expands <c>{name}</c> placeholders. Unknown placeholders are errors, so a typo never reaches the server.
/// </summary>
internal static partial class PlaceholderExpander
{
    public static string Expand(string template, IReadOnlyDictionary<string, string> values, string context)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);

        return Placeholder().Replace(template, match =>
        {
            var name = match.Groups["name"].Value;
            if (values.TryGetValue(name, out var value))
            {
                return value;
            }

            var available = string.Join(", ", values.Keys.Select(key => "{" + key + "}"));
            throw new CakeException($"Unknown placeholder '{{{name}}}' in {context}. Available placeholders: {available}.");
        });
    }

    public static bool ContainsAny(string template, IEnumerable<string> names)
    {
        var used = Placeholder().Matches(template).Select(match => match.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);
        return names.Any(used.Contains);
    }

    [GeneratedRegex(@"\{(?<name>[^{}]*)\}")]
    private static partial Regex Placeholder();
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --project test/Cake.Download.Module.Tests/Cake.Download.Module.Tests.csproj --filter-class "*PlatformDialectTests"` and again with `--filter-class "*PlaceholderExpanderTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ test/
git commit -F - <<'EOF'
Add go, dotnet and rust platform dialects and placeholder expansion

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 3: Directive parser

**Files:**
- Create: `src/Cake.Download.Module/Directives/DownloadDirective.cs`, `src/Cake.Download.Module/Directives/DirectiveParser.cs`
- Test: `test/Cake.Download.Module.Tests/Directives/DirectiveParserTests.cs`

**Interfaces:**
- Consumes: `PlatformInfo.IsSupportedRid`, `PlatformInfo.SupportedRids` (Task 1); `PlatformDialects.Find`, `PlatformDialect.OsValues/ArchValues/HasTriples/Name` (Task 2); `Cake.Core.Packaging.PackageReference`.
- Produces (namespace `Cake.Download.Module.Directives`):
  - `sealed record DownloadDirective`, all `init`:
    - `required string OriginalString`, `required string Package`, `required string Version`
    - `string? UrlTemplate`, `string? Url`
    - `IReadOnlyDictionary<string,string> UrlByRid`: lower-case RID keys
    - `string Dialect`: canonical lower-case name, default `"go"`
    - `IReadOnlyDictionary<string,string> OsOverrides`, `ArchOverrides`, `ArchiveOverrides`: case-insensitive keys
    - `IReadOnlyDictionary<string,string> TripleOverrides`: lower-case RID keys
    - `string? Format`: `file|zip|tar|tar.gz`
    - `string? FileName`
    - `IReadOnlyList<string> Include`, `Exclude`
    - `required IntegritySpec Integrity`
  - `abstract record IntegritySpec` with subtypes:
    - `Sha256Integrity(string Sha256)`
    - `PerRidSha256Integrity(IReadOnlyDictionary<string,string> Sha256ByRid)`
    - `ChecksumsFileIntegrity(string Reference, string? Sha256)`: a null `Sha256` means unpinned
    - `SkipIntegrity()`, `MissingIntegrity()`

    All hashes are lower-case hex.
  - `static class DirectiveParser`: `const string Scheme = "download"`; `DownloadDirective Parse(PackageReference reference)`.
  - Every validation error is `CakeException("Invalid download directive '<original>': <problem>")`.

- [ ] **Step 1: Write the failing tests**

`test/Cake.Download.Module.Tests/Directives/DirectiveParserTests.cs`:

```csharp
using Cake.Core;
using Cake.Core.Packaging;
using Cake.Download.Module.Directives;

namespace Cake.Download.Module.Tests.Directives;

public sealed class DirectiveParserTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string Base = "download:https://example.com/tool-{version}-{os}-{arch}{exe}?package=tool&version=1.2.3";

    [Fact]
    public void Parse_Reads_The_Template_From_The_Original_String_With_Placeholders_Intact()
    {
        var directive = Parse(Base + "&sha256=" + Hash);

        Assert.Equal("https://example.com/tool-{version}-{os}-{arch}{exe}", directive.UrlTemplate);
        Assert.Equal("tool", directive.Package);
        Assert.Equal("1.2.3", directive.Version);
        Assert.Equal("go", directive.Dialect);
        Assert.Null(directive.Url);
        Assert.Equal(new Sha256Integrity(Hash), directive.Integrity);
    }

    [Fact]
    public void Parse_Accepts_Upper_Case_Scheme_And_Parameter_Keys()
    {
        var directive = Parse("DOWNLOAD:https://example.com/tool.zip?Package=tool&Version=1.0&SHA256=" + Hash.ToUpperInvariant() + "&Dialect=DotNet");

        Assert.Equal("tool", directive.Package);
        Assert.Equal("dotnet", directive.Dialect);
        Assert.Equal(new Sha256Integrity(Hash), directive.Integrity);
    }

    [Fact]
    public void Parse_Preserves_Plus_In_The_Version()
    {
        Assert.Equal("1.2.3+build.5", Parse("download:https://example.com/t.zip?package=t&version=1.2.3+build.5&sha256=skip").Version);
    }

    [Theory]
    [InlineData("download:https://example.com/t.zip?package=t&sha256=skip", "the 'version' parameter is required.")]
    [InlineData("download:https://example.com/t.zip?package=t&version=latest&sha256=skip", "'version=latest' is not supported; pin an exact version.")]
    [InlineData("download:https://example.com/t.zip?package=t&version=1%2F2&sha256=skip", "'version' may only contain letters, digits, '.', '_', '+' and '-' (was '1/2').")]
    [InlineData("download:https://example.com/t.zip?package=a%2Fb&version=1&sha256=skip", "'package' may only contain letters, digits, '.', '_' and '-' (was 'a/b').")]
    [InlineData("download:https://example.com/t.zip?package=t&version=1&sha256=skip&token=x", "unknown parameter 'token'.")]
    [InlineData("download:https://example.com/t.zip?package=t&version=1&sha256=skip&dialect=go&dialect=rust", "parameter 'dialect' may only be specified once.")]
    [InlineData("download:https://example.com/t.zip?package=t&version=1&sha256=", "parameter 'sha256' needs a value.")]
    [InlineData("download:https://example.com/t.zip?package=t&version=1&sha256=skip&dialect=python", "unknown dialect 'python'. Supported dialects: go, dotnet, rust.")]
    [InlineData("download:https://example.com/t.zip?package=t&version=1&sha256=skip&url=https%3A%2F%2Fexample.com%2Fother.zip", "specify the download URL either after 'download:' or with 'url=', not both.")]
    [InlineData("download:?package=t&version=1&sha256=skip", "no download URL. Put the URL after 'download:' or use 'url=' or 'url.<rid>='.")]
    [InlineData("download:https://example.com/t?package=t&version=1&sha256=skip&format=7z", "unknown format '7z'. Supported formats: file, zip, tar, tar.gz.")]
    [InlineData("download:https://example.com/t?package=t&version=1&sha256=skip&filename=bin%2Ft", "'filename' must be a file name, not a path (was 'bin/t').")]
    [InlineData("download:https://example.com/t?package=t&version=1&sha256=skip&triple.linux-x64=x", "'triple.<rid>' parameters require 'dialect=rust'.")]
    [InlineData("download:https://example.com/t?package=t&version=1&sha256=skip&url.freebsd-x64=https%3A%2F%2Fexample.com%2Ft", "'url.freebsd-x64' does not name a supported platform. Supported platforms: win-x64, win-x86, win-arm64, linux-x64, linux-arm64, linux-arm, osx-x64, osx-arm64.")]
    [InlineData("download:https://example.com/t?package=t&version=1&sha256=skip&os.macos=x", "'os.macos' is not a valid override for dialect 'go'. Use one of: os.windows, os.linux, os.darwin.")]
    [InlineData("download:https://example.com/t?package=t&version=1&sha256=skip&arch.x64=x", "'arch.x64' is not a valid override for dialect 'go'. Use one of: arch.amd64, arch.arm64, arch.386, arch.arm.")]
    [InlineData("download:https://example.com/t?package=t&version=1&sha256=skip&archive.osx=zip", "'archive.osx' is not a valid override for dialect 'go'. Use one of: archive.windows, archive.linux, archive.darwin.")]
    public void Parse_Rejects_Invalid_Directives(string uri, string problem)
    {
        var exception = Assert.Throws<CakeException>(() => Parse(uri));

        Assert.StartsWith($"Invalid download directive '{uri}': ", exception.Message);
        Assert.Contains(problem, exception.Message);
    }

    [Fact]
    public void Parse_Lists_The_Known_Parameters_For_An_Unknown_One()
    {
        var exception = Assert.Throws<CakeException>(() => Parse("download:https://example.com/t.zip?package=t&version=1&sha256=skip&token=x"));

        Assert.Contains("Known parameters: package, version, sha256, sha256.<rid>, checksums, checksums_sha256, dialect, os.<os>, arch.<arch>, archive.<os>, triple.<rid>, url, url.<rid>, format, filename, include, exclude.", exception.Message);
    }

    [Fact]
    public void Parse_Reads_Overrides_And_Rid_Maps()
    {
        var directive = Parse(
            "download:https://example.com/rg-{triple}.{archive}?package=rg&version=14.1.1&dialect=rust&sha256=skip" +
            "&os.darwin=macos&arch.x86_64=amd64&archive.windows=7z.zip&triple.LINUX-ARM64=aarch64-unknown-linux-gnu" +
            "&url.win-x64=https%3A%2F%2Fexample.com%2Fwin.zip");

        Assert.Equal("rust", directive.Dialect);
        Assert.Equal("macos", directive.OsOverrides["DARWIN"]);
        Assert.Equal("amd64", directive.ArchOverrides["x86_64"]);
        Assert.Equal("7z.zip", directive.ArchiveOverrides["windows"]);
        Assert.Equal("aarch64-unknown-linux-gnu", directive.TripleOverrides["linux-arm64"]);
        Assert.Equal("https://example.com/win.zip", directive.UrlByRid["win-x64"]);
    }

    [Fact]
    public void Parse_Decodes_A_Percent_Encoded_Url_Parameter()
    {
        var directive = Parse("download:?package=t&version=1&sha256=skip&url=https%3A%2F%2Fexample.com%2Fa.zip%3Fsig%3Dabc%26x%3D1");

        Assert.Null(directive.UrlTemplate);
        Assert.Equal("https://example.com/a.zip?sig=abc&x=1", directive.Url);
    }

    [Fact]
    public void Parse_Accepts_Only_Url_For_Rid_Without_A_Default_Url()
    {
        var directive = Parse("download:?package=t&version=1&sha256=skip&url.linux-x64=https%3A%2F%2Fexample.com%2Ft");

        Assert.Equal("https://example.com/t", directive.UrlByRid["linux-x64"]);
    }

    [Fact]
    public void Parse_Reads_Format_Filename_Include_And_Exclude()
    {
        var directive = Parse(Base + "&sha256=skip&format=TGZ&include=**%2Fbin%2F*&include=**%2Ftool&exclude=**%2F*.txt");

        Assert.Equal("tar.gz", directive.Format);
        Assert.Equal(["**/bin/*", "**/tool"], directive.Include);
        Assert.Equal(["**/*.txt"], directive.Exclude);

        Assert.Equal("tool.jar", Parse(Base + "&sha256=skip&filename=tool.jar").FileName);
    }

    [Fact]
    public void Parse_Reads_Skip()
    {
        Assert.IsType<SkipIntegrity>(Parse(Base + "&sha256=SKIP").Integrity);
    }

    [Fact]
    public void Parse_Reads_Per_Rid_Hashes_With_Lower_Case_Keys_And_Values()
    {
        var integrity = Assert.IsType<PerRidSha256Integrity>(
            Parse(Base + "&sha256.WIN-X64=" + Hash.ToUpperInvariant() + "&sha256.linux-x64=" + Hash).Integrity);

        Assert.Equal(Hash, integrity.Sha256ByRid["win-x64"]);
        Assert.Equal(Hash, integrity.Sha256ByRid["linux-x64"]);
    }

    [Fact]
    public void Parse_Reads_Pinned_And_Unpinned_Checksums_Files()
    {
        Assert.Equal(
            new ChecksumsFileIntegrity("sha256sum.txt", Hash),
            Parse(Base + "&checksums=sha256sum.txt&checksums_sha256=" + Hash).Integrity);
        Assert.Equal(
            new ChecksumsFileIntegrity("tool-{version}.sha256", null),
            Parse(Base + "&checksums=tool-%7Bversion%7D.sha256").Integrity);
    }

    [Fact]
    public void Parse_Reports_Missing_Integrity_As_A_Value_Not_An_Error()
    {
        Assert.IsType<MissingIntegrity>(Parse(Base).Integrity);
    }

    [Theory]
    [InlineData("&sha256=" + Hash + "&sha256.win-x64=" + Hash)]
    [InlineData("&sha256=skip&checksums=sums.txt&checksums_sha256=" + Hash)]
    [InlineData("&sha256.win-x64=" + Hash + "&checksums=sums.txt")]
    public void Parse_Rejects_More_Than_One_Integrity_Option(string query)
    {
        var exception = Assert.Throws<CakeException>(() => Parse(Base + query));

        Assert.Contains("specify only one integrity option: 'sha256=', 'sha256.<rid>=', 'checksums=' with 'checksums_sha256=', or 'sha256=skip'.", exception.Message);
    }

    [Theory]
    [InlineData("&checksums_sha256=" + Hash, "'checksums_sha256' requires 'checksums'.")]
    [InlineData("&sha256=abc", "'sha256' must be a SHA-256 hash of 64 hexadecimal characters, or 'skip'.")]
    [InlineData("&sha256.win-x64=abc", "'sha256.win-x64' must be a SHA-256 hash of 64 hexadecimal characters.")]
    [InlineData("&checksums=s.txt&checksums_sha256=xyz", "'checksums_sha256' must be a SHA-256 hash of 64 hexadecimal characters.")]
    public void Parse_Rejects_Invalid_Integrity_Values(string query, string problem)
    {
        var exception = Assert.Throws<CakeException>(() => Parse(Base + query));

        Assert.Contains(problem, exception.Message);
    }

    private static DownloadDirective Parse(string uri) => DirectiveParser.Parse(new PackageReference(uri));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project test/Cake.Download.Module.Tests/Cake.Download.Module.Tests.csproj --filter-class "*DirectiveParserTests"`
Expected: build fails with `CS0246: The type or namespace name 'DownloadDirective' could not be found`.

- [ ] **Step 3: Implement the directive model**

`src/Cake.Download.Module/Directives/DownloadDirective.cs`:

```csharp
namespace Cake.Download.Module.Directives;

/// <summary>
/// A validated <c>download:</c> directive. Templates are kept unexpanded; <see cref="DownloadPlanner"/> expands them.
/// </summary>
internal sealed record DownloadDirective
{
    public required string OriginalString { get; init; }

    public required string Package { get; init; }

    public required string Version { get; init; }

    public string? UrlTemplate { get; init; }

    public string? Url { get; init; }

    public IReadOnlyDictionary<string, string> UrlByRid { get; init; } = Empty();

    public string Dialect { get; init; } = "go";

    public IReadOnlyDictionary<string, string> OsOverrides { get; init; } = Empty();

    public IReadOnlyDictionary<string, string> ArchOverrides { get; init; } = Empty();

    public IReadOnlyDictionary<string, string> ArchiveOverrides { get; init; } = Empty();

    public IReadOnlyDictionary<string, string> TripleOverrides { get; init; } = Empty();

    public string? Format { get; init; }

    public string? FileName { get; init; }

    public IReadOnlyList<string> Include { get; init; } = [];

    public IReadOnlyList<string> Exclude { get; init; } = [];

    public required IntegritySpec Integrity { get; init; }

    private static Dictionary<string, string> Empty() => new(StringComparer.OrdinalIgnoreCase);
}

internal abstract record IntegritySpec;

internal sealed record Sha256Integrity(string Sha256) : IntegritySpec;

internal sealed record PerRidSha256Integrity(IReadOnlyDictionary<string, string> Sha256ByRid) : IntegritySpec;

internal sealed record ChecksumsFileIntegrity(string Reference, string? Sha256) : IntegritySpec;

internal sealed record SkipIntegrity : IntegritySpec;

internal sealed record MissingIntegrity : IntegritySpec;
```

- [ ] **Step 4: Implement the parser**

`src/Cake.Download.Module/Directives/DirectiveParser.cs`:

```csharp
using System.Text.RegularExpressions;
using Cake.Core;
using Cake.Core.Packaging;
using Cake.Download.Module.Platforms;

namespace Cake.Download.Module.Directives;

/// <summary>
/// Parses and validates a <c>download:</c> <see cref="PackageReference"/>. Cake's query parsing is case-insensitive
/// and does not decode values, so values are percent-decoded here, and the URL template is read from
/// <see cref="PackageReference.OriginalString"/> because <see cref="PackageReference.Address"/> escapes <c>{}</c>.
/// </summary>
internal static partial class DirectiveParser
{
    public const string Scheme = "download";

    private const string KnownParameters =
        "package, version, sha256, sha256.<rid>, checksums, checksums_sha256, dialect, os.<os>, arch.<arch>, " +
        "archive.<os>, triple.<rid>, url, url.<rid>, format, filename, include, exclude";

    private static readonly string[] SingleValued = ["package", "version", "sha256", "checksums", "checksums_sha256", "dialect", "url", "format", "filename"];
    private static readonly string[] MultiValued = ["include", "exclude"];
    private static readonly string[] Prefixes = ["sha256.", "url.", "os.", "arch.", "archive.", "triple."];
    private static readonly string[] Formats = ["file", "zip", "tar", "tar.gz"];

    public static DownloadDirective Parse(PackageReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        var original = reference.OriginalString;
        CakeException Fail(string problem) => new($"Invalid download directive '{original}': {problem}");

        if (!original.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase))
        {
            throw Fail($"it must start with '{Scheme}:'.");
        }

        var parameters = ReadParameters(reference.Parameters, Fail);
        string? Single(string key) => parameters.TryGetValue(key, out var values) ? values[0] : null;

        var package = Single("package") ?? throw Fail("the 'package' parameter is required.");
        if (!PackagePattern().IsMatch(package))
        {
            throw Fail($"'package' may only contain letters, digits, '.', '_' and '-' (was '{package}').");
        }

        var version = Single("version") ?? throw Fail("the 'version' parameter is required.");
        if (string.Equals(version, "latest", StringComparison.OrdinalIgnoreCase))
        {
            throw Fail("'version=latest' is not supported; pin an exact version.");
        }

        if (!VersionPattern().IsMatch(version))
        {
            throw Fail($"'version' may only contain letters, digits, '.', '_', '+' and '-' (was '{version}').");
        }

        var dialectName = Single("dialect") ?? PlatformDialects.Go.Name;
        var dialect = PlatformDialects.Find(dialectName)
            ?? throw Fail($"unknown dialect '{dialectName}'. Supported dialects: {string.Join(", ", PlatformDialects.All.Select(d => d.Name))}.");

        var osOverrides = Prefixed(parameters, "os.", ridKeys: false);
        CheckValues(osOverrides.Keys, dialect.OsValues, "os.", dialect, Fail);
        var archOverrides = Prefixed(parameters, "arch.", ridKeys: false);
        CheckValues(archOverrides.Keys, dialect.ArchValues, "arch.", dialect, Fail);
        var archiveOverrides = Prefixed(parameters, "archive.", ridKeys: false);
        CheckValues(archiveOverrides.Keys, dialect.OsValues, "archive.", dialect, Fail);

        var tripleOverrides = Prefixed(parameters, "triple.", ridKeys: true);
        if (tripleOverrides.Count > 0 && !dialect.HasTriples)
        {
            throw Fail("'triple.<rid>' parameters require 'dialect=rust'.");
        }

        CheckRids(tripleOverrides.Keys, "triple.", Fail);
        var urlByRid = Prefixed(parameters, "url.", ridKeys: true);
        CheckRids(urlByRid.Keys, "url.", Fail);
        var sha256ByRid = Prefixed(parameters, "sha256.", ridKeys: true);
        CheckRids(sha256ByRid.Keys, "sha256.", Fail);

        var template = ReadTemplate(original);
        var url = Single("url");
        if (template is not null && url is not null)
        {
            throw Fail("specify the download URL either after 'download:' or with 'url=', not both.");
        }

        if (template is null && url is null && urlByRid.Count == 0)
        {
            throw Fail("no download URL. Put the URL after 'download:' or use 'url=' or 'url.<rid>='.");
        }

        var format = Single("format")?.ToLowerInvariant();
        if (format == "tgz")
        {
            format = "tar.gz";
        }

        if (format is not null && !Formats.Contains(format))
        {
            throw Fail($"unknown format '{format}'. Supported formats: {string.Join(", ", Formats)}.");
        }

        var fileName = Single("filename");
        if (fileName is not null && (fileName.IndexOfAny(['/', '\\']) >= 0 || fileName is "." or ".."))
        {
            throw Fail($"'filename' must be a file name, not a path (was '{fileName}').");
        }

        return new DownloadDirective
        {
            OriginalString = original,
            Package = package,
            Version = version,
            UrlTemplate = template,
            Url = url,
            UrlByRid = urlByRid,
            Dialect = dialect.Name,
            OsOverrides = osOverrides,
            ArchOverrides = archOverrides,
            ArchiveOverrides = archiveOverrides,
            TripleOverrides = tripleOverrides,
            Format = format,
            FileName = fileName,
            Include = parameters.TryGetValue("include", out var include) ? include : [],
            Exclude = parameters.TryGetValue("exclude", out var exclude) ? exclude : [],
            Integrity = ParseIntegrity(Single("sha256"), sha256ByRid, Single("checksums"), Single("checksums_sha256"), Fail),
        };
    }

    private static Dictionary<string, IReadOnlyList<string>> ReadParameters(
        IReadOnlyDictionary<string, IReadOnlyList<string>> raw,
        Func<string, CakeException> fail)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, values) in raw)
        {
            var isMulti = MultiValued.Contains(key, StringComparer.OrdinalIgnoreCase);
            var isKnown = isMulti
                || SingleValued.Contains(key, StringComparer.OrdinalIgnoreCase)
                || Prefixes.Any(prefix => key.Length > prefix.Length && key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (!isKnown)
            {
                throw fail($"unknown parameter '{key}'. Known parameters: {KnownParameters}.");
            }

            var decoded = values.Select(value => Uri.UnescapeDataString(value)).ToList();
            if (decoded.Count == 0 || decoded.Any(string.IsNullOrWhiteSpace))
            {
                throw fail($"parameter '{key}' needs a value.");
            }

            if (!isMulti && decoded.Count > 1)
            {
                throw fail($"parameter '{key}' may only be specified once.");
            }

            result[key] = decoded;
        }

        return result;
    }

    private static string? ReadTemplate(string original)
    {
        var rest = original[(Scheme.Length + 1)..];
        var query = rest.IndexOf('?', StringComparison.Ordinal);
        var template = query < 0 ? rest : rest[..query];
        return template.Length == 0 ? null : template;
    }

    private static Dictionary<string, string> Prefixed(Dictionary<string, IReadOnlyList<string>> parameters, string prefix, bool ridKeys)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, values) in parameters)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var suffix = key[prefix.Length..];
                result[ridKeys ? suffix.ToLowerInvariant() : suffix] = values[0];
            }
        }

        return result;
    }

    private static void CheckRids(IEnumerable<string> rids, string prefix, Func<string, CakeException> fail)
    {
        foreach (var rid in rids)
        {
            if (!PlatformInfo.IsSupportedRid(rid))
            {
                throw fail($"'{prefix}{rid}' does not name a supported platform. Supported platforms: {string.Join(", ", PlatformInfo.SupportedRids)}.");
            }
        }
    }

    private static void CheckValues(
        IEnumerable<string> keys,
        IReadOnlyCollection<string> allowed,
        string prefix,
        PlatformDialect dialect,
        Func<string, CakeException> fail)
    {
        foreach (var key in keys)
        {
            if (!allowed.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                throw fail($"'{prefix}{key}' is not a valid override for dialect '{dialect.Name}'. Use one of: {string.Join(", ", allowed.Select(value => prefix + value))}.");
            }
        }
    }

    private static IntegritySpec ParseIntegrity(
        string? sha256,
        Dictionary<string, string> sha256ByRid,
        string? checksums,
        string? checksumsSha256,
        Func<string, CakeException> fail)
    {
        var modes = (sha256 is null ? 0 : 1) + (sha256ByRid.Count == 0 ? 0 : 1) + (checksums is null ? 0 : 1);
        if (modes > 1 || (sha256 is not null && checksumsSha256 is not null))
        {
            throw fail("specify only one integrity option: 'sha256=', 'sha256.<rid>=', 'checksums=' with 'checksums_sha256=', or 'sha256=skip'.");
        }

        if (checksumsSha256 is not null && checksums is null)
        {
            throw fail("'checksums_sha256' requires 'checksums'.");
        }

        if (sha256 is not null)
        {
            if (string.Equals(sha256, "skip", StringComparison.OrdinalIgnoreCase))
            {
                return new SkipIntegrity();
            }

            return new Sha256Integrity(Hex(sha256, "'sha256' must be a SHA-256 hash of 64 hexadecimal characters, or 'skip'.", fail));
        }

        if (sha256ByRid.Count > 0)
        {
            return new PerRidSha256Integrity(sha256ByRid.ToDictionary(
                entry => entry.Key,
                entry => Hex(entry.Value, $"'sha256.{entry.Key}' must be a SHA-256 hash of 64 hexadecimal characters.", fail),
                StringComparer.OrdinalIgnoreCase));
        }

        if (checksums is not null)
        {
            var pin = checksumsSha256 is null
                ? null
                : Hex(checksumsSha256, "'checksums_sha256' must be a SHA-256 hash of 64 hexadecimal characters.", fail);
            return new ChecksumsFileIntegrity(checksums, pin);
        }

        return new MissingIntegrity();
    }

    private static string Hex(string value, string problem, Func<string, CakeException> fail) =>
        Sha256Pattern().IsMatch(value) ? value.ToLowerInvariant() : throw fail(problem);

    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex PackagePattern();

    [GeneratedRegex("^[A-Za-z0-9._+-]+$")]
    private static partial Regex VersionPattern();

    [GeneratedRegex("^[0-9a-fA-F]{64}$")]
    private static partial Regex Sha256Pattern();
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --project test/Cake.Download.Module.Tests/Cake.Download.Module.Tests.csproj --filter-class "*DirectiveParserTests"`
Expected: PASS.

If `Parse_Decodes_A_Percent_Encoded_Url_Parameter` or `Parse_Reads_The_Template_From_The_Original_String_With_Placeholders_Intact` fails because `System.Uri` changes the query or the braces, **stop and report**. These tests pin the directive grammar's contract with Cake's parser (spec §4.1).

- [ ] **Step 6: Commit**

```bash
git add src/ test/
git commit -F - <<'EOF'
Parse and validate download: directives

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 4: Download planner

**Files:**
- Create: `src/Cake.Download.Module/Directives/DownloadPlan.cs`, `src/Cake.Download.Module/Directives/DownloadPlanner.cs`
- Test: `test/Cake.Download.Module.Tests/Directives/DownloadPlannerTests.cs`

**Interfaces:**
- Consumes: `DownloadDirective` and the `IntegritySpec` subtypes (Task 3); `PlaceholderExpander` and `PlatformDialects` (Task 2); `PlatformInfo` (Task 1).
- Produces (namespace `Cake.Download.Module.Directives`):
  - `enum ArchiveFormat { File, Zip, Tar, TarGz }`.
  - `static class ArchiveFormats`: `string ToName(ArchiveFormat)` (`file|zip|tar|tar.gz`), `ArchiveFormat FromName(string)`, `ArchiveFormat Detect(Uri)`.
  - `sealed record DownloadPlan`, all `init`:
    - `required string Package`, `Version`, `Dialect`
    - `required PlatformInfo Platform`
    - `required IReadOnlyDictionary<string,string> Placeholders`
    - `required Uri Url`
    - `required ArchiveFormat Format`
    - `string? FileName`: set only when `Format == File`
    - `required IReadOnlyList<string> Include`, `Exclude`
    - `required IntegrityPlan Integrity`
    - computed: `string FolderName` (`<package>.<version>`), `string AssetName` (decoded last URL segment)
  - `abstract record IntegrityPlan { abstract string Fingerprint }` with subtypes:
    - `PinnedSha256(string Sha256, string Parameter)` → fingerprint `sha256:<hex>`
    - `ChecksumsFilePlan(Uri Url, string? Sha256)` → `checksums:<url>#<hex>`
    - `SkippedIntegrity()` → `skip`
    - `MissingIntegrityPlan(string Parameter)` → `missing`
  - `static class DownloadPlanner`: `DownloadPlan Create(DownloadDirective directive, PlatformInfo platform)`.

- [ ] **Step 1: Write the failing tests**

`test/Cake.Download.Module.Tests/Directives/DownloadPlannerTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project test/Cake.Download.Module.Tests/Cake.Download.Module.Tests.csproj --filter-class "*DownloadPlannerTests"`
Expected: build fails with `CS0246: The type or namespace name 'DownloadPlan' could not be found`.

- [ ] **Step 3: Implement the plan model**

`src/Cake.Download.Module/Directives/DownloadPlan.cs`:

```csharp
using Cake.Download.Module.Platforms;

namespace Cake.Download.Module.Directives;

internal enum ArchiveFormat
{
    File,
    Zip,
    Tar,
    TarGz,
}

internal static class ArchiveFormats
{
    public static string ToName(ArchiveFormat format) => format switch
    {
        ArchiveFormat.File => "file",
        ArchiveFormat.Zip => "zip",
        ArchiveFormat.Tar => "tar",
        _ => "tar.gz",
    };

    public static ArchiveFormat FromName(string name) => name switch
    {
        "file" => ArchiveFormat.File,
        "zip" => ArchiveFormat.Zip,
        "tar" => ArchiveFormat.Tar,
        "tar.gz" => ArchiveFormat.TarGz,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown archive format."),
    };

    public static ArchiveFormat Detect(Uri url)
    {
        var path = url.AbsolutePath;
        if (path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            return ArchiveFormat.TarGz;
        }

        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return ArchiveFormat.Zip;
        }

        return path.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) ? ArchiveFormat.Tar : ArchiveFormat.File;
    }
}

/// <summary>
/// A directive resolved for one platform: every template expanded, the expected integrity chosen.
/// </summary>
internal sealed record DownloadPlan
{
    public required string Package { get; init; }

    public required string Version { get; init; }

    public required PlatformInfo Platform { get; init; }

    public required string Dialect { get; init; }

    public required IReadOnlyDictionary<string, string> Placeholders { get; init; }

    public required Uri Url { get; init; }

    public required ArchiveFormat Format { get; init; }

    public string? FileName { get; init; }

    public required IReadOnlyList<string> Include { get; init; }

    public required IReadOnlyList<string> Exclude { get; init; }

    public required IntegrityPlan Integrity { get; init; }

    public string FolderName => $"{Package}.{Version}";

    public string AssetName => Uri.UnescapeDataString(Url.Segments[^1]);
}

internal abstract record IntegrityPlan
{
    public abstract string Fingerprint { get; }
}

internal sealed record PinnedSha256(string Sha256, string Parameter) : IntegrityPlan
{
    public override string Fingerprint => "sha256:" + Sha256;
}

internal sealed record ChecksumsFilePlan(Uri Url, string? Sha256) : IntegrityPlan
{
    public override string Fingerprint => $"checksums:{Url.AbsoluteUri}#{Sha256}";
}

internal sealed record SkippedIntegrity : IntegrityPlan
{
    public override string Fingerprint => "skip";
}

internal sealed record MissingIntegrityPlan(string Parameter) : IntegrityPlan
{
    public override string Fingerprint => "missing";
}
```

- [ ] **Step 4: Implement the planner**

`src/Cake.Download.Module/Directives/DownloadPlanner.cs`:

```csharp
using Cake.Core;
using Cake.Download.Module.Platforms;

namespace Cake.Download.Module.Directives;

internal static class DownloadPlanner
{
    private static readonly string[] PlatformPlaceholders = ["os", "arch", "rid", "exe", "archive", "triple"];

    public static DownloadPlan Create(DownloadDirective directive, PlatformInfo platform)
    {
        ArgumentNullException.ThrowIfNull(directive);
        ArgumentNullException.ThrowIfNull(platform);

        var dialect = PlatformDialects.Find(directive.Dialect)
            ?? throw new InvalidOperationException($"Unknown dialect '{directive.Dialect}'.");
        var rid = platform.Rid;
        var osDefault = dialect.GetOs(platform.Os);
        var archDefault = dialect.GetArch(platform.Cpu);

        var placeholders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["version"] = directive.Version,
            ["os"] = directive.OsOverrides.GetValueOrDefault(osDefault, osDefault),
            ["arch"] = directive.ArchOverrides.GetValueOrDefault(archDefault, archDefault),
            ["rid"] = rid,
            ["exe"] = platform.Exe,
            ["archive"] = directive.ArchiveOverrides.GetValueOrDefault(osDefault, platform.IsWindows ? "zip" : "tar.gz"),
        };
        if (dialect.HasTriples)
        {
            placeholders["triple"] = directive.TripleOverrides.GetValueOrDefault(rid, dialect.GetTriple(platform));
        }

        var template = directive.UrlByRid.GetValueOrDefault(rid) ?? directive.Url ?? directive.UrlTemplate
            ?? throw new CakeException(
                $"The download directive for '{directive.Package}' has no URL for {rid}. Add 'url.{rid}=<url>' or a default URL. " +
                $"Directive: {directive.OriginalString}");
        var url = ParseHttps(PlaceholderExpander.Expand(template, placeholders, "the download URL"), "download URL", directive);
        var format = directive.Format is null ? ArchiveFormats.Detect(url) : ArchiveFormats.FromName(directive.Format);

        string? fileName = null;
        if (format == ArchiveFormat.File)
        {
            fileName = directive.FileName is null
                ? directive.Package + platform.Exe
                : PlaceholderExpander.Expand(directive.FileName, placeholders, "'filename'");
            if (fileName.IndexOfAny(['/', '\\']) >= 0)
            {
                throw new CakeException($"'filename' must be a file name, not a path (was '{fileName}'). Directive: {directive.OriginalString}");
            }
        }
        else if (directive.FileName is not null)
        {
            throw new CakeException(
                $"'filename' only applies to raw file downloads, but {url.AbsoluteUri} is a {ArchiveFormats.ToName(format)} archive. " +
                $"Directive: {directive.OriginalString}");
        }

        IReadOnlyList<string> include = directive.Include.Count > 0
            ? directive.Include.Select(pattern => PlaceholderExpander.Expand(pattern, placeholders, "'include'")).ToList()
            : [fileName ?? $"**/{directive.Package}{platform.Exe}"];
        var exclude = directive.Exclude.Select(pattern => PlaceholderExpander.Expand(pattern, placeholders, "'exclude'")).ToList();

        var platformSpecific = directive.UrlByRid.Count > 0 || PlaceholderExpander.ContainsAny(template, PlatformPlaceholders);

        return new DownloadPlan
        {
            Package = directive.Package,
            Version = directive.Version,
            Platform = platform,
            Dialect = dialect.Name,
            Placeholders = placeholders,
            Url = url,
            Format = format,
            FileName = fileName,
            Include = include,
            Exclude = exclude,
            Integrity = PlanIntegrity(directive, rid, url, placeholders, platformSpecific),
        };
    }

    private static IntegrityPlan PlanIntegrity(
        DownloadDirective directive,
        string rid,
        Uri url,
        IReadOnlyDictionary<string, string> placeholders,
        bool platformSpecific) => directive.Integrity switch
        {
            Sha256Integrity pinned => new PinnedSha256(pinned.Sha256, "sha256"),
            PerRidSha256Integrity perRid => perRid.Sha256ByRid.TryGetValue(rid, out var hash)
                ? new PinnedSha256(hash, $"sha256.{rid}")
                : new MissingIntegrityPlan($"sha256.{rid}"),
            ChecksumsFileIntegrity checksums => new ChecksumsFilePlan(
                ParseHttps(new Uri(url, PlaceholderExpander.Expand(checksums.Reference, placeholders, "'checksums'")).AbsoluteUri, "checksums URL", directive),
                checksums.Sha256),
            SkipIntegrity => new SkippedIntegrity(),
            _ => new MissingIntegrityPlan(platformSpecific ? $"sha256.{rid}" : "sha256"),
        };

    private static Uri ParseHttps(string value, string what, DownloadDirective directive)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new CakeException(
                $"The {what} '{value}' for '{directive.Package}' is not an absolute https URL. Only https is supported. " +
                $"Directive: {directive.OriginalString}");
        }

        return uri;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --project test/Cake.Download.Module.Tests/Cake.Download.Module.Tests.csproj --filter-class "*DownloadPlannerTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ test/
git commit -F - <<'EOF'
Resolve directives into per-platform download plans

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 5: HTTP downloader

**Files:**
- Create: `src/Cake.Download.Module/Http/DownloadOptions.cs`, `src/Cake.Download.Module/Http/DownloadResult.cs`, `src/Cake.Download.Module/Http/DownloadHttpException.cs`, `src/Cake.Download.Module/Http/HttpDownloader.cs`
- Create (test helpers): `test/Cake.Download.Module.Tests/Fakes/FakeHttpHandler.cs`, `test/Cake.Download.Module.Tests/Fakes/StallingStream.cs`, `test/Cake.Download.Module.Tests/Fakes/TestDirectory.cs`, `test/Cake.Download.Module.Tests/Fakes/TestHashes.cs`
- Test: `test/Cake.Download.Module.Tests/Http/HttpDownloaderTests.cs`

**Interfaces:**
- Consumes: `Cake.Core.Diagnostics.ICakeLog`.
- Produces (namespace `Cake.Download.Module.Http`):
  - `sealed record DownloadOptions` (all `init`):
    - `TimeSpan StallTimeout` = 60 s
    - `int MaxAttempts` = 3
    - `int MaxRedirects` = 10
    - `TimeSpan MaxRetryAfter` = 30 s
    - `Func<int, TimeSpan> Backoff`: attempt → 2^(attempt-1) s
    - `Func<TimeSpan, CancellationToken, Task> Delay` = `Task.Delay`
    - `static DownloadOptions Default`
  - `sealed record DownloadResult(string Path, string Sha256, long Length)`: `Sha256` is lower-case hex.
  - `sealed class DownloadHttpException : CakeException` with `Uri Url` and `HttpStatusCode StatusCode`. Thrown for non-retryable 4xx.
  - `sealed class HttpDownloader(HttpMessageHandler handler, ICakeLog log, DownloadOptions options)`:
    - `DownloadResult Download(Uri url, string destinationPath, long? maxBytes = null)`
    - follows up to `MaxRedirects` redirects, https only
    - retries `HttpRequestException`, `IOException`, stalls, 408, 429 and 5xx
    - otherwise throws `CakeException`
- Test helpers (namespace `Cake.Download.Module.Tests.Fakes`):
  - `FakeHttpHandler`:
    - `Respond(string url, params Func<HttpResponseMessage>[] responses)`: each request takes the next response, and the last one repeats
    - static `Ok(string|byte[])`, `Status(HttpStatusCode)`, `RedirectTo(string location)`, `Stalling()`
    - `IReadOnlyList<Uri> RequestedUrls`, `IReadOnlyList<string> UserAgents`
    - thread-safe
  - `TestDirectory : IDisposable` with `string Root`, `string Combine(params string[] parts)`.
  - `TestHashes.Sha256(string text)` and `TestHashes.Sha256(byte[] bytes)`.

- [ ] **Step 1: Write the test helpers**

`test/Cake.Download.Module.Tests/Fakes/FakeHttpHandler.cs`:

```csharp
using System.Net;
using System.Text;

namespace Cake.Download.Module.Tests.Fakes;

internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Queue<Func<HttpResponseMessage>>> _responses = new(StringComparer.Ordinal);
    private readonly List<Uri> _requestedUrls = [];
    private readonly List<string> _userAgents = [];

    public IReadOnlyList<Uri> RequestedUrls
    {
        get
        {
            lock (_gate)
            {
                return [.. _requestedUrls];
            }
        }
    }

    public IReadOnlyList<string> UserAgents
    {
        get
        {
            lock (_gate)
            {
                return [.. _userAgents];
            }
        }
    }

    public static Func<HttpResponseMessage> Ok(byte[] content) =>
        () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };

    public static Func<HttpResponseMessage> Ok(string content) => Ok(Encoding.UTF8.GetBytes(content));

    public static Func<HttpResponseMessage> Status(HttpStatusCode status) => () => new HttpResponseMessage(status);

    public static Func<HttpResponseMessage> RedirectTo(string location) => () =>
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    };

    public static Func<HttpResponseMessage> Stalling() =>
        () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream()) };

    public FakeHttpHandler Respond(string url, params Func<HttpResponseMessage>[] responses)
    {
        lock (_gate)
        {
            _responses[url] = new Queue<Func<HttpResponseMessage>>(responses);
        }

        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(Send(request, cancellationToken));

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _requestedUrls.Add(request.RequestUri!);
            _userAgents.Add(request.Headers.UserAgent.ToString());
            if (!_responses.TryGetValue(request.RequestUri!.AbsoluteUri, out var queue))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            var factory = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
            return factory();
        }
    }
}
```

`test/Cake.Download.Module.Tests/Fakes/StallingStream.cs`:

```csharp
namespace Cake.Download.Module.Tests.Fakes;

/// <summary>
/// A response body that never delivers a byte, until the reader cancels.
/// </summary>
internal sealed class StallingStream : Stream
{
    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return 0;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
```

`test/Cake.Download.Module.Tests/Fakes/TestDirectory.cs`:

```csharp
namespace Cake.Download.Module.Tests.Fakes;

internal sealed class TestDirectory : IDisposable
{
    public TestDirectory()
    {
        Root = Directory.CreateTempSubdirectory("cake-download-tests-").FullName;
    }

    public string Root { get; }

    public string Combine(params string[] parts) => Path.Combine([Root, .. parts]);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
```

`test/Cake.Download.Module.Tests/Fakes/TestHashes.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace Cake.Download.Module.Tests.Fakes;

internal static class TestHashes
{
    public static string Sha256(string text) => Sha256(Encoding.UTF8.GetBytes(text));

    public static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
```

- [ ] **Step 2: Write the failing tests**

`test/Cake.Download.Module.Tests/Http/HttpDownloaderTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using Cake.Core;
using Cake.Download.Module.Http;
using Cake.Download.Module.Tests.Fakes;
using Cake.Testing;

namespace Cake.Download.Module.Tests.Http;

public sealed class HttpDownloaderTests : IDisposable
{
    private const string Url = "https://example.com/tool";
    private const string HelloSha256 = "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824";

    private readonly TestDirectory _directory = new();
    private readonly FakeHttpHandler _handler = new();
    private readonly FakeLog _log = new();
    private readonly List<TimeSpan> _delays = [];

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void Download_Writes_The_File_And_Computes_Its_Sha256()
    {
        _handler.Respond(Url, FakeHttpHandler.Ok("hello"));

        var result = Download();

        Assert.Equal(HelloSha256, result.Sha256);
        Assert.Equal(5, result.Length);
        Assert.Equal("hello", File.ReadAllText(result.Path));
    }

    [Fact]
    public void Download_Follows_Redirects_And_Sends_A_User_Agent()
    {
        _handler
            .Respond(Url, FakeHttpHandler.RedirectTo("https://objects.example.com/signed?sig=1"))
            .Respond("https://objects.example.com/signed?sig=1", FakeHttpHandler.RedirectTo("/final"))
            .Respond("https://objects.example.com/final", FakeHttpHandler.Ok("hello"));

        var result = Download();

        Assert.Equal(HelloSha256, result.Sha256);
        Assert.Equal(3, _handler.RequestedUrls.Count);
        Assert.All(_handler.UserAgents, agent => Assert.StartsWith("Cake.Download.Module/", agent));
    }

    [Fact]
    public void Download_Refuses_A_Redirect_To_Http()
    {
        _handler.Respond(Url, FakeHttpHandler.RedirectTo("http://example.com/tool"));

        var exception = Assert.Throws<CakeException>(() => Download());

        Assert.Equal("https://example.com/tool redirected to http://example.com/tool, which is not https. Only https downloads are supported.", exception.Message);
    }

    [Fact]
    public void Download_Refuses_Too_Many_Redirects()
    {
        _handler
            .Respond(Url, FakeHttpHandler.RedirectTo("https://example.com/a"))
            .Respond("https://example.com/a", FakeHttpHandler.RedirectTo("https://example.com/b"))
            .Respond("https://example.com/b", FakeHttpHandler.RedirectTo("https://example.com/c"));

        var exception = Assert.Throws<CakeException>(() => Download(new DownloadOptions { MaxRedirects = 2 }));

        Assert.Equal("https://example.com/tool redirected more than 2 times.", exception.Message);
    }

    [Fact]
    public void Download_Does_Not_Retry_A_404()
    {
        var exception = Assert.Throws<DownloadHttpException>(() => Download());

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("Downloading https://example.com/tool failed: HTTP 404 (Not Found).", exception.Message);
        Assert.Single(_handler.RequestedUrls);
    }

    [Fact]
    public void Download_Retries_A_Server_Error_With_Backoff()
    {
        _handler.Respond(Url, FakeHttpHandler.Status(HttpStatusCode.InternalServerError), FakeHttpHandler.Ok("hello"));

        Assert.Equal(HelloSha256, Download().Sha256);
        Assert.Equal(2, _handler.RequestedUrls.Count);
        Assert.Equal([TimeSpan.FromSeconds(1)], _delays);
    }

    [Theory]
    [InlineData(7, 7)]
    [InlineData(120, 30)]
    public void Download_Honours_Retry_After_Up_To_The_Cap(int retryAfterSeconds, int expectedSeconds)
    {
        _handler.Respond(
            Url,
            () =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(retryAfterSeconds));
                return response;
            },
            FakeHttpHandler.Ok("hello"));

        Download();

        Assert.Equal([TimeSpan.FromSeconds(expectedSeconds)], _delays);
    }

    [Fact]
    public void Download_Gives_Up_After_The_Last_Attempt()
    {
        _handler.Respond(Url, FakeHttpHandler.Status(HttpStatusCode.ServiceUnavailable));

        var exception = Assert.Throws<CakeException>(() => Download());

        Assert.Equal("Downloading https://example.com/tool failed after 3 attempts: HTTP 503 (Service Unavailable).", exception.Message);
        Assert.Equal(3, _handler.RequestedUrls.Count);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], _delays);
    }

    [Fact]
    public void Download_Retries_A_Stalled_Body()
    {
        _handler.Respond(Url, FakeHttpHandler.Stalling(), FakeHttpHandler.Ok("hello"));

        var result = Download(new DownloadOptions { StallTimeout = TimeSpan.FromMilliseconds(200) });

        Assert.Equal(HelloSha256, result.Sha256);
        Assert.Contains(_log.Entries, entry => entry.Message.Contains("No data received from https://example.com/tool for 0.2 s", StringComparison.Ordinal));
    }

    [Fact]
    public void Download_Enforces_The_Size_Limit_Without_Retrying()
    {
        _handler.Respond(Url, FakeHttpHandler.Ok("hello world"));

        var exception = Assert.Throws<CakeException>(() => Download(maxBytes: 5));

        Assert.Equal("https://example.com/tool is larger than the 5 bytes allowed.", exception.Message);
        Assert.Single(_handler.RequestedUrls);
    }

    private DownloadResult Download(DownloadOptions? options = null, long? maxBytes = null)
    {
        var effective = (options ?? new DownloadOptions()) with
        {
            Delay = (delay, _) =>
            {
                _delays.Add(delay);
                return Task.CompletedTask;
            },
        };

        return new HttpDownloader(_handler, _log, effective).Download(new Uri(Url), _directory.Combine("download"), maxBytes);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test --project test/Cake.Download.Module.Tests/Cake.Download.Module.Tests.csproj --filter-class "*HttpDownloaderTests"`
Expected: build fails with `CS0246: The type or namespace name 'DownloadOptions' could not be found`.

- [ ] **Step 4: Implement the HTTP types**

`src/Cake.Download.Module/Http/DownloadOptions.cs`:

```csharp
namespace Cake.Download.Module.Http;

internal sealed record DownloadOptions
{
    public static DownloadOptions Default { get; } = new();

    public TimeSpan StallTimeout { get; init; } = TimeSpan.FromSeconds(60);

    public int MaxAttempts { get; init; } = 3;

    public int MaxRedirects { get; init; } = 10;

    public TimeSpan MaxRetryAfter { get; init; } = TimeSpan.FromSeconds(30);

    public Func<int, TimeSpan> Backoff { get; init; } = attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));

    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = (delay, cancellationToken) => Task.Delay(delay, cancellationToken);
}
```

`src/Cake.Download.Module/Http/DownloadResult.cs`:

```csharp
namespace Cake.Download.Module.Http;

internal sealed record DownloadResult(string Path, string Sha256, long Length);
```

`src/Cake.Download.Module/Http/DownloadHttpException.cs`:

```csharp
using System.Net;
using Cake.Core;

namespace Cake.Download.Module.Http;

internal sealed class DownloadHttpException : CakeException
{
    public DownloadHttpException(Uri url, HttpStatusCode statusCode, string message)
        : base(message)
    {
        Url = url;
        StatusCode = statusCode;
    }

    public Uri Url { get; }

    public HttpStatusCode StatusCode { get; }
}
```

- [ ] **Step 5: Implement the downloader**

`src/Cake.Download.Module/Http/HttpDownloader.cs`:

```csharp
using System.Net;
using System.Security.Cryptography;
using Cake.Core;
using Cake.Core.Diagnostics;

namespace Cake.Download.Module.Http;

/// <summary>
/// Streams a URL to a file while hashing it. Follows https-only redirects itself, retries transient failures and
/// aborts an attempt when no bytes arrive for <see cref="DownloadOptions.StallTimeout"/>. Never sends credentials.
/// </summary>
internal sealed class HttpDownloader
{
    private static readonly string UserAgent =
        $"Cake.Download.Module/{typeof(HttpDownloader).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"}";

    private readonly HttpClient _client;
    private readonly ICakeLog _log;
    private readonly DownloadOptions _options;

    public HttpDownloader(HttpMessageHandler handler, ICakeLog log, DownloadOptions options)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _client = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public DownloadResult Download(Uri url, string destinationPath, long? maxBytes = null) =>
        DownloadAsync(url, destinationPath, maxBytes).GetAwaiter().GetResult();

    private static bool IsRedirect(HttpStatusCode status) => (int)status is 301 or 302 or 303 or 307 or 308;

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
        {
            return delta;
        }

        return header?.Date is { } date ? date - DateTimeOffset.UtcNow : null;
    }

    private async Task<DownloadResult> DownloadAsync(Uri url, string destinationPath, long? maxBytes)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= _options.MaxAttempts; attempt++)
        {
            TimeSpan? retryAfter = null;
            try
            {
                return await DownloadOnceAsync(url, destinationPath, maxBytes).ConfigureAwait(false);
            }
            catch (RetryableException exception)
            {
                lastError = exception;
                retryAfter = exception.RetryAfter;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or TimeoutException)
            {
                lastError = exception;
            }

            if (attempt < _options.MaxAttempts)
            {
                var delay = retryAfter is { } wait
                    ? TimeSpan.FromTicks(Math.Clamp(wait.Ticks, 0, _options.MaxRetryAfter.Ticks))
                    : _options.Backoff(attempt);
                _log.Verbose(
                    "Attempt {0} of {1} to download {2} failed: {3} Retrying in {4:0.#} s.",
                    attempt,
                    _options.MaxAttempts,
                    url,
                    lastError.Message,
                    delay.TotalSeconds);
                await _options.Delay(delay, CancellationToken.None).ConfigureAwait(false);
            }
        }

        throw new CakeException($"Downloading {url} failed after {_options.MaxAttempts} attempts: {lastError!.Message}", lastError);
    }

    private async Task<DownloadResult> DownloadOnceAsync(Uri url, string destinationPath, long? maxBytes)
    {
        using var stall = new CancellationTokenSource();
        using var response = await SendFollowingRedirectsAsync(url, stall).ConfigureAwait(false);

        var status = (int)response.StatusCode;
        if (status is 408 or 429 or >= 500)
        {
            throw new RetryableException($"HTTP {status} ({response.ReasonPhrase}).", RetryAfter(response));
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new DownloadHttpException(url, response.StatusCode, $"Downloading {url} failed: HTTP {status} ({response.ReasonPhrase}).");
        }

        if (maxBytes is { } limit && response.Content.Headers.ContentLength > limit)
        {
            throw TooLarge(url, limit);
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long length = 0;
        {
            await using var source = await response.Content.ReadAsStreamAsync(stall.Token).ConfigureAwait(false);
            await using var target = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            while (true)
            {
                stall.CancelAfter(_options.StallTimeout);
                int read;
                try
                {
                    read = await source.ReadAsync(buffer, stall.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stall.IsCancellationRequested)
                {
                    throw Stalled(url);
                }

                if (read == 0)
                {
                    break;
                }

                length += read;
                if (maxBytes is { } max && length > max)
                {
                    throw TooLarge(url, max);
                }

                hash.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
            }
        }

        return new DownloadResult(destinationPath, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), length);
    }

    private async Task<HttpResponseMessage> SendFollowingRedirectsAsync(Uri url, CancellationTokenSource stall)
    {
        var current = url;
        for (var redirects = 0; ; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            stall.CancelAfter(_options.StallTimeout);

            HttpResponseMessage response;
            try
            {
                response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stall.IsCancellationRequested)
            {
                throw Stalled(current);
            }

            if (!IsRedirect(response.StatusCode))
            {
                return response;
            }

            var location = response.Headers.Location;
            response.Dispose();
            if (location is null)
            {
                throw new CakeException($"{current} redirected without a Location header.");
            }

            var next = location.IsAbsoluteUri ? location : new Uri(current, location);
            if (next.Scheme != Uri.UriSchemeHttps)
            {
                throw new CakeException($"{current} redirected to {next}, which is not https. Only https downloads are supported.");
            }

            if (redirects + 1 > _options.MaxRedirects)
            {
                throw new CakeException($"{url} redirected more than {_options.MaxRedirects} times.");
            }

            _log.Verbose("{0} redirected to {1}.", current.GetLeftPart(UriPartial.Path), next.GetLeftPart(UriPartial.Path));
            current = next;
        }
    }

    private TimeoutException Stalled(Uri url) =>
        new($"No data received from {url} for {_options.StallTimeout.TotalSeconds:0.#} s.");

    private static CakeException TooLarge(Uri url, long limit) => new($"{url} is larger than the {limit} bytes allowed.");

    private sealed class RetryableException : Exception
    {
        public RetryableException(string message, TimeSpan? retryAfter)
            : base(message)
        {
            RetryAfter = retryAfter;
        }

        public TimeSpan? RetryAfter { get; }
    }
}
```

The retry log line formats `lastError.Message`, which already ends with a period. `Math.Clamp` caps `Retry-After` at 30 s and turns a `Date` in the past into a zero delay.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --project test/Cake.Download.Module.Tests/Cake.Download.Module.Tests.csproj --filter-class "*HttpDownloaderTests"`
Expected: PASS (the stall test takes about 0.2 s).

Run: `dotnet build src/Cake.Download.Module.slnx -c Release`
Expected: 0 warnings.

- [ ] **Step 7: Commit**

```bash
git add src/ test/
git commit -F - <<'EOF'
Add HTTPS downloader with redirects, retries and stall timeout

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 6: Checksums file parser and integrity resolver

**Files:**
- Create: `src/Cake.Download.Module/Integrity/ChecksumsFileParser.cs`, `src/Cake.Download.Module/Integrity/IntegrityResolver.cs`
- Test: `test/Cake.Download.Module.Tests/Integrity/ChecksumsFileParserTests.cs`, `test/Cake.Download.Module.Tests/Integrity/IntegrityResolverTests.cs`

**Interfaces:**
- Consumes:
  - `DownloadPlan` and the `IntegrityPlan` subtypes (Task 4)
  - `HttpDownloader`, `DownloadOptions` (Task 5)
  - test helpers `FakeHttpHandler`, `TestDirectory`, `TestHashes` (Task 5)
- Produces (namespace `Cake.Download.Module.Integrity`):
  - `static class ChecksumsFileParser`: `IReadOnlyDictionary<string,string> Parse(string text, string source)`. The result maps name → lower-case hash.
  - `sealed record ExpectedHash(string Sha256, string Source)`.
  - `sealed class IntegrityResolver(HttpDownloader downloader)`:
    - `ExpectedHash? Resolve(DownloadPlan plan, string workDirectory)`: returns null for skipped or missing integrity.
    - `static void Verify(DownloadPlan plan, ExpectedHash? expected, string actualSha256)`

- [ ] **Step 1: Write the failing tests**

`test/Cake.Download.Module.Tests/Integrity/ChecksumsFileParserTests.cs`:

```csharp
using Cake.Core;
using Cake.Download.Module.Integrity;

namespace Cake.Download.Module.Tests.Integrity;

public sealed class ChecksumsFileParserTests
{
    private const string A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string B = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void Parse_Reads_Gnu_Text_And_Binary_Lines()
    {
        var entries = ChecksumsFileParser.Parse($"{A}  jq-linux-amd64\n{B} *jq-windows-amd64.exe\n", "sums");

        Assert.Equal(A, entries["jq-linux-amd64"]);
        Assert.Equal(B, entries["jq-windows-amd64.exe"]);
    }

    [Fact]
    public void Parse_Reads_Bsd_Lines()
    {
        Assert.Equal(A, ChecksumsFileParser.Parse($"SHA256 (tool.tar.gz) = {A}", "sums")["tool.tar.gz"]);
    }

    [Fact]
    public void Parse_Tolerates_Crlf_Blank_Lines_Other_Content_Upper_Case_And_Dot_Slash()
    {
        var entries = ChecksumsFileParser.Parse($"# checksums\r\n\r\n{A.ToUpperInvariant()}  ./tool.zip\r\nnot a checksum line\r\n", "sums");

        Assert.Equal(A, Assert.Single(entries).Value);
        Assert.Equal("tool.zip", entries.Keys.Single());
    }

    [Fact]
    public void Parse_Accepts_Identical_Duplicates_And_Rejects_Conflicting_Ones()
    {
        Assert.Single(ChecksumsFileParser.Parse($"{A}  tool\n{A}  tool\n", "sums"));

        var exception = Assert.Throws<CakeException>(() => ChecksumsFileParser.Parse($"{A}  tool\n{B}  tool\n", "https://example.com/sums"));
        Assert.Equal("The checksums file https://example.com/sums lists two different SHA-256 hashes for 'tool'.", exception.Message);
    }
}
```

`test/Cake.Download.Module.Tests/Integrity/IntegrityResolverTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project test/Cake.Download.Module.Tests/Cake.Download.Module.Tests.csproj --filter-class "*ChecksumsFileParserTests"`
Expected: build fails with `CS0103: The name 'ChecksumsFileParser' does not exist`.

- [ ] **Step 3: Implement the parser**

`src/Cake.Download.Module/Integrity/ChecksumsFileParser.cs`:

```csharp
using System.Text.RegularExpressions;
using Cake.Core;

namespace Cake.Download.Module.Integrity;

/// <summary>
/// Reads GNU (<c>hash  name</c>, <c>hash *name</c>) and BSD (<c>SHA256 (name) = hash</c>) checksum lines and ignores
/// everything else.
/// </summary>
internal static partial class ChecksumsFileParser
{
    public static IReadOnlyDictionary<string, string> Parse(string text, string source)
    {
        ArgumentNullException.ThrowIfNull(text);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var match = Gnu().Match(line);
            if (!match.Success)
            {
                match = Bsd().Match(line);
            }

            if (!match.Success)
            {
                continue;
            }

            var name = match.Groups["name"].Value;
            if (name.StartsWith("./", StringComparison.Ordinal))
            {
                name = name[2..];
            }

            var hash = match.Groups["hash"].Value.ToLowerInvariant();
            if (result.TryGetValue(name, out var existing) && existing != hash)
            {
                throw new CakeException($"The checksums file {source} lists two different SHA-256 hashes for '{name}'.");
            }

            result[name] = hash;
        }

        return result;
    }

    [GeneratedRegex(@"^(?<hash>[0-9a-fA-F]{64})\s+\*?(?<name>\S.*)$")]
    private static partial Regex Gnu();

    [GeneratedRegex(@"^SHA256 ?\((?<name>.+)\) ?= ?(?<hash>[0-9a-fA-F]{64})$")]
    private static partial Regex Bsd();
}
```

- [ ] **Step 4: Implement the resolver**

`src/Cake.Download.Module/Integrity/IntegrityResolver.cs`:

```csharp
using Cake.Core;
using Cake.Download.Module.Directives;
using Cake.Download.Module.Http;

namespace Cake.Download.Module.Integrity;

internal sealed record ExpectedHash(string Sha256, string Source);

/// <summary>
/// Turns a plan's integrity option into the SHA-256 the download must have, and verifies the download against it.
/// </summary>
internal sealed class IntegrityResolver
{
    private const long MaxChecksumsFileBytes = 1024 * 1024;

    private readonly HttpDownloader _downloader;

    public IntegrityResolver(HttpDownloader downloader)
    {
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
    }

    public static void Verify(DownloadPlan plan, ExpectedHash? expected, string actualSha256)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Integrity is SkippedIntegrity)
        {
            return;
        }

        if (plan.Integrity is MissingIntegrityPlan missing)
        {
            throw new CakeException(
                $"The download directive for '{plan.Package}' has no integrity check. {plan.AssetName} ({plan.Platform.Rid}) has SHA-256 {actualSha256}. " +
                $"Add '&{missing.Parameter}={actualSha256}' to the directive, or '&sha256=skip' to install without verification (not recommended). " +
                "The download was discarded.");
        }

        if (expected is null)
        {
            throw new InvalidOperationException("An expected hash is required for pinned integrity.");
        }

        if (!string.Equals(expected.Sha256, actualSha256, StringComparison.Ordinal))
        {
            throw new CakeException(
                $"SHA-256 mismatch for {plan.Url.AbsoluteUri}.{Environment.NewLine}" +
                $"  Expected: {expected.Sha256} (from {expected.Source}){Environment.NewLine}" +
                $"  Actual:   {actualSha256}{Environment.NewLine}" +
                "The download was discarded.");
        }
    }

    public ExpectedHash? Resolve(DownloadPlan plan, string workDirectory)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return plan.Integrity switch
        {
            PinnedSha256 pinned => new ExpectedHash(pinned.Sha256, $"'{pinned.Parameter}'"),
            ChecksumsFilePlan checksums => FromChecksumsFile(plan, checksums, workDirectory),
            _ => null,
        };
    }

    private static string? Find(IReadOnlyDictionary<string, string> entries, string asset)
    {
        if (entries.TryGetValue(asset, out var exact))
        {
            return exact;
        }

        var bySuffix = entries
            .Where(entry => entry.Key.EndsWith("/" + asset, StringComparison.Ordinal))
            .Select(entry => entry.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return bySuffix.Count == 1 ? bySuffix[0] : null;
    }

    private ExpectedHash FromChecksumsFile(DownloadPlan plan, ChecksumsFilePlan checksums, string workDirectory)
    {
        var url = checksums.Url.AbsoluteUri;
        var download = _downloader.Download(checksums.Url, Path.Combine(workDirectory, "checksums.txt"), MaxChecksumsFileBytes);

        if (checksums.Sha256 is null)
        {
            throw new CakeException(
                $"The checksums file {url} is not pinned. Its SHA-256 is {download.Sha256}. " +
                $"Add '&checksums_sha256={download.Sha256}' to the directive for '{plan.Package}'.");
        }

        if (!string.Equals(checksums.Sha256, download.Sha256, StringComparison.Ordinal))
        {
            throw new CakeException(
                $"SHA-256 mismatch for checksums file {url}.{Environment.NewLine}" +
                $"  Expected: {checksums.Sha256} (from 'checksums_sha256'){Environment.NewLine}" +
                $"  Actual:   {download.Sha256}");
        }

        var entries = ChecksumsFileParser.Parse(File.ReadAllText(download.Path), url);
        var hash = Find(entries, plan.AssetName);
        if (hash is null)
        {
            var listed = string.Join(", ", entries.Keys.Take(20)) + (entries.Count > 20 ? ", …" : string.Empty);
            throw new CakeException($"'{plan.AssetName}' is not listed in the checksums file {url}. Listed files: {listed}.");
        }

        return new ExpectedHash(hash, "checksums file " + url);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --project test/Cake.Download.Module.Tests/Cake.Download.Module.Tests.csproj --filter-class "*ChecksumsFileParserTests"` and again with `--filter-class "*IntegrityResolverTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ test/
git commit -F - <<'EOF'
Resolve and verify SHA-256 from pins and pinned checksums files

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 7: Archive extractor

**Files:**
- Create: `src/Cake.Download.Module/Archives/ArchiveExtractor.cs`
- Create (test helper): `test/Cake.Download.Module.Tests/Fakes/TestArchives.cs`
- Test: `test/Cake.Download.Module.Tests/Archives/ArchiveExtractorTests.cs`

**Interfaces:**
- Consumes: `ArchiveFormat` (Task 4); `TestDirectory` (Task 5).
- Produces:
  - `static class ArchiveExtractor` (namespace `Cake.Download.Module.Archives`): `void Extract(string archivePath, ArchiveFormat format, string destination)`.
    - Throws `CakeException("Refusing to extract archive entry '<name>': <reason>.")` for unsafe entries.
    - Throws `ArgumentOutOfRangeException` for `ArchiveFormat.File`.
  - Test helper `TestArchives` (namespace `Cake.Download.Module.Tests.Fakes`):
    - `static byte[] Zip(params ArchiveEntrySpec[] entries)`, `static byte[] Tar(bool gzip, params ArchiveEntrySpec[] entries)`
    - `sealed record ArchiveEntrySpec(string Name, string Content = "", UnixFileMode? Mode = null)` with static factories `Directory(name)`, `Symlink(name, target)`, `Hardlink(name, target)`.

- [ ] **Step 1: Write the test helper**

`test/Cake.Download.Module.Tests/Fakes/TestArchives.cs`:

```csharp
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;

namespace Cake.Download.Module.Tests.Fakes;

internal sealed record ArchiveEntrySpec(string Name, string Content = "", UnixFileMode? Mode = null)
{
    public bool IsDirectory { get; init; }

    public string? SymlinkTarget { get; init; }

    public string? HardlinkTarget { get; init; }

    public static ArchiveEntrySpec Directory(string name) => new(name) { IsDirectory = true };

    public static ArchiveEntrySpec Symlink(string name, string target) => new(name) { SymlinkTarget = target };

    public static ArchiveEntrySpec Hardlink(string name, string target) => new(name) { HardlinkTarget = target };
}

internal static class TestArchives
{
    private const UnixFileMode DefaultMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    public static byte[] Zip(params ArchiveEntrySpec[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var spec in entries)
            {
                var entry = archive.CreateEntry(spec.IsDirectory ? spec.Name.TrimEnd('/') + "/" : spec.Name);
                if (spec.Mode is { } mode)
                {
                    entry.ExternalAttributes = unchecked((int)(((uint)mode | 0x8000u) << 16));
                }

                if (!spec.IsDirectory)
                {
                    using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                    writer.Write(spec.Content);
                }
            }
        }

        return buffer.ToArray();
    }

    public static byte[] Tar(bool gzip, params ArchiveEntrySpec[] entries)
    {
        using var buffer = new MemoryStream();
        using (Stream output = gzip ? new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true) : new NonClosingStream(buffer))
        using (var writer = new TarWriter(output, TarEntryFormat.Pax, leaveOpen: true))
        {
            foreach (var spec in entries)
            {
                PaxTarEntry entry;
                if (spec.IsDirectory)
                {
                    entry = new PaxTarEntry(TarEntryType.Directory, spec.Name);
                }
                else if (spec.SymlinkTarget is not null)
                {
                    entry = new PaxTarEntry(TarEntryType.SymbolicLink, spec.Name) { LinkName = spec.SymlinkTarget };
                }
                else if (spec.HardlinkTarget is not null)
                {
                    entry = new PaxTarEntry(TarEntryType.HardLink, spec.Name) { LinkName = spec.HardlinkTarget };
                }
                else
                {
                    entry = new PaxTarEntry(TarEntryType.RegularFile, spec.Name)
                    {
                        Mode = spec.Mode ?? DefaultMode,
                        DataStream = new MemoryStream(Encoding.UTF8.GetBytes(spec.Content)),
                    };
                }

                writer.WriteEntry(entry);
            }
        }

        return buffer.ToArray();
    }

    private sealed class NonClosingStream(Stream inner) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }
}
```

- [ ] **Step 2: Write the failing tests**

`test/Cake.Download.Module.Tests/Archives/ArchiveExtractorTests.cs`:

```csharp
using Cake.Core;
using Cake.Download.Module.Archives;
using Cake.Download.Module.Directives;
using Cake.Download.Module.Tests.Fakes;

namespace Cake.Download.Module.Tests.Archives;

public sealed class ArchiveExtractorTests : IDisposable
{
    private const UnixFileMode Executable =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    private readonly TestDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Theory]
    [InlineData(ArchiveFormat.Zip)]
    [InlineData(ArchiveFormat.Tar)]
    [InlineData(ArchiveFormat.TarGz)]
    public void Extract_Keeps_The_Archive_Layout_Verbatim(ArchiveFormat format)
    {
        var target = Extract(
            format,
            ArchiveEntrySpec.Directory("gh_2.62.0_linux_amd64"),
            new ArchiveEntrySpec("gh_2.62.0_linux_amd64/bin/gh", "binary"),
            new ArchiveEntrySpec("gh_2.62.0_linux_amd64/LICENSE", "MIT"));

        Assert.Equal("binary", File.ReadAllText(Path.Combine(target, "gh_2.62.0_linux_amd64", "bin", "gh")));
        Assert.Equal("MIT", File.ReadAllText(Path.Combine(target, "gh_2.62.0_linux_amd64", "LICENSE")));
    }

    [Theory]
    [InlineData(ArchiveFormat.Zip)]
    [InlineData(ArchiveFormat.TarGz)]
    public void Extract_Keeps_Unix_Execute_Bits(ArchiveFormat format)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes only.");
            return;
        }

        var target = Extract(format, new ArchiveEntrySpec("bin/tool", "binary", Executable));

        Assert.Equal(Executable, File.GetUnixFileMode(Path.Combine(target, "bin", "tool")));
    }

    [Theory]
    [InlineData(ArchiveFormat.Zip, "../evil.txt")]
    [InlineData(ArchiveFormat.Zip, "a/../../evil.txt")]
    [InlineData(ArchiveFormat.Tar, "../evil.txt")]
    [InlineData(ArchiveFormat.TarGz, "/etc/evil.txt")]
    public void Extract_Rejects_Entries_Outside_The_Destination(ArchiveFormat format, string name)
    {
        var exception = Assert.Throws<CakeException>(() => Extract(format, new ArchiveEntrySpec(name, "evil")));

        Assert.StartsWith($"Refusing to extract archive entry '{name}': ", exception.Message);
        Assert.False(File.Exists(_directory.Combine("sandbox", "evil.txt")));
    }

    [Fact]
    public void Extract_Rejects_A_Symlink_Pointing_Outside_The_Destination()
    {
        var exception = Assert.Throws<CakeException>(
            () => Extract(ArchiveFormat.TarGz, ArchiveEntrySpec.Symlink("bin/tool", "../../../outside")));

        Assert.Equal("Refusing to extract archive entry 'bin/tool': its link target '../../../outside' is outside the target folder.", exception.Message);
    }

    [Fact]
    public void Extract_Materializes_Links_Inside_The_Destination()
    {
        var target = Extract(
            ArchiveFormat.TarGz,
            new ArchiveEntrySpec("tool-1.0/bin/tool", "binary"),
            ArchiveEntrySpec.Symlink("tool-1.0/tool", "bin/tool"),
            ArchiveEntrySpec.Hardlink("tool-1.0/tool-copy", "tool-1.0/bin/tool"));

        Assert.Equal("binary", File.ReadAllText(Path.Combine(target, "tool-1.0", "tool")));
        Assert.Equal("binary", File.ReadAllText(Path.Combine(target, "tool-1.0", "tool-copy")));
    }

    [Fact]
    public void Extract_Rejects_Raw_Files()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ArchiveExtractor.Extract(_directory.Combine("x"), ArchiveFormat.File, _directory.Combine("out")));
    }

    private string Extract(ArchiveFormat format, params ArchiveEntrySpec[] entries)
    {
        var archive = _directory.Combine("archive");
        File.WriteAllBytes(archive, format == ArchiveFormat.Zip ? TestArchives.Zip(entries) : TestArchives.Tar(format == ArchiveFormat.TarGz, entries));
        var target = _directory.Combine("sandbox", "content");
        ArchiveExtractor.Extract(archive, format, target);
        return target;
    }
}
```

The destination is two levels deep (`sandbox/content`), so both `../evil.txt` and `a/../../evil.txt` would land in `sandbox/`. The test asserts that nothing was written there.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test --project test/Cake.Download.Module.Tests/Cake.Download.Module.Tests.csproj --filter-class "*ArchiveExtractorTests"`
Expected: build fails with `CS0103: The name 'ArchiveExtractor' does not exist`.

- [ ] **Step 4: Implement the extractor**

`src/Cake.Download.Module/Archives/ArchiveExtractor.cs`:

```csharp
using System.Formats.Tar;
using System.IO.Compression;
using Cake.Core;
using Cake.Download.Module.Directives;

namespace Cake.Download.Module.Archives;

/// <summary>
/// Extracts zip and tar archives verbatim, refusing any entry or link that would land outside the destination and
/// keeping Unix permission bits.
/// </summary>
internal static class ArchiveExtractor
{
    private const int PermissionBits = 0x1FF;

    public static void Extract(string archivePath, ArchiveFormat format, string destination)
    {
        if (format == ArchiveFormat.File)
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "Only archives can be extracted.");
        }

        Directory.CreateDirectory(destination);
        var root = Path.GetFullPath(destination);
        if (format == ArchiveFormat.Zip)
        {
            ExtractZip(archivePath, root);
        }
        else
        {
            ExtractTar(archivePath, root, gzip: format == ArchiveFormat.TarGz);
        }
    }

    private static void ExtractZip(string archivePath, string root)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var target = ResolveInside(root, entry.FullName);
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);

            var mode = (entry.ExternalAttributes >> 16) & PermissionBits;
            if (!OperatingSystem.IsWindows() && mode != 0)
            {
                File.SetUnixFileMode(target, (UnixFileMode)mode);
            }
        }
    }

    private static void ExtractTar(string archivePath, string root, bool gzip)
    {
        var links = new List<(string Path, string LinkName, string Target, bool Symbolic)>();
        using (var file = File.OpenRead(archivePath))
        using (var stream = gzip ? new GZipStream(file, CompressionMode.Decompress) : (Stream)file)
        using (var reader = new TarReader(stream))
        {
            while (reader.GetNextEntry() is { } entry)
            {
                switch (entry.EntryType)
                {
                    case TarEntryType.Directory:
                        Directory.CreateDirectory(ResolveInside(root, entry.Name));
                        break;
                    case TarEntryType.RegularFile:
                    case TarEntryType.V7RegularFile:
                    case TarEntryType.ContiguousFile:
                        var target = ResolveInside(root, entry.Name);
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        entry.ExtractToFile(target, overwrite: true);
                        if (!OperatingSystem.IsWindows())
                        {
                            File.SetUnixFileMode(target, (UnixFileMode)((int)entry.Mode & PermissionBits));
                        }

                        break;
                    case TarEntryType.SymbolicLink:
                        var link = ResolveInside(root, entry.Name);
                        var linkTarget = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(link)!, entry.LinkName));
                        if (!IsInside(root, linkTarget))
                        {
                            throw Unsafe(entry.Name, $"its link target '{entry.LinkName}' is outside the target folder");
                        }

                        links.Add((link, entry.LinkName, linkTarget, true));
                        break;
                    case TarEntryType.HardLink:
                        links.Add((ResolveInside(root, entry.Name), entry.LinkName, ResolveInside(root, entry.LinkName), false));
                        break;
                    default:
                        // PAX/GNU metadata entries are consumed by TarReader; devices and FIFOs are skipped.
                        break;
                }
            }
        }

        foreach (var (path, linkName, target, symbolic) in links)
        {
            CreateLink(path, linkName, target, symbolic);
        }
    }

    private static void CreateLink(string path, string linkName, string target, bool symbolic)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        if (symbolic && !OperatingSystem.IsWindows())
        {
            File.CreateSymbolicLink(path, linkName);
            return;
        }

        // Hard links, and symbolic links on Windows (which need extra privileges), become copies of their target.
        if (File.Exists(target))
        {
            File.Copy(target, path, overwrite: true);
        }
    }

    private static string ResolveInside(string root, string entryName)
    {
        var name = entryName.Replace('\\', '/');
        if (name.StartsWith('/') || Path.IsPathRooted(name) || (name.Length >= 2 && name[1] == ':'))
        {
            throw Unsafe(entryName, "absolute paths are not allowed");
        }

        var full = Path.GetFullPath(Path.Combine(root, name));
        if (!IsInside(root, full))
        {
            throw Unsafe(entryName, "it would be extracted outside the target folder");
        }

        return full;
    }

    private static bool IsInside(string root, string fullPath)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var rootWithSeparator = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return string.Equals(Path.TrimEndingDirectorySeparator(fullPath), Path.TrimEndingDirectorySeparator(root), comparison)
            || fullPath.StartsWith(rootWithSeparator, comparison);
    }

    private static CakeException Unsafe(string entryName, string reason) =>
        new($"Refusing to extract archive entry '{entryName}': {reason}.");
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --project test/Cake.Download.Module.Tests/Cake.Download.Module.Tests.csproj --filter-class "*ArchiveExtractorTests"`
Expected: PASS. On Windows, `Extract_Keeps_Unix_Execute_Bits` is reported as skipped.

Run: `dotnet build src/Cake.Download.Module.slnx -c Release`
Expected: 0 warnings (in particular no CA1416 for `File.SetUnixFileMode` / `File.GetUnixFileMode`).

- [ ] **Step 6: Commit**

```bash
git add src/ test/
git commit -F - <<'EOF'
Extract zip and tar archives safely with Unix modes

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 8: Glob matcher and file selector

**Files:**
- Create: `src/Cake.Download.Module/Installation/GlobMatcher.cs`, `src/Cake.Download.Module/Installation/FileSelector.cs`
- Test: `test/Cake.Download.Module.Tests/Installation/GlobMatcherTests.cs`, `test/Cake.Download.Module.Tests/Installation/FileSelectorTests.cs`

**Interfaces:**
- Consumes: `TestDirectory` (Task 5); `Cake.Core.IO.IFileSystem`, `IFile`, `FilePath`; real `Cake.Core.IO.FileSystem` in tests.
- Produces (namespace `Cake.Download.Module.Installation`):
  - `sealed class GlobMatcher(string pattern, bool ignoreCase)`:
    - `string Pattern`
    - `bool IsMatch(string relativePath)`
    - supports `**`, `*` and `?`; rejects `..`
  - `sealed class FileSelector(IFileSystem fileSystem)`:
    - `const string MarkerFileName = ".cake-download.json"`: never selected
    - `IReadOnlyList<string> Match(string installDirectory, IReadOnlyList<string> include, IReadOnlyList<string> exclude)`: full paths ordered by relative path, ordinal; no side effects
    - `IReadOnlyCollection<IFile> Select(…same…)`: throws `CakeException` when nothing matches, otherwise sets `+x` on Unix and returns `IFile`s
    - `static void MakeExecutable(string path)`: no-op on Windows

- [ ] **Step 1: Write the failing tests**

`test/Cake.Download.Module.Tests/Installation/GlobMatcherTests.cs`:

```csharp
using Cake.Core;
using Cake.Download.Module.Installation;

namespace Cake.Download.Module.Tests.Installation;

public sealed class GlobMatcherTests
{
    [Theory]
    [InlineData("**/tool", "tool", true)]
    [InlineData("**/tool", "a/b/tool", true)]
    [InlineData("**/tool", "a/tool.txt", false)]
    [InlineData("**/tool", "a/xtool", false)]
    [InlineData("bin/*", "bin/x", true)]
    [InlineData("bin/*", "bin/a/x", false)]
    [InlineData("bin/**", "bin/a/x", true)]
    [InlineData("*.exe", "a.exe", true)]
    [InlineData("*.exe", "d/a.exe", false)]
    [InlineData("**/*.exe", "d/e/a.exe", true)]
    [InlineData("tool?", "tool1", true)]
    [InlineData("tool?", "tool12", false)]
    [InlineData("./bin/tool", "bin/tool", true)]
    [InlineData("a+b/(x)", "a+b/(x)", true)]
    [InlineData("jq", "jq", true)]
    public void IsMatch_Matches_Relative_Paths(string pattern, string path, bool expected)
    {
        Assert.Equal(expected, new GlobMatcher(pattern, ignoreCase: false).IsMatch(path));
    }

    [Fact]
    public void IsMatch_Honours_Case_Sensitivity_And_Backslashes()
    {
        Assert.True(new GlobMatcher("**/Tool.EXE", ignoreCase: true).IsMatch("BIN\\tool.exe"));
        Assert.False(new GlobMatcher("**/Tool", ignoreCase: false).IsMatch("bin/tool"));
    }

    [Fact]
    public void Constructor_Rejects_Patterns_Leaving_The_Install_Folder()
    {
        var exception = Assert.Throws<CakeException>(() => new GlobMatcher("../other/tool", ignoreCase: false));

        Assert.Equal("The glob '../other/tool' must stay inside the install folder ('..' is not allowed).", exception.Message);
    }
}
```

`test/Cake.Download.Module.Tests/Installation/FileSelectorTests.cs`:

```csharp
using Cake.Core;
using Cake.Core.IO;
using Cake.Download.Module.Installation;
using Cake.Download.Module.Tests.Fakes;

namespace Cake.Download.Module.Tests.Installation;

public sealed class FileSelectorTests : IDisposable
{
    private readonly TestDirectory _directory = new();
    private readonly FileSelector _selector = new(new FileSystem());

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void Select_Finds_The_Default_Include_In_A_Nested_Folder()
    {
        Write("gh_2.62.0_linux_amd64/bin/gh", "gh_2.62.0_linux_amd64/LICENSE", ".cake-download.json");

        var files = _selector.Select(_directory.Root, ["**/gh"], []);

        Assert.Equal(_directory.Combine("gh_2.62.0_linux_amd64", "bin", "gh"), Assert.Single(files).Path.FullPath.Replace('/', Path.DirectorySeparatorChar));
    }

    [Fact]
    public void Select_Returns_Every_Match_In_Ordinal_Order_And_Applies_Exclude()
    {
        Write("tools/b", "tools/a", "tools/a.txt", "other/c");

        var matches = _selector.Match(_directory.Root, ["tools/*"], ["**/*.txt"]);

        Assert.Equal([_directory.Combine("tools", "a"), _directory.Combine("tools", "b")], matches);
    }

    [Fact]
    public void Select_Never_Returns_The_Marker()
    {
        Write(".cake-download.json");

        Assert.Empty(_selector.Match(_directory.Root, ["**/*"], []));
    }

    [Fact]
    public void Select_Explains_An_Empty_Selection_With_The_Folder_Contents()
    {
        Write("bin/tool.exe", "README.md");

        var exception = Assert.Throws<CakeException>(() => _selector.Select(_directory.Root, ["**/jq"], ["**/*.md"]));

        Assert.Equal(
            $"No files in {_directory.Root} match include '**/jq' and exclude '**/*.md'. Contents: README.md, bin/tool.exe. Use 'include=' to choose the files to register.",
            exception.Message);
    }

    [Fact]
    public void Select_Makes_Selected_Files_Executable_On_Unix()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes only.");
            return;
        }

        Write("bin/tool", "bin/data");

        _selector.Select(_directory.Root, ["bin/tool"], []);

        Assert.True(File.GetUnixFileMode(_directory.Combine("bin", "tool")).HasFlag(UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute));
        Assert.False(File.GetUnixFileMode(_directory.Combine("bin", "data")).HasFlag(UnixFileMode.UserExecute));
    }

    private void Write(params string[] relativePaths)
    {
        foreach (var relativePath in relativePaths)
        {
            var path = _directory.Combine(relativePath.Split('/'));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, relativePath);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project test/Cake.Download.Module.Tests/Cake.Download.Module.Tests.csproj --filter-class "*GlobMatcherTests"`
Expected: build fails with `CS0246: The type or namespace name 'GlobMatcher' could not be found`.

- [ ] **Step 3: Implement the glob matcher**

`src/Cake.Download.Module/Installation/GlobMatcher.cs`:

```csharp
using System.Text;
using System.Text.RegularExpressions;
using Cake.Core;

namespace Cake.Download.Module.Installation;

/// <summary>
/// Matches '/'-separated relative paths against a glob: <c>**</c> spans folders, <c>*</c> and <c>?</c> stay inside one.
/// </summary>
internal sealed class GlobMatcher
{
    private readonly Regex _regex;

    public GlobMatcher(string pattern, bool ignoreCase)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        var normalized = pattern.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        normalized = normalized.TrimStart('/');
        if (normalized.Split('/').Contains(".."))
        {
            throw new CakeException($"The glob '{pattern}' must stay inside the install folder ('..' is not allowed).");
        }

        Pattern = pattern;
        var options = RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
        _regex = new Regex("^" + ToRegex(normalized) + "$", options);
    }

    public string Pattern { get; }

    public bool IsMatch(string relativePath) => _regex.IsMatch(relativePath.Replace('\\', '/'));

    private static string ToRegex(string glob)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            if (c == '*' && i + 1 < glob.Length && glob[i + 1] == '*')
            {
                var atSegmentStart = i == 0 || glob[i - 1] == '/';
                var followedBySlash = i + 2 < glob.Length && glob[i + 2] == '/';
                if (atSegmentStart && followedBySlash)
                {
                    builder.Append("(?:.*/)?");
                    i += 2;
                }
                else
                {
                    builder.Append(".*");
                    i += 1;
                }
            }
            else if (c == '*')
            {
                builder.Append("[^/]*");
            }
            else if (c == '?')
            {
                builder.Append("[^/]");
            }
            else
            {
                builder.Append(Regex.Escape(c.ToString()));
            }
        }

        return builder.ToString();
    }
}
```

- [ ] **Step 4: Implement the file selector**

`src/Cake.Download.Module/Installation/FileSelector.cs`:

```csharp
using Cake.Core;
using Cake.Core.IO;

namespace Cake.Download.Module.Installation;

/// <summary>
/// Chooses the files of an install folder that are registered with Cake's tool locator.
/// </summary>
internal sealed class FileSelector
{
    public const string MarkerFileName = ".cake-download.json";

    private const int ListingLimit = 50;

    private const UnixFileMode ExecuteBits = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

    private readonly IFileSystem _fileSystem;

    public FileSelector(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var mode = File.GetUnixFileMode(path);
        if ((mode & ExecuteBits) != ExecuteBits)
        {
            File.SetUnixFileMode(path, mode | ExecuteBits);
        }
    }

    public IReadOnlyList<string> Match(string installDirectory, IReadOnlyList<string> include, IReadOnlyList<string> exclude) =>
        Candidates(installDirectory, include, exclude).Selected.Select(file => file.Full).ToList();

    public IReadOnlyCollection<IFile> Select(string installDirectory, IReadOnlyList<string> include, IReadOnlyList<string> exclude)
    {
        var (all, selected) = Candidates(installDirectory, include, exclude);
        if (selected.Count == 0)
        {
            var excluded = exclude.Count > 0 ? $" and exclude '{string.Join("', '", exclude)}'" : string.Empty;
            var heading = all.Count > ListingLimit ? $"Contents (first {ListingLimit})" : "Contents";
            var listing = all.Count == 0 ? "(empty)" : string.Join(", ", all.Take(ListingLimit).Select(file => file.Relative));
            throw new CakeException(
                $"No files in {installDirectory} match include '{string.Join("', '", include)}'{excluded}. " +
                $"{heading}: {listing}. Use 'include=' to choose the files to register.");
        }

        foreach (var file in selected)
        {
            MakeExecutable(file.Full);
        }

        return selected.Select(file => _fileSystem.GetFile(new FilePath(file.Full))).ToList();
    }

    private static (List<(string Full, string Relative)> All, List<(string Full, string Relative)> Selected) Candidates(
        string installDirectory,
        IReadOnlyList<string> include,
        IReadOnlyList<string> exclude)
    {
        var ignoreCase = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();
        var includes = include.Select(pattern => new GlobMatcher(pattern, ignoreCase)).ToList();
        var excludes = exclude.Select(pattern => new GlobMatcher(pattern, ignoreCase)).ToList();

        var all = Directory.EnumerateFiles(installDirectory, "*", SearchOption.AllDirectories)
            .Select(full => (Full: full, Relative: Path.GetRelativePath(installDirectory, full).Replace('\\', '/')))
            .Where(file => file.Relative != MarkerFileName)
            .OrderBy(file => file.Relative, StringComparer.Ordinal)
            .ToList();
        var selected = all
            .Where(file => includes.Any(matcher => matcher.IsMatch(file.Relative)) && !excludes.Any(matcher => matcher.IsMatch(file.Relative)))
            .ToList();
        return (all, selected);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --project test/Cake.Download.Module.Tests/Cake.Download.Module.Tests.csproj --filter-class "*GlobMatcherTests"` and again with `--filter-class "*FileSelectorTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ test/
git commit -F - <<'EOF'
Select registered files with include/exclude globs

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 9: Install store (marker, cache check, staging, atomic publish)

**Files:**
- Create: `src/Cake.Download.Module/Installation/InstallMarker.cs`, `src/Cake.Download.Module/Installation/StagingArea.cs`, `src/Cake.Download.Module/Installation/InstallStore.cs`
- Create (test helper): `test/Cake.Download.Module.Tests/Fakes/FixedTimeProvider.cs`
- Test: `test/Cake.Download.Module.Tests/Installation/InstallStoreTests.cs`

**Interfaces:**
- Consumes: `DownloadPlan`, `ArchiveFormats`, `IntegrityPlan.Fingerprint`, `PinnedSha256` (Task 4); `PlatformInfo.FromRid` (Task 1); `FileSelector.MarkerFileName` (Task 8); `TestDirectory` (Task 5).
- Produces (namespace `Cake.Download.Module.Installation`):
  - `sealed record InstallMarker(int Schema, string Package, string Version, string Rid, string Url, string Format, string? FileName, string Integrity, string Sha256, DateTimeOffset InstalledAt)`:
    - `const int CurrentSchema = 1`
    - `static InstallMarker For(DownloadPlan plan, string sha256, DateTimeOffset installedAt)`
    - `bool Matches(DownloadPlan plan)`
  - `sealed class StagingArea : IDisposable`: `string Root`, `string DownloadDirectory`, `string ContentDirectory`. `Dispose` deletes `Root` (best effort).
  - `sealed class InstallStore(string toolsDirectory, TimeProvider time[, Action<TimeSpan> sleep])`:
    - `string GetInstallDirectory(DownloadPlan)`
    - `bool IsCurrent(DownloadPlan)`
    - `static InstallMarker? ReadMarker(string directory)`
    - `StagingArea CreateStaging(DownloadPlan)`: deletes this package's `.tmp-*` folders older than 1 hour first
    - `InstallMarker CreateMarker(DownloadPlan, string sha256)`
    - `void Publish(DownloadPlan plan, StagingArea staging, InstallMarker marker, bool replaceCurrent = false)`
    - `static void TryDeleteDirectory(string path)`
  - Test helper `FixedTimeProvider(DateTimeOffset now) : TimeProvider`.

- [ ] **Step 1: Write the failing tests**

`test/Cake.Download.Module.Tests/Fakes/FixedTimeProvider.cs`:

```csharp
namespace Cake.Download.Module.Tests.Fakes;

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
```

`test/Cake.Download.Module.Tests/Installation/InstallStoreTests.cs`:

```csharp
using Cake.Core.Packaging;
using Cake.Download.Module.Directives;
using Cake.Download.Module.Installation;
using Cake.Download.Module.Platforms;
using Cake.Download.Module.Tests.Fakes;

namespace Cake.Download.Module.Tests.Installation;

public sealed class InstallStoreTests : IDisposable
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly TestDirectory _directory = new();
    private readonly InstallStore _store;
    private readonly DownloadPlan _plan = DownloadPlanner.Create(
        DirectiveParser.Parse(new PackageReference("download:https://example.com/jq-{os}-{arch}?package=jq&version=1.8.2&sha256=" + Hash)),
        PlatformInfo.FromRid("linux-x64"));

    public InstallStoreTests()
    {
        _store = new InstallStore(_directory.Root, new FixedTimeProvider(Now), _ => { });
    }

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void GetInstallDirectory_Is_Package_Dot_Version_Under_The_Tools_Folder()
    {
        Assert.Equal(_directory.Combine("jq.1.8.2"), _store.GetInstallDirectory(_plan));
    }

    [Fact]
    public void IsCurrent_Is_False_Without_A_Marker_And_True_After_Publish()
    {
        Assert.False(_store.IsCurrent(_plan));

        PublishWithFile(_plan, "jq", "v1");

        Assert.True(_store.IsCurrent(_plan));
        Assert.Equal("v1", File.ReadAllText(_directory.Combine("jq.1.8.2", "jq")));
    }

    [Fact]
    public void Publish_Writes_The_Marker()
    {
        PublishWithFile(_plan, "jq", "v1");

        var marker = InstallStore.ReadMarker(_directory.Combine("jq.1.8.2"))!;
        Assert.Equal(new InstallMarker(1, "jq", "1.8.2", "linux-x64", "https://example.com/jq-linux-amd64", "file", "jq", "sha256:" + Hash, Hash, Now), marker);

        var json = File.ReadAllText(_directory.Combine("jq.1.8.2", ".cake-download.json"));
        Assert.Contains("\"schema\": 1", json);
        Assert.Contains("\"integrity\": \"sha256:" + Hash + "\"", json);
    }

    public static TheoryData<string> ChangedFields => ["url", "integrity", "rid", "fileName", "format"];

    [Theory]
    [MemberData(nameof(ChangedFields))]
    public void IsCurrent_Is_False_When_A_Compared_Field_Changes(string field)
    {
        PublishWithFile(_plan, "jq", "v1");

        var changed = field switch
        {
            "url" => _plan with { Url = new Uri("https://example.com/other") },
            "integrity" => _plan with { Integrity = new PinnedSha256(new string('f', 64), "sha256") },
            "rid" => _plan with { Platform = PlatformInfo.FromRid("linux-arm64") },
            "fileName" => _plan with { FileName = "jq2" },
            _ => _plan with { Format = ArchiveFormat.Zip, FileName = null },
        };

        Assert.False(_store.IsCurrent(changed));
    }

    [Fact]
    public void IsCurrent_Is_False_For_An_Unreadable_Marker()
    {
        Directory.CreateDirectory(_directory.Combine("jq.1.8.2"));
        File.WriteAllText(_directory.Combine("jq.1.8.2", ".cake-download.json"), "{ not json");

        Assert.False(_store.IsCurrent(_plan));
    }

    [Fact]
    public void Publish_Replaces_A_Stale_Install()
    {
        Directory.CreateDirectory(_directory.Combine("jq.1.8.2"));
        File.WriteAllText(_directory.Combine("jq.1.8.2", "old-file"), "stale");

        PublishWithFile(_plan, "jq", "v1");

        Assert.False(File.Exists(_directory.Combine("jq.1.8.2", "old-file")));
        Assert.True(_store.IsCurrent(_plan));
    }

    [Fact]
    public void Publish_Keeps_A_Matching_Install_Published_By_Another_Build()
    {
        PublishWithFile(_plan, "jq", "first");
        PublishWithFile(_plan, "jq", "second");

        Assert.Equal("first", File.ReadAllText(_directory.Combine("jq.1.8.2", "jq")));
    }

    [Fact]
    public void Publish_With_ReplaceCurrent_Replaces_A_Matching_Install()
    {
        PublishWithFile(_plan, "jq", "first");
        PublishWithFile(_plan, "jq", "second", replaceCurrent: true);

        Assert.Equal("second", File.ReadAllText(_directory.Combine("jq.1.8.2", "jq")));
    }

    [Fact]
    public void Staging_Is_Removed_On_Dispose_And_Stale_Staging_Is_Cleaned_Up()
    {
        var stale = Directory.CreateDirectory(_directory.Combine(".jq.1.8.2.tmp-old")).FullName;
        Directory.SetLastWriteTimeUtc(stale, Now.UtcDateTime.AddHours(-2));
        var fresh = Directory.CreateDirectory(_directory.Combine(".jq.1.8.2.tmp-fresh")).FullName;
        Directory.SetLastWriteTimeUtc(fresh, Now.UtcDateTime.AddMinutes(-10));

        string root;
        using (var staging = _store.CreateStaging(_plan))
        {
            root = staging.Root;
            Assert.True(Directory.Exists(staging.DownloadDirectory));
            Assert.True(Directory.Exists(staging.ContentDirectory));
            Assert.StartsWith(_directory.Combine(".jq.1.8.2.tmp-"), root);
        }

        Assert.False(Directory.Exists(root));
        Assert.False(Directory.Exists(stale));
        Assert.True(Directory.Exists(fresh));
    }

    private void PublishWithFile(DownloadPlan plan, string fileName, string content, bool replaceCurrent = false)
    {
        using var staging = _store.CreateStaging(plan);
        File.WriteAllText(Path.Combine(staging.ContentDirectory, fileName), content);
        _store.Publish(plan, staging, _store.CreateMarker(plan, Hash), replaceCurrent);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project test/Cake.Download.Module.Tests/Cake.Download.Module.Tests.csproj --filter-class "*InstallStoreTests"`
Expected: build fails with `CS0246: The type or namespace name 'InstallStore' could not be found`.

- [ ] **Step 3: Implement the marker and the staging area**

`src/Cake.Download.Module/Installation/InstallMarker.cs`:

```csharp
using Cake.Download.Module.Directives;

namespace Cake.Download.Module.Installation;

/// <summary>
/// The provenance written to <c>.cake-download.json</c>; an install is current when it matches the plan.
/// </summary>
internal sealed record InstallMarker(
    int Schema,
    string Package,
    string Version,
    string Rid,
    string Url,
    string Format,
    string? FileName,
    string Integrity,
    string Sha256,
    DateTimeOffset InstalledAt)
{
    public const int CurrentSchema = 1;

    public static InstallMarker For(DownloadPlan plan, string sha256, DateTimeOffset installedAt) => new(
        CurrentSchema,
        plan.Package,
        plan.Version,
        plan.Platform.Rid,
        plan.Url.AbsoluteUri,
        ArchiveFormats.ToName(plan.Format),
        plan.FileName,
        plan.Integrity.Fingerprint,
        sha256,
        installedAt);

    public bool Matches(DownloadPlan plan) =>
        Schema == CurrentSchema
        && Rid == plan.Platform.Rid
        && Url == plan.Url.AbsoluteUri
        && Format == ArchiveFormats.ToName(plan.Format)
        && FileName == plan.FileName
        && Integrity == plan.Integrity.Fingerprint;
}
```

`src/Cake.Download.Module/Installation/StagingArea.cs`:

```csharp
namespace Cake.Download.Module.Installation;

internal sealed class StagingArea : IDisposable
{
    public StagingArea(string root)
    {
        Root = root;
        DownloadDirectory = Path.Combine(root, "download");
        ContentDirectory = Path.Combine(root, "content");
        Directory.CreateDirectory(DownloadDirectory);
        Directory.CreateDirectory(ContentDirectory);
    }

    public string Root { get; }

    public string DownloadDirectory { get; }

    public string ContentDirectory { get; }

    public void Dispose() => InstallStore.TryDeleteDirectory(Root);
}
```

- [ ] **Step 4: Implement the store**

`src/Cake.Download.Module/Installation/InstallStore.cs`:

```csharp
using System.Text.Json;
using Cake.Core;
using Cake.Download.Module.Directives;

namespace Cake.Download.Module.Installation;

/// <summary>
/// Owns <c>&lt;tools&gt;/&lt;package&gt;.&lt;version&gt;/</c>: decides whether an install is current, stages new installs
/// next to it and publishes them with an atomic directory move. Concurrent builds are handled optimistically: the
/// first matching publish wins and later ones discard their staging.
/// </summary>
internal sealed class InstallStore
{
    private const int MaxPublishAttempts = 5;

    private static readonly TimeSpan StaleStagingAge = TimeSpan.FromHours(1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _toolsDirectory;
    private readonly TimeProvider _time;
    private readonly Action<TimeSpan> _sleep;

    public InstallStore(string toolsDirectory, TimeProvider time)
        : this(toolsDirectory, time, Thread.Sleep)
    {
    }

    public InstallStore(string toolsDirectory, TimeProvider time, Action<TimeSpan> sleep)
    {
        _toolsDirectory = toolsDirectory ?? throw new ArgumentNullException(nameof(toolsDirectory));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _sleep = sleep ?? throw new ArgumentNullException(nameof(sleep));
    }

    public static InstallMarker? ReadMarker(string directory)
    {
        var path = Path.Combine(directory, FileSelector.MarkerFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<InstallMarker>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception exception) when (exception is JsonException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    public static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover folder is cleaned up by a later install.
        }
    }

    public string GetInstallDirectory(DownloadPlan plan) => Path.Combine(_toolsDirectory, plan.FolderName);

    public bool IsCurrent(DownloadPlan plan) => ReadMarker(GetInstallDirectory(plan))?.Matches(plan) == true;

    public InstallMarker CreateMarker(DownloadPlan plan, string sha256) => InstallMarker.For(plan, sha256, _time.GetUtcNow());

    public StagingArea CreateStaging(DownloadPlan plan)
    {
        Directory.CreateDirectory(_toolsDirectory);
        var cutoff = (_time.GetUtcNow() - StaleStagingAge).UtcDateTime;
        foreach (var leftover in Directory.EnumerateDirectories(_toolsDirectory, $".{plan.FolderName}.tmp-*"))
        {
            if (Directory.GetLastWriteTimeUtc(leftover) < cutoff)
            {
                TryDeleteDirectory(leftover);
            }
        }

        return new StagingArea(Path.Combine(_toolsDirectory, $".{plan.FolderName}.tmp-{Guid.NewGuid():N}"));
    }

    public void Publish(DownloadPlan plan, StagingArea staging, InstallMarker marker, bool replaceCurrent = false)
    {
        File.WriteAllText(Path.Combine(staging.ContentDirectory, FileSelector.MarkerFileName), JsonSerializer.Serialize(marker, JsonOptions));

        var final = GetInstallDirectory(plan);
        for (var attempt = 1; ; attempt++)
        {
            if (Directory.Exists(final))
            {
                if (!replaceCurrent && IsCurrent(plan))
                {
                    return;
                }

                replaceCurrent = false;
                TryDeleteDirectory(final);
            }

            try
            {
                Directory.Move(staging.ContentDirectory, final);
                return;
            }
            catch (Exception exception) when (attempt < MaxPublishAttempts && exception is IOException or UnauthorizedAccessException)
            {
                _sleep(TimeSpan.FromMilliseconds(200 * attempt));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new CakeException($"Could not move the new install of {plan.Package} {plan.Version} into {final}: {exception.Message}", exception);
            }
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --project test/Cake.Download.Module.Tests/Cake.Download.Module.Tests.csproj --filter-class "*InstallStoreTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ test/
git commit -F - <<'EOF'
Add install store with marker, staging and atomic publish

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 10: Package installer and module

**Files:**
- Create: `src/Cake.Download.Module/DownloadPackageInstaller.cs`, `src/Cake.Download.Module/DownloadModule.cs`
- Create (test helpers): `test/Cake.Download.Module.Tests/Fakes/FixedPlatformDetector.cs`, `test/Cake.Download.Module.Tests/Fakes/RecordingRegistrar.cs`
- Test: `test/Cake.Download.Module.Tests/DownloadPackageInstallerTests.cs`, `test/Cake.Download.Module.Tests/DownloadModuleTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–9:
  - `DirectiveParser.Parse`, `DownloadPlanner.Create`
  - `IPlatformDetector`, `PlatformDetector`
  - `HttpDownloader`, `DownloadOptions`, `DownloadHttpException`
  - `IntegrityResolver.Resolve/Verify`
  - `ArchiveExtractor.Extract`
  - `FileSelector.Match/Select/MakeExecutable`
  - `InstallStore`
  - test helpers `FakeHttpHandler`, `TestDirectory`, `TestHashes`, `TestArchives`
- Produces (namespace `Cake.Download.Module`, **public**):
  - `public sealed class DownloadPackageInstaller : IPackageInstaller`:
    - public ctor `(ICakeEnvironment environment, IFileSystem fileSystem, ICakeLog log)`
    - internal ctor `(ICakeEnvironment, IFileSystem, ICakeLog, IPlatformDetector, HttpMessageHandler, DownloadOptions, TimeProvider)`
  - `public sealed class DownloadModule : ICakeModule`
  - `[assembly: CakeModule(typeof(DownloadModule))]`

- [ ] **Step 1: Write the test helpers**

`test/Cake.Download.Module.Tests/Fakes/FixedPlatformDetector.cs`:

```csharp
using Cake.Download.Module.Platforms;

namespace Cake.Download.Module.Tests.Fakes;

internal sealed class FixedPlatformDetector(PlatformInfo platform) : IPlatformDetector
{
    public PlatformInfo Detect() => platform;
}
```

`test/Cake.Download.Module.Tests/Fakes/RecordingRegistrar.cs`:

```csharp
using Cake.Core.Composition;

namespace Cake.Download.Module.Tests.Fakes;

internal sealed class RecordingRegistrar : ICakeContainerRegistrar
{
    public List<RecordingRegistration> Registrations { get; } = [];

    public ICakeRegistrationBuilder RegisterType(Type type)
    {
        var registration = new RecordingRegistration(type);
        Registrations.Add(registration);
        return registration;
    }

    public ICakeRegistrationBuilder RegisterInstance<TImplementation>(TImplementation instance)
        where TImplementation : class => RegisterType(typeof(TImplementation));
}

internal sealed class RecordingRegistration(Type implementation) : ICakeRegistrationBuilder
{
    public Type Implementation { get; } = implementation;

    public List<Type> Services { get; } = [];

    public bool IsSingleton { get; private set; }

    public ICakeRegistrationBuilder As(Type type)
    {
        Services.Add(type);
        return this;
    }

    public ICakeRegistrationBuilder AsSelf() => As(Implementation);

    public ICakeRegistrationBuilder Singleton()
    {
        IsSingleton = true;
        return this;
    }

    public ICakeRegistrationBuilder Transient()
    {
        IsSingleton = false;
        return this;
    }
}
```

- [ ] **Step 2: Write the failing tests**

`test/Cake.Download.Module.Tests/DownloadModuleTests.cs`:

```csharp
using System.Reflection;
using Cake.Core.Annotations;
using Cake.Core.Packaging;
using Cake.Download.Module.Tests.Fakes;

namespace Cake.Download.Module.Tests;

public sealed class DownloadModuleTests
{
    [Fact]
    public void Assembly_Declares_The_Module()
    {
        var attribute = typeof(DownloadModule).Assembly.GetCustomAttribute<CakeModuleAttribute>();

        Assert.Equal(typeof(DownloadModule), attribute?.ModuleType);
    }

    [Fact]
    public void Register_Adds_The_Installer_As_A_Singleton_Package_Installer()
    {
        var registrar = new RecordingRegistrar();

        new DownloadModule().Register(registrar);

        var registration = Assert.Single(registrar.Registrations);
        Assert.Equal(typeof(DownloadPackageInstaller), registration.Implementation);
        Assert.Equal([typeof(IPackageInstaller)], registration.Services);
        Assert.True(registration.IsSingleton);
    }
}
```

`test/Cake.Download.Module.Tests/DownloadPackageInstallerTests.cs`:

```csharp
using System.Net;
using Cake.Core;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using Cake.Core.Packaging;
using Cake.Download.Module.Http;
using Cake.Download.Module.Platforms;
using Cake.Download.Module.Tests.Fakes;
using Cake.Testing;

namespace Cake.Download.Module.Tests;

public sealed class DownloadPackageInstallerTests : IDisposable
{
    private const string RawContent = "#!/bin/sh\necho jq\n";
    private const string JqUrl = "https://example.com/jq-linux-amd64";
    private const string JqDirective = "download:https://example.com/jq-{os}-{arch}?package=jq&version=1.8.2";

    private readonly TestDirectory _directory = new();
    private readonly FakeHttpHandler _handler = new();
    private readonly FakeLog _log = new();
    private readonly string _tools;

    public DownloadPackageInstallerTests()
    {
        _tools = _directory.Combine("tools");
        _handler.Respond(JqUrl, FakeHttpHandler.Ok(RawContent));
    }

    public void Dispose() => _directory.Dispose();

    [Theory]
    [InlineData("download:https://example.com/a?package=a", PackageType.Tool, true)]
    [InlineData("DOWNLOAD:https://example.com/a?package=a", PackageType.Tool, true)]
    [InlineData("download:https://example.com/a?package=a", PackageType.Addin, false)]
    [InlineData("download:https://example.com/a?package=a", PackageType.Module, false)]
    [InlineData("nuget:?package=a", PackageType.Tool, false)]
    public void CanInstall_Accepts_Only_Download_Tools(string uri, PackageType type, bool expected)
    {
        Assert.Equal(expected, CreateInstaller().CanInstall(new PackageReference(uri), type));
    }

    [Fact]
    public void Install_Registers_A_Raw_Download_Under_The_Package_Name()
    {
        var file = Assert.Single(Install(JqDirective + "&sha256=" + TestHashes.Sha256(RawContent)));

        Assert.EndsWith("/tools/jq.1.8.2/jq", file.Path.FullPath, StringComparison.Ordinal);
        Assert.Equal(RawContent, File.ReadAllText(file.Path.FullPath));
        Assert.True(File.Exists(Path.Combine(_tools, "jq.1.8.2", ".cake-download.json")));
        Assert.Contains(_log.Entries, entry => entry.Level == LogLevel.Information && entry.Message == $"Downloading jq 1.8.2 (linux-x64) from {JqUrl}");
    }

    [Fact]
    public void Install_Uses_The_Exe_Name_On_Windows()
    {
        _handler.Respond("https://example.com/jq-windows-amd64.exe", FakeHttpHandler.Ok(RawContent));

        var file = Assert.Single(Install("download:https://example.com/jq-{os}-{arch}{exe}?package=jq&version=1.8.2&sha256=" + TestHashes.Sha256(RawContent), "win-x64"));

        Assert.EndsWith("/jq.1.8.2/jq.exe", file.Path.FullPath, StringComparison.Ordinal);
    }

    [Fact]
    public void Install_Extracts_An_Archive_And_Registers_The_Default_Include()
    {
        var archive = TestArchives.Tar(
            gzip: true,
            new ArchiveEntrySpec("tool-1.0/bin/tool", "binary"),
            new ArchiveEntrySpec("tool-1.0/README.md", "docs"));
        _handler.Respond("https://example.com/tool-1.0.tar.gz", FakeHttpHandler.Ok(archive));

        var file = Assert.Single(Install("download:https://example.com/tool-{version}.tar.gz?package=tool&version=1.0&sha256=" + TestHashes.Sha256(archive)));

        Assert.EndsWith("/tools/tool.1.0/tool-1.0/bin/tool", file.Path.FullPath, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(_tools, "tool.1.0", "tool-1.0", "README.md")));
    }

    [Fact]
    public void Install_Twice_Makes_No_Second_Request()
    {
        var directive = JqDirective + "&sha256=" + TestHashes.Sha256(RawContent);
        Install(directive);
        var requests = _handler.RequestedUrls.Count;

        var file = Assert.Single(Install(directive));

        Assert.Equal(requests, _handler.RequestedUrls.Count);
        Assert.True(File.Exists(file.Path.FullPath));
        Assert.Contains(_log.Entries, entry => entry.Message.StartsWith("jq 1.8.2 (linux-x64) is already installed in ", StringComparison.Ordinal));
    }

    [Fact]
    public void Install_Reinstalls_When_The_Integrity_Option_Changes()
    {
        Install(JqDirective + "&sha256=skip");
        Install(JqDirective + "&sha256=" + TestHashes.Sha256(RawContent));

        Assert.Equal(2, _handler.RequestedUrls.Count);
    }

    [Fact]
    public void Install_Reinstalls_When_The_Registered_File_Was_Deleted()
    {
        var directive = JqDirective + "&sha256=" + TestHashes.Sha256(RawContent);
        var path = Assert.Single(Install(directive)).Path.FullPath;
        File.Delete(path);

        Install(directive);

        Assert.Equal(2, _handler.RequestedUrls.Count);
        Assert.Equal(RawContent, File.ReadAllText(path));
    }

    [Fact]
    public void Install_Works_With_Spaces_In_The_Tools_Path()
    {
        var tools = _directory.Combine("My Tools");

        var file = Assert.Single(Install(JqDirective + "&sha256=" + TestHashes.Sha256(RawContent), tools: tools));

        Assert.EndsWith("/My Tools/jq.1.8.2/jq", file.Path.FullPath, StringComparison.Ordinal);
    }

    [Fact]
    public void Install_Rejects_A_Hash_Mismatch_And_Leaves_Nothing_Behind()
    {
        var exception = Assert.Throws<CakeException>(() => Install(JqDirective + "&sha256=" + new string('f', 64)));

        Assert.StartsWith($"SHA-256 mismatch for {JqUrl}.", exception.Message);
        Assert.Empty(Directory.GetFileSystemEntries(_tools));
    }

    [Theory]
    [InlineData(JqDirective, "sha256.linux-x64")]
    [InlineData("download:https://example.com/jq-linux-amd64?package=jq&version=1.8.2", "sha256")]
    public void Install_Without_Integrity_Fails_With_The_Parameter_To_Paste(string directive, string parameter)
    {
        var exception = Assert.Throws<CakeException>(() => Install(directive));

        Assert.Contains($"Add '&{parameter}={TestHashes.Sha256(RawContent)}' to the directive", exception.Message);
        Assert.False(Directory.Exists(Path.Combine(_tools, "jq.1.8.2")));
    }

    [Fact]
    public void Install_With_Skip_Logs_A_Warning()
    {
        Install(JqDirective + "&sha256=skip");

        Assert.Contains(_log.Entries, entry => entry.Level == LogLevel.Warning
            && entry.Message == $"Integrity verification is disabled for jq 1.8.2 (sha256=skip). The downloaded file has SHA-256 {TestHashes.Sha256(RawContent)}.");
    }

    [Fact]
    public void Install_Explains_A_404_With_The_Expanded_Placeholders()
    {
        var exception = Assert.Throws<CakeException>(() => Install(JqDirective + "&sha256=skip", "osx-arm64"));

        Assert.StartsWith("https://example.com/jq-darwin-arm64 was not found (HTTP 404). Detected platform osx-arm64; dialect 'go' expanded {os}='darwin', {arch}='arm64', {rid}='osx-arm64', {exe}='', {archive}='tar.gz'.", exception.Message);
        Assert.Contains("'url.osx-arm64='", exception.Message);
    }

    [Fact]
    public async Task Concurrent_Installs_Both_Succeed_With_One_Install_Folder()
    {
        var directive = JqDirective + "&sha256=" + TestHashes.Sha256(RawContent);

        var results = await Task.WhenAll(
            Task.Run(() => Install(directive, log: new FakeLog())),
            Task.Run(() => Install(directive, log: new FakeLog())));

        Assert.All(results, files => Assert.True(File.Exists(Assert.Single(files).Path.FullPath)));
        Assert.Equal([Path.Combine(_tools, "jq.1.8.2")], Directory.GetDirectories(_tools));
    }

    private IReadOnlyCollection<IFile> Install(string directive, string rid = "linux-x64", string? tools = null, FakeLog? log = null) =>
        CreateInstaller(rid, log).Install(new PackageReference(directive), PackageType.Tool, new DirectoryPath(tools ?? _tools));

    private DownloadPackageInstaller CreateInstaller(string rid = "linux-x64", FakeLog? log = null) => new(
        FakeEnvironment.CreateUnixEnvironment(),
        new FileSystem(),
        log ?? _log,
        new FixedPlatformDetector(PlatformInfo.FromRid(rid)),
        _handler,
        new DownloadOptions { Delay = (_, _) => Task.CompletedTask },
        TimeProvider.System);
}
```

The concurrent test gives each installer its own `FakeLog`, because `FakeLog` is not thread-safe.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test --project test/Cake.Download.Module.Tests/Cake.Download.Module.Tests.csproj --filter-class "*DownloadPackageInstallerTests"`
Expected: build fails with `CS0246: The type or namespace name 'DownloadPackageInstaller' could not be found`.

- [ ] **Step 4: Implement the module**

`src/Cake.Download.Module/DownloadModule.cs`:

```csharp
using Cake.Core.Annotations;
using Cake.Core.Composition;
using Cake.Core.Packaging;
using Cake.Download.Module;

[assembly: CakeModule(typeof(DownloadModule))]

namespace Cake.Download.Module;

/// <summary>
/// The Cake module that adds the <c>download:</c> scheme for <c>#tool</c> and <c>InstallTool</c>.
/// </summary>
public sealed class DownloadModule : ICakeModule
{
    /// <summary>
    /// Registers <see cref="DownloadPackageInstaller"/> as a package installer.
    /// </summary>
    /// <param name="registrar">The container registrar.</param>
    public void Register(ICakeContainerRegistrar registrar)
    {
        ArgumentNullException.ThrowIfNull(registrar);

        registrar.RegisterType<DownloadPackageInstaller>().As<IPackageInstaller>().Singleton();
    }
}
```

- [ ] **Step 5: Implement the installer**

`src/Cake.Download.Module/DownloadPackageInstaller.cs`:

```csharp
using System.Net;
using Cake.Core;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using Cake.Core.Packaging;
using Cake.Download.Module.Archives;
using Cake.Download.Module.Directives;
using Cake.Download.Module.Http;
using Cake.Download.Module.Installation;
using Cake.Download.Module.Integrity;
using Cake.Download.Module.Platforms;

namespace Cake.Download.Module;

/// <summary>
/// Installs tools from <c>download:</c> package references: downloads a file over HTTPS, verifies its SHA-256,
/// extracts it when it is an archive and returns the files to register with Cake's tool locator.
/// </summary>
public sealed class DownloadPackageInstaller : IPackageInstaller
{
    private static readonly Lazy<HttpMessageHandler> SharedHandler = new(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    });

    private readonly ICakeEnvironment _environment;
    private readonly ICakeLog _log;
    private readonly IPlatformDetector _platformDetector;
    private readonly HttpDownloader _downloader;
    private readonly IntegrityResolver _integrity;
    private readonly FileSelector _fileSelector;
    private readonly TimeProvider _time;

    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadPackageInstaller"/> class.
    /// </summary>
    /// <param name="environment">The Cake environment.</param>
    /// <param name="fileSystem">The file system.</param>
    /// <param name="log">The log.</param>
    public DownloadPackageInstaller(ICakeEnvironment environment, IFileSystem fileSystem, ICakeLog log)
        : this(
            environment,
            fileSystem,
            log,
            new PlatformDetector(GetPlatform(environment)),
            SharedHandler.Value,
            DownloadOptions.Default,
            TimeProvider.System)
    {
    }

    internal DownloadPackageInstaller(
        ICakeEnvironment environment,
        IFileSystem fileSystem,
        ICakeLog log,
        IPlatformDetector platformDetector,
        HttpMessageHandler handler,
        DownloadOptions options,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _platformDetector = platformDetector ?? throw new ArgumentNullException(nameof(platformDetector));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _downloader = new HttpDownloader(handler, log, options);
        _integrity = new IntegrityResolver(_downloader);
        _fileSelector = new FileSelector(fileSystem);
    }

    /// <summary>
    /// Determines whether this installer handles the package: <c>download:</c> references of type tool.
    /// </summary>
    /// <param name="package">The package reference.</param>
    /// <param name="type">The package type.</param>
    /// <returns><see langword="true"/> for <c>download:</c> tools; otherwise <see langword="false"/>.</returns>
    public bool CanInstall(PackageReference package, PackageType type)
    {
        ArgumentNullException.ThrowIfNull(package);

        return type == PackageType.Tool && string.Equals(package.Scheme, DirectiveParser.Scheme, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Installs the tool into <c>&lt;path&gt;/&lt;package&gt;.&lt;version&gt;/</c>, unless an identical install is
    /// already there, and returns the files to register.
    /// </summary>
    /// <param name="package">The package reference.</param>
    /// <param name="type">The package type.</param>
    /// <param name="path">The tools directory.</param>
    /// <returns>The files to register with the tool locator.</returns>
    /// <exception cref="CakeException">The directive is invalid, or downloading, verifying or extracting failed.</exception>
    public IReadOnlyCollection<IFile> Install(PackageReference package, PackageType type, DirectoryPath path)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(path);

        var directive = DirectiveParser.Parse(package);
        var plan = DownloadPlanner.Create(directive, _platformDetector.Detect());
        var store = new InstallStore(path.MakeAbsolute(_environment).FullPath, _time);
        var installDirectory = store.GetInstallDirectory(plan);

        if (!store.IsCurrent(plan))
        {
            InstallFresh(plan, store, replaceCurrent: false);
        }
        else if (_fileSelector.Match(installDirectory, plan.Include, plan.Exclude).Count == 0)
        {
            _log.Verbose("{0} {1} ({2}) is installed in {3}, but no files match 'include'; reinstalling.", plan.Package, plan.Version, plan.Platform.Rid, installDirectory);
            InstallFresh(plan, store, replaceCurrent: true);
        }
        else
        {
            _log.Verbose("{0} {1} ({2}) is already installed in {3}.", plan.Package, plan.Version, plan.Platform.Rid, installDirectory);
        }

        var files = _fileSelector.Select(installDirectory, plan.Include, plan.Exclude);
        foreach (var file in files)
        {
            _log.Verbose("Registering {0}.", file.Path.FullPath);
        }

        return files;
    }

    private static ICakePlatform GetPlatform(ICakeEnvironment environment) =>
        environment?.Platform ?? throw new ArgumentNullException(nameof(environment));

    private static string NotFoundMessage(DownloadPlan plan)
    {
        var expanded = string.Join(", ", plan.Placeholders.Where(entry => entry.Key != "version").Select(entry => $"{{{entry.Key}}}='{entry.Value}'"));
        return $"{plan.Url.AbsoluteUri} was not found (HTTP 404). Detected platform {plan.Platform.Rid}; dialect '{plan.Dialect}' expanded {expanded}. " +
            "If the asset is named differently on this platform, add an override such as 'os.<value>=', 'arch.<value>=', 'archive.<value>=' " +
            $"or 'url.{plan.Platform.Rid}=' to the directive.";
    }

    private void InstallFresh(DownloadPlan plan, InstallStore store, bool replaceCurrent)
    {
        _log.Information("Downloading {0} {1} ({2}) from {3}", plan.Package, plan.Version, plan.Platform.Rid, plan.Url.AbsoluteUri);

        using var staging = store.CreateStaging(plan);
        var expected = _integrity.Resolve(plan, staging.DownloadDirectory);
        var assetPath = Path.Combine(staging.DownloadDirectory, "asset");

        DownloadResult download;
        try
        {
            download = _downloader.Download(plan.Url, assetPath);
        }
        catch (DownloadHttpException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new CakeException(NotFoundMessage(plan), exception);
        }

        IntegrityResolver.Verify(plan, expected, download.Sha256);
        if (plan.Integrity is SkippedIntegrity)
        {
            _log.Warning(
                "Integrity verification is disabled for {0} {1} (sha256=skip). The downloaded file has SHA-256 {2}.",
                plan.Package,
                plan.Version,
                download.Sha256);
        }
        else
        {
            _log.Verbose("Verified SHA-256 {0} of {1}.", download.Sha256, plan.AssetName);
        }

        if (plan.Format == ArchiveFormat.File)
        {
            var target = Path.Combine(staging.ContentDirectory, plan.FileName!);
            File.Move(assetPath, target);
            FileSelector.MakeExecutable(target);
        }
        else
        {
            ArchiveExtractor.Extract(assetPath, plan.Format, staging.ContentDirectory);
        }

        store.Publish(plan, staging, store.CreateMarker(plan, download.Sha256), replaceCurrent);
        _log.Verbose("Installed {0} {1} into {2}.", plan.Package, plan.Version, store.GetInstallDirectory(plan));
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --project test/Cake.Download.Module.Tests/Cake.Download.Module.Tests.csproj`
Expected: the whole suite passes.

Run: `dotnet build src/Cake.Download.Module.slnx -c Release`
Expected: 0 warnings, including XML documentation warnings for the public types.

- [ ] **Step 7: Commit**

```bash
git add src/ test/
git commit -F - <<'EOF'
Add download: package installer and Cake module

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 11: Build project (Build → Test → Pack + package verification)

**Files:**
- Create: `build.ps1`, `build.sh`
- Create: `build/Build.csproj`, `build/Build.slnx`, `build/Program.cs`, `build/BuildContext.cs`, `build/BuildLifetime.cs`, `build/PackageVerifier.cs`
- Create: `build/Tasks/BuildTask.cs`, `build/Tasks/TestTask.cs`, `build/Tasks/PackTask.cs`, `build/Tasks/AllTask.cs`, `build/Tasks/DefaultTask.cs`
- Create: `test/Build.Tests/Directory.Build.props`, `test/Build.Tests/Directory.Build.targets`, `test/Build.Tests/Directory.Packages.props`, `test/Build.Tests/Build.Tests.csproj`, `test/Build.Tests/PackageVerifierTests.cs`
- Modify: `README.md` (it must exist with content, since it is packed)

**Interfaces:**
- Consumes: `src/Cake.Download.Module.slnx` (Tasks 1–10); `build/MinVer.props` (Task 1).
- Produces:
  - `Build.BuildContext`: `const string Solution`, `DirectoryPath ArtifactsDirectory`, `FilePath PackageFile`.
  - `Build.PackageVerifier.Verify(string packagePath) → IReadOnlyList<string>` (public static).
  - Frosting tasks `Build`, `Test`, `Pack`, `All`, `Default`.
  - `ThisAssembly.PackageVersion` (generated by AssemblyMetadata.Generators).

- [ ] **Step 1: Create the build project skeleton**

`build.ps1`:

```powershell
dotnet run --project build/Build.csproj -- $args
exit $LASTEXITCODE;
```

`build.sh`:

```bash
#!/usr/bin/env bash
dotnet run --project ./build/Build.csproj -- "$@"
```

```bash
git update-index --add --chmod=+x build.sh 2>/dev/null || chmod +x build.sh
```

`build/Build.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="MinVer.props" />
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RunWorkingDirectory>$(MSBuildProjectDirectory)\..</RunWorkingDirectory>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="AssemblyMetadata.Generators" Version="2.2.0">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Cake.Frosting" Version="6.3.0" />
    <PackageReference Include="MinVer" Version="8.0.0" />
  </ItemGroup>
  <Target Name="PostMinVer" AfterTargets="MinVer">
    <ItemGroup>
      <AssemblyMetadata Include="PackageVersion" Value="$(PackageVersion)" />
    </ItemGroup>
  </Target>
</Project>
```

`build/Build.slnx`:

```xml
<Solution>
  <Project Path="Build.csproj" />
  <Project Path="../test/Build.Tests/Build.Tests.csproj" />
</Solution>
```

`build/Program.cs`:

```csharp
using Cake.Frosting;

namespace Build;

public static class Program
{
    public static int Main(string[] args)
    {
        return new CakeHost()
            .UseContext<BuildContext>()
            .UseLifetime<BuildLifetime>()
            .Run(args);
    }
}
```

`build/BuildContext.cs`:

```csharp
using Cake.Core;
using Cake.Core.IO;
using Cake.Frosting;

namespace Build;

public class BuildContext : FrostingContext
{
    public const string Solution = "./src/Cake.Download.Module.slnx";

    public BuildContext(ICakeContext context)
        : base(context)
    {
        ArtifactsDirectory = context.Environment.WorkingDirectory.Combine("artifacts");
    }

    /// <summary>Gets the directory packages are written to.</summary>
    public DirectoryPath ArtifactsDirectory { get; }

    /// <summary>Gets the package this build produces.</summary>
    public FilePath PackageFile => ArtifactsDirectory.CombineWithFilePath($"Cake.Download.Module.{ThisAssembly.PackageVersion}.nupkg");
}
```

`build/BuildLifetime.cs`:

```csharp
using Cake.Common.Diagnostics;
using Cake.Core;
using Cake.Frosting;

namespace Build;

internal sealed class BuildLifetime : FrostingLifetime<BuildContext>
{
    public override void Setup(BuildContext context, ISetupContext info)
    {
        context.Information("Product Version: {0}", ThisAssembly.PackageVersion);
    }

    public override void Teardown(BuildContext context, ITeardownContext info)
    {
    }
}
```

- [ ] **Step 2: Add the tasks**

`build/Tasks/BuildTask.cs`:

```csharp
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Build;
using Cake.Frosting;

namespace Build.Tasks;

[TaskName("Build")]
public sealed class BuildTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        context.DotNetBuild(BuildContext.Solution, new DotNetBuildSettings
        {
            Configuration = "Release",
            Verbosity = DotNetVerbosity.Minimal,
        });
    }
}
```

`build/Tasks/TestTask.cs`:

```csharp
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Test;
using Cake.Frosting;

namespace Build.Tasks;

[TaskName("Test")]
[IsDependentOn(typeof(BuildTask))]
public sealed class TestTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        context.DotNetTest(BuildContext.Solution, new DotNetTestSettings
        {
            Configuration = "Release",
            Verbosity = DotNetVerbosity.Minimal,
            NoBuild = true,
            PathType = DotNetTestPathType.Solution,
        });

        context.DotNetTest("./test/Build.Tests/Build.Tests.csproj", new DotNetTestSettings
        {
            Configuration = "Release",
            Verbosity = DotNetVerbosity.Minimal,
            PathType = DotNetTestPathType.Project,
        });
    }
}
```

`build/Tasks/PackTask.cs`:

```csharp
using Cake.Common.Diagnostics;
using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Pack;
using Cake.Core;
using Cake.Frosting;

namespace Build.Tasks;

/// <summary>
/// Packs Cake.Download.Module into ./artifacts and verifies the package content for this version.
/// </summary>
[TaskName("Pack")]
[IsDependentOn(typeof(BuildTask))]
public sealed class PackTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        context.DotNetPack(BuildContext.Solution, new DotNetPackSettings
        {
            Configuration = "Release",
            Verbosity = DotNetVerbosity.Minimal,
            NoBuild = true,
            NoRestore = true,
            OutputDirectory = context.ArtifactsDirectory,
        });

        if (!context.FileExists(context.PackageFile))
        {
            throw new CakeException($"Pack did not produce {context.PackageFile.GetFilename()}.");
        }

        var problems = PackageVerifier.Verify(context.PackageFile.FullPath);
        if (problems.Count > 0)
        {
            throw new CakeException($"Package {context.PackageFile.GetFilename()} is invalid: {string.Join("; ", problems)}");
        }

        context.Information("Verified {0}", context.PackageFile.GetFilename());
    }
}
```

`build/Tasks/AllTask.cs`:

```csharp
using Cake.Frosting;

namespace Build.Tasks;

[TaskName("All")]
[IsDependentOn(typeof(BuildTask))]
[IsDependentOn(typeof(TestTask))]
[IsDependentOn(typeof(PackTask))]
public sealed class AllTask : FrostingTask
{
}
```

`build/Tasks/DefaultTask.cs`:

```csharp
using Cake.Frosting;

namespace Build.Tasks;

[TaskName("Default")]
[IsDependentOn(typeof(BuildTask))]
public sealed class DefaultTask : FrostingTask
{
}
```

- [ ] **Step 3: Write the failing package verifier tests**

`test/Build.Tests` tests the `build/` project, which follows Cake.CycloneDX's build code and isn't StyleCop-clean or centrally versioned. It opts out of the `test/Directory.*` files, which import the `src/` ones.

`test/Build.Tests/Directory.Build.props`:

```xml
<!-- Stops the import of test/Directory.Build.props: the build tests use neither StyleCop nor the module's settings. -->
<Project>
</Project>
```

`test/Build.Tests/Directory.Build.targets`:

```xml
<Project>
</Project>
```

`test/Build.Tests/Directory.Packages.props`:

```xml
<!-- The build tests pin their own package versions. -->
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
  </PropertyGroup>
</Project>
```

`test/Build.Tests/Build.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
    <TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="xunit.v3" Version="4.0.1" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\build\Build.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

</Project>
```

`test/Build.Tests/PackageVerifierTests.cs`:

```csharp
using System.IO.Compression;

namespace Build.Tests;

public sealed class PackageVerifierTests : IDisposable
{
    private const string Tags = "cake,cake-module,cake-build,download,tool";

    private static readonly string[] Libraries =
    [
        "lib/net8.0/Cake.Download.Module.dll", "lib/net8.0/Cake.Download.Module.xml",
        "lib/net9.0/Cake.Download.Module.dll", "lib/net9.0/Cake.Download.Module.xml",
        "lib/net10.0/Cake.Download.Module.dll", "lib/net10.0/Cake.Download.Module.xml",
    ];

    private readonly string _directory = Directory.CreateTempSubdirectory("PackageVerifierTests").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Verify_Accepts_A_Complete_Package_With_Empty_Dependency_Groups()
    {
        var package = CreatePackage(
            Libraries.Concat(["icon.png", "README.md"]),
            Nuspec(Tags, """<dependencies><group targetFramework="net8.0" /></dependencies>"""));

        Assert.Empty(PackageVerifier.Verify(package));
    }

    [Fact]
    public void Verify_Reports_Missing_Files()
    {
        var package = CreatePackage(Libraries.Where(path => path != "lib/net9.0/Cake.Download.Module.xml"), Nuspec(Tags));

        Assert.Equal(
            ["missing lib/net9.0/Cake.Download.Module.xml", "missing icon.png", "missing README.md"],
            PackageVerifier.Verify(package));
    }

    [Fact]
    public void Verify_Reports_A_Missing_Cake_Module_Tag()
    {
        var package = CreatePackage(Libraries.Concat(["icon.png", "README.md"]), Nuspec("cake,cake-addin"));

        Assert.Equal(["nuspec tags do not contain 'cake-module'"], PackageVerifier.Verify(package));
    }

    [Fact]
    public void Verify_Reports_Any_Dependency()
    {
        var package = CreatePackage(
            Libraries.Concat(["icon.png", "README.md"]),
            Nuspec(Tags, """<dependencies><group targetFramework="net8.0"><dependency id="System.Text.Json" version="8.0.0" /><dependency id="Cake.Core" version="6.0.0" /></group></dependencies>"""));

        Assert.Equal(
            ["nuspec declares dependencies (Cake.Core, System.Text.Json); Cake does not install the dependencies of a #module package, so it must have none"],
            PackageVerifier.Verify(package));
    }

    [Fact]
    public void Verify_Reports_A_Missing_Nuspec()
    {
        var package = CreatePackage(Libraries.Concat(["icon.png", "README.md"]), nuspec: null);

        Assert.Equal(["missing .nuspec"], PackageVerifier.Verify(package));
    }

    private static string Nuspec(string tags, string dependencies = "") => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
          <metadata>
            <id>Cake.Download.Module</id>
            <version>1.0.0</version>
            <tags>{tags}</tags>
            {dependencies}
          </metadata>
        </package>
        """;

    private string CreatePackage(IEnumerable<string> files, string? nuspec)
    {
        var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.nupkg");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var file in files)
        {
            archive.CreateEntry(file);
        }

        if (nuspec is not null)
        {
            using var writer = new StreamWriter(archive.CreateEntry("Cake.Download.Module.nuspec").Open());
            writer.Write(nuspec);
        }

        return path;
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test --project test/Build.Tests/Build.Tests.csproj`
Expected: build fails with `CS0103: The name 'PackageVerifier' does not exist`.

- [ ] **Step 5: Implement the verifier**

`build/PackageVerifier.cs`:

```csharp
using System.IO.Compression;
using System.Xml.Linq;

namespace Build;

/// <summary>
/// Checks a packed Cake.Download.Module .nupkg: a library and its XML docs per target framework, the icon and README,
/// the <c>cake-module</c> tag, and no dependencies at all, since Cake never installs a <c>#module</c>'s dependencies.
/// </summary>
public static class PackageVerifier
{
    private const string PackageId = "Cake.Download.Module";

    private static readonly string[] TargetFrameworks = ["net8.0", "net9.0", "net10.0"];

    private static readonly char[] TagSeparators = [' ', ','];

    public static IReadOnlyList<string> Verify(string packagePath)
    {
        var problems = new List<string>();

        using var package = ZipFile.OpenRead(packagePath);
        var entries = new HashSet<string>(package.Entries.Select(entry => entry.FullName), StringComparer.OrdinalIgnoreCase);

        foreach (var framework in TargetFrameworks)
        {
            RequireEntry(entries, $"lib/{framework}/{PackageId}.dll", problems);
            RequireEntry(entries, $"lib/{framework}/{PackageId}.xml", problems);
        }

        RequireEntry(entries, "icon.png", problems);
        RequireEntry(entries, "README.md", problems);

        var nuspecEntry = package.Entries.SingleOrDefault(
            entry => !entry.FullName.Contains('/') && entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        if (nuspecEntry is null)
        {
            problems.Add("missing .nuspec");
            return problems;
        }

        using var stream = nuspecEntry.Open();
        var nuspec = XDocument.Load(stream);
        var ns = nuspec.Root!.Name.Namespace;

        var tags = (string?)nuspec.Root.Element(ns + "metadata")?.Element(ns + "tags") ?? string.Empty;
        if (!tags.Split(TagSeparators, StringSplitOptions.RemoveEmptyEntries).Contains("cake-module"))
        {
            problems.Add("nuspec tags do not contain 'cake-module'");
        }

        var dependencies = nuspec.Descendants(ns + "dependency")
            .Select(dependency => (string?)dependency.Attribute("id") ?? "?")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (dependencies.Count > 0)
        {
            problems.Add($"nuspec declares dependencies ({string.Join(", ", dependencies)}); Cake does not install the dependencies of a #module package, so it must have none");
        }

        return problems;
    }

    private static void RequireEntry(HashSet<string> entries, string path, List<string> problems)
    {
        if (!entries.Contains(path))
        {
            problems.Add($"missing {path}");
        }
    }
}
```

- [ ] **Step 6: Give the README real content (it is packed)**

Replace `README.md` with a short placeholder that Task 13 expands:

```markdown
# Cake.Download.Module

A [Cake](https://cakebuild.net) module that installs tools from HTTPS URLs and GitHub release assets with the
`download:` scheme. See the full documentation in this README once released.
```

- [ ] **Step 7: Run the verifier tests and the full build**

Run: `dotnet test --project test/Build.Tests/Build.Tests.csproj`
Expected: PASS.

Run: `./build.ps1 --target All` (or `./build.sh --target All`)
Expected:
- Build, Test and Pack succeed.
- `artifacts/Cake.Download.Module.<version>.nupkg` exists.
- The log ends with `Verified Cake.Download.Module.<version>.nupkg`.

- [ ] **Step 8: Commit**

```bash
git add build.ps1 build.sh build/ test/Build.Tests/ README.md
git commit -F - <<'EOF'
Add Frosting build with pack verification for a dependency-free module

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 12: Runner tests (Cake.Tool, Cake.Sdk, Frosting) against real releases

**Files:**
- Create: `test/runners/Directory.Build.props`, `test/runners/Directory.Build.targets`, `test/runners/Directory.Packages.props`, `test/runners/script/build.cake`, `test/runners/sdk/cake.cs`
- Create: `test/runners/frosting/Frosting.csproj`, `test/runners/frosting/Program.cs`, `test/runners/frosting/ScenarioContext.cs`, `test/runners/frosting/DefaultTask.cs`
- Create: `build/RunnerTests/` with:
  - `RunnerTestContext.cs`, `IRunner.cs`, `RunnerResult.cs`
  - `NuGetConfig.cs`, `RunnerProcess.cs`, `CakeVersionResolver.cs`
  - `ScriptTemplate.cs`, `ToolReport.cs`, `ReportAssertions.cs`, `MarkerSnapshot.cs`
  - `ScriptRunner.cs`, `SdkRunner.cs`, `FrostingRunner.cs`
- Create: `build/Tasks/RunnerTestsTask.cs`
- Test: `test/Build.Tests/ScriptTemplateTests.cs`, `test/Build.Tests/ReportAssertionsTests.cs`, `test/Build.Tests/MarkerSnapshotTests.cs`

**Interfaces:**
- Consumes: `BuildContext.PackageFile`, `PackTask`, `ThisAssembly.PackageVersion` (Task 11); the packed module (Tasks 1–10).
- Produces (namespace `Build.RunnerTests`, public so `Build.Tests` can test them):
  - `ScriptTemplate`:
    - `const string PipelineMarker = "// --- pipeline ---"`
    - `Render(FilePath source, FilePath destination, IReadOnlyDictionary<string,string> lineReplacements)`
    - `string GetPipeline(string text)`
    - `IReadOnlyList<string> GetDirectives(string text)`
    - `void EnsureConsistent(string script, string sdk, string frosting)`
  - `ToolReportEntry(string Name, string Path, string Sha256, string Version)` and `ToolReport.Read(string path)`.
  - `ReportAssertions`: `Check(IReadOnlyList<ToolReportEntry> report, bool isWindows)` and `Compare(string referenceName, IReadOnlyList<ToolReportEntry> reference, IReadOnlyList<ToolReportEntry> other)`. Both return `IReadOnlyList<string>` failures.
  - `MarkerSnapshot`: `Take(string toolsDirectory) → IReadOnlyDictionary<string,string>` and `Compare(first, second) → IReadOnlyList<string>`.
  - Frosting task `RunnerTests` with `--cake-version 6.0.0|6.*` (default `6.*`).

**Stop condition:** if the **sdk** runner fails because `InstallTool("download:…")` doesn't reach the module, **stop and report to the user** before changing the design (spec §14.1). Cake.Generator's README says that modules referenced with `#:package` are registered automatically and that `InstallTool(string)` installs and registers tools.

- [ ] **Step 1: Write the failing helper tests**

`test/Build.Tests/ScriptTemplateTests.cs`:

```csharp
using Build.RunnerTests;
using Cake.Core;

namespace Build.Tests;

public sealed class ScriptTemplateTests
{
    private const string Pipeline = "// --- pipeline ---\nTask(\"Default\");\n";

    [Fact]
    public void GetDirectives_Returns_The_Sorted_Download_Uris()
    {
        var text = "#tool \"download:https://b.example/x?package=b\"\n#tool \"download:https://a.example/x?package=a\"\n";

        Assert.Equal(["download:https://a.example/x?package=a", "download:https://b.example/x?package=b"], ScriptTemplate.GetDirectives(text));
    }

    [Fact]
    public void EnsureConsistent_Accepts_Matching_Templates()
    {
        ScriptTemplate.EnsureConsistent(
            "#tool \"download:https://a.example/x?package=a\"\n" + Pipeline,
            "InstallTool(\"download:https://a.example/x?package=a\");\r\n" + Pipeline.Replace("\n", "\r\n"),
            ".InstallTool(new Uri(\"download:https://a.example/x?package=a\"))");
    }

    [Fact]
    public void EnsureConsistent_Rejects_Different_Pipelines()
    {
        var exception = Assert.Throws<CakeException>(() => ScriptTemplate.EnsureConsistent(
            "#tool \"download:https://a.example/x?package=a\"\n" + Pipeline,
            "InstallTool(\"download:https://a.example/x?package=a\");\n" + Pipeline + "// extra\n",
            ".InstallTool(new Uri(\"download:https://a.example/x?package=a\"))"));

        Assert.StartsWith("The pipelines in test/runners/script/build.cake and test/runners/sdk/cake.cs differ.", exception.Message);
    }

    [Fact]
    public void EnsureConsistent_Rejects_Different_Directives()
    {
        var exception = Assert.Throws<CakeException>(() => ScriptTemplate.EnsureConsistent(
            "#tool \"download:https://a.example/x?package=a\"\n" + Pipeline,
            "InstallTool(\"download:https://a.example/x?package=a\");\n" + Pipeline,
            ".InstallTool(new Uri(\"download:https://a.example/y?package=a\"))"));

        Assert.StartsWith("The download: directives in test/runners/frosting/Program.cs differ from test/runners/script/build.cake.", exception.Message);
    }
}
```

`test/Build.Tests/ReportAssertionsTests.cs`:

```csharp
using Build.RunnerTests;

namespace Build.Tests;

public sealed class ReportAssertionsTests
{
    private static readonly string Sha = new('a', 64);

    private static readonly ToolReportEntry[] Linux =
    [
        new("jq", "jq.1.8.2/jq", Sha, "jq-1.8.2"),
        new("gh", "gh.2.62.0/gh_2.62.0_linux_amd64/bin/gh", Sha, "gh version 2.62.0 (2024-11-14)\nhttps://github.com/cli/cli/releases/tag/v2.62.0"),
        new("rg", "rg.14.1.1/ripgrep-14.1.1-x86_64-unknown-linux-musl/rg", Sha, "ripgrep 14.1.1\n\nfeatures:+pcre2"),
        new("cyclonedx", "cyclonedx.0.30.0/cyclonedx", Sha, "0.30.0+abc"),
    ];

    [Fact]
    public void Check_Accepts_A_Complete_Report()
    {
        Assert.Empty(ReportAssertions.Check(Linux, isWindows: false));
    }

    [Fact]
    public void Check_Expects_Exe_Paths_On_Windows()
    {
        var failures = ReportAssertions.Check(Linux, isWindows: true);

        Assert.Contains("jq: path 'jq.1.8.2/jq' does not match 'jq.1.8.2/jq.exe'", failures);
    }

    [Fact]
    public void Check_Reports_Missing_Tools_Wrong_Versions_And_Bad_Hashes()
    {
        var report = new[]
        {
            Linux[0] with { Version = "jq-1.7.1" },
            Linux[1] with { Sha256 = "nope" },
            Linux[2],
        };

        Assert.Equal(
            [
                "jq: version output 'jq-1.7.1' does not contain 'jq-1.8.2'",
                "gh: sha256 'nope' is not a SHA-256 hash",
                "cyclonedx: missing from the report",
            ],
            ReportAssertions.Check(report, isWindows: false));
    }

    [Fact]
    public void Compare_Reports_Differences_Between_Runners()
    {
        var other = new[] { Linux[0], Linux[1] with { Sha256 = new string('b', 64) }, Linux[2], Linux[3] };

        Assert.Equal(["gh: sha256 differs from the script runner"], ReportAssertions.Compare("script", Linux, other));
    }
}
```

`test/Build.Tests/MarkerSnapshotTests.cs`:

```csharp
using Build.RunnerTests;

namespace Build.Tests;

public sealed class MarkerSnapshotTests : IDisposable
{
    private readonly string _tools = Directory.CreateTempSubdirectory("MarkerSnapshotTests").FullName;

    public void Dispose() => Directory.Delete(_tools, recursive: true);

    [Fact]
    public void Take_Reads_Every_Install_Marker()
    {
        Write("jq.1.8.2", "one");
        Write("gh.2.62.0", "two");
        Directory.CreateDirectory(Path.Combine(_tools, "Modules"));

        var snapshot = MarkerSnapshot.Take(_tools);

        Assert.Equal(["gh.2.62.0", "jq.1.8.2"], snapshot.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("one", snapshot["jq.1.8.2"]);
    }

    [Fact]
    public void Compare_Reports_Changed_Missing_And_Empty_Snapshots()
    {
        var first = new Dictionary<string, string> { ["jq.1.8.2"] = "one", ["gh.2.62.0"] = "two" };
        var second = new Dictionary<string, string> { ["jq.1.8.2"] = "changed" };

        Assert.Equal(
            ["the second run removed gh.2.62.0", "the second run reinstalled jq.1.8.2"],
            MarkerSnapshot.Compare(first, second));
        Assert.Equal(["no .cake-download.json markers were written"], MarkerSnapshot.Compare(new Dictionary<string, string>(), second));
        Assert.Empty(MarkerSnapshot.Compare(first, first));
    }

    private void Write(string folder, string content)
    {
        Directory.CreateDirectory(Path.Combine(_tools, folder));
        File.WriteAllText(Path.Combine(_tools, folder, ".cake-download.json"), content);
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test --project test/Build.Tests/Build.Tests.csproj`
Expected: build fails with `CS0246: The type or namespace name 'ToolReportEntry' could not be found` (and `ScriptTemplate`, `MarkerSnapshot`).

- [ ] **Step 3: Implement the pure helpers**

`build/RunnerTests/ScriptTemplate.cs`:

```csharp
using System.Text.RegularExpressions;
using Cake.Core;
using Cake.Core.IO;

namespace Build.RunnerTests;

/// <summary>
/// Renders runner templates (real, compilable files with a dummy version) and guards them against drifting apart.
/// </summary>
public static partial class ScriptTemplate
{
    public const string PipelineMarker = "// --- pipeline ---";

    /// <param name="lineReplacements">Regex pattern (matched per line) → replacement line.</param>
    public static void Render(FilePath source, FilePath destination, IReadOnlyDictionary<string, string> lineReplacements)
    {
        var text = File.ReadAllText(source.FullPath);
        foreach (var (pattern, replacement) in lineReplacements)
        {
            var regex = new Regex(pattern, RegexOptions.Multiline);
            if (!regex.IsMatch(text))
            {
                throw new CakeException($"'{source.FullPath}' has no line matching '{pattern}'.");
            }

            text = regex.Replace(text, replacement);
        }

        Directory.CreateDirectory(destination.GetDirectory().FullPath);
        File.WriteAllText(destination.FullPath, text);
    }

    public static string GetPipeline(string text)
    {
        var normalized = text.Replace("\r\n", "\n");
        var index = normalized.IndexOf(PipelineMarker, StringComparison.Ordinal);
        return index < 0 ? throw new CakeException($"The template has no '{PipelineMarker}' line.") : normalized[index..];
    }

    public static IReadOnlyList<string> GetDirectives(string text) =>
        Directive().Matches(text).Select(match => match.Groups["uri"].Value).Order(StringComparer.Ordinal).ToList();

    public static void EnsureConsistent(string script, string sdk, string frosting)
    {
        if (!string.Equals(GetPipeline(script), GetPipeline(sdk), StringComparison.Ordinal))
        {
            throw new CakeException(
                "The pipelines in test/runners/script/build.cake and test/runners/sdk/cake.cs differ. " +
                $"Everything after '{PipelineMarker}' must be identical.");
        }

        var expected = GetDirectives(script);
        foreach (var (name, text) in new[] { ("test/runners/sdk/cake.cs", sdk), ("test/runners/frosting/Program.cs", frosting) })
        {
            if (!expected.SequenceEqual(GetDirectives(text)))
            {
                throw new CakeException($"The download: directives in {name} differ from test/runners/script/build.cake. Keep all three runners on the same directives.");
            }
        }
    }

    [GeneratedRegex("\"(?<uri>download:[^\"]+)\"")]
    private static partial Regex Directive();
}
```

`build/RunnerTests/ToolReport.cs`:

```csharp
using System.Text.Json;

namespace Build.RunnerTests;

public sealed record ToolReportEntry(string Name, string Path, string Sha256, string Version);

public static class ToolReport
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<ToolReportEntry> Read(string path) =>
        JsonSerializer.Deserialize<List<ToolReportEntry>>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException($"'{path}' is not a tool report.");
}
```

`build/RunnerTests/ReportAssertions.cs`:

```csharp
using System.Text.RegularExpressions;

namespace Build.RunnerTests;

/// <summary>
/// What every runner must report for the scenario's four tools.
/// </summary>
public static partial class ReportAssertions
{
    public static IReadOnlyList<string> Check(IReadOnlyList<ToolReportEntry> report, bool isWindows)
    {
        var exe = isWindows ? ".exe" : string.Empty;
        var expectations = new (string Name, string Version, Func<string, bool> PathMatches, string PathDescription)[]
        {
            ("jq", "jq-1.8.2", path => path == $"jq.1.8.2/jq{exe}", $"jq.1.8.2/jq{exe}"),
            ("gh", "gh version 2.62.0", path => path.StartsWith("gh.2.62.0/", StringComparison.Ordinal) && path.EndsWith($"/bin/gh{exe}", StringComparison.Ordinal), $"gh.2.62.0/**/bin/gh{exe}"),
            ("rg", "ripgrep 14.1.1", path => path.StartsWith("rg.14.1.1/ripgrep-14.1.1-", StringComparison.Ordinal) && path.EndsWith($"/rg{exe}", StringComparison.Ordinal), $"rg.14.1.1/ripgrep-14.1.1-*/rg{exe}"),
            ("cyclonedx", "0.30.0", path => path == $"cyclonedx.0.30.0/cyclonedx{exe}", $"cyclonedx.0.30.0/cyclonedx{exe}"),
        };

        var failures = new List<string>();
        foreach (var expected in expectations)
        {
            var entry = report.FirstOrDefault(candidate => candidate.Name == expected.Name);
            if (entry is null)
            {
                failures.Add($"{expected.Name}: missing from the report");
                continue;
            }

            if (!expected.PathMatches(entry.Path))
            {
                failures.Add($"{expected.Name}: path '{entry.Path}' does not match '{expected.PathDescription}'");
            }

            if (!Sha256().IsMatch(entry.Sha256))
            {
                failures.Add($"{expected.Name}: sha256 '{entry.Sha256}' is not a SHA-256 hash");
            }

            if (!entry.Version.Contains(expected.Version, StringComparison.Ordinal))
            {
                failures.Add($"{expected.Name}: version output '{entry.Version}' does not contain '{expected.Version}'");
            }
        }

        return failures;
    }

    public static IReadOnlyList<string> Compare(string referenceName, IReadOnlyList<ToolReportEntry> reference, IReadOnlyList<ToolReportEntry> other)
    {
        var failures = new List<string>();
        foreach (var expected in reference)
        {
            var actual = other.FirstOrDefault(entry => entry.Name == expected.Name);
            if (actual is null)
            {
                continue;
            }

            if (actual.Path != expected.Path)
            {
                failures.Add($"{expected.Name}: path differs from the {referenceName} runner");
            }

            if (actual.Sha256 != expected.Sha256)
            {
                failures.Add($"{expected.Name}: sha256 differs from the {referenceName} runner");
            }

            if (actual.Version != expected.Version)
            {
                failures.Add($"{expected.Name}: version output differs from the {referenceName} runner");
            }
        }

        return failures;
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256();
}
```

`build/RunnerTests/MarkerSnapshot.cs`:

```csharp
namespace Build.RunnerTests;

/// <summary>
/// Captures every <c>.cake-download.json</c> in a tools folder, to prove a second run reinstalled nothing.
/// </summary>
public static class MarkerSnapshot
{
    private const string MarkerFileName = ".cake-download.json";

    public static IReadOnlyDictionary<string, string> Take(string toolsDirectory)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(toolsDirectory))
        {
            return result;
        }

        foreach (var folder in Directory.EnumerateDirectories(toolsDirectory))
        {
            var marker = Path.Combine(folder, MarkerFileName);
            if (File.Exists(marker))
            {
                result[Path.GetFileName(folder)] = File.ReadAllText(marker);
            }
        }

        return result;
    }

    public static IReadOnlyList<string> Compare(IReadOnlyDictionary<string, string> first, IReadOnlyDictionary<string, string> second)
    {
        if (first.Count == 0)
        {
            return ["no .cake-download.json markers were written"];
        }

        var failures = new List<string>();
        foreach (var (folder, content) in first.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (!second.TryGetValue(folder, out var after))
            {
                failures.Add($"the second run removed {folder}");
            }
            else if (after != content)
            {
                failures.Add($"the second run reinstalled {folder}");
            }
        }

        return failures;
    }
}
```

- [ ] **Step 4: Run the helper tests to verify they pass**

Run: `dotnet test --project test/Build.Tests/Build.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Write the runner templates**

The runner projects must behave like a user's build. They are isolated from `test/Directory.*`: no StyleCop, no central package management, and the Cake.Sdk file-based app pins versions in `#:package`.

`test/runners/Directory.Build.props`:

```xml
<!-- Runner templates behave like a user's build: no repository build settings. -->
<Project>
</Project>
```

`test/runners/Directory.Build.targets`:

```xml
<Project>
</Project>
```

`test/runners/Directory.Packages.props`:

```xml
<!-- Runner templates pin their own versions (Frosting.csproj properties, cake.cs #:package). -->
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
  </PropertyGroup>
</Project>
```

The rendered copies run under `artifacts/runner-tests/`, outside `test/`, so they are isolated anyway. These files keep the templates buildable in place, for example when opening `Frosting.csproj` in an IDE.

All three runners use exactly these four directives. The hashes were taken from the GitHub release API `digest` fields and the projects' published checksum files on 2026-10-03.

`test/runners/script/build.cake`:

```csharp
// Cake .NET Tool runner test. RunnerTestsTask renders a copy with the real module version; not meant to run from here.
#module nuget:?package=Cake.Download.Module&version=0.0.0&prerelease
#tool "download:https://github.com/jqlang/jq/releases/download/jq-{version}/jq-{os}-{arch}{exe}?package=jq&version=1.8.2&os.darwin=macos&checksums=sha256sum.txt&checksums_sha256=dc86824a41c165ece971ff691aff6e08bbfe6e1d1f531688b47ee78c283a85cd"
#tool "download:https://github.com/cli/cli/releases/download/v{version}/gh_{version}_{os}_{arch}.{archive}?package=gh&version=2.62.0&os.darwin=macOS&archive.darwin=zip&checksums=gh_{version}_checksums.txt&checksums_sha256=89dc6f5225aa0c70d6f90950f5246225afff2f423f3d242f7d8813fe9993af4d"
#tool "download:https://github.com/BurntSushi/ripgrep/releases/download/{version}/ripgrep-{version}-{triple}.{archive}?package=rg&version=14.1.1&dialect=rust&triple.linux-arm64=aarch64-unknown-linux-gnu&sha256.win-x64=d0f534024c42afd6cb4d38907c25cd2b249b79bbe6cc1dbee8e3e37c2b6e25a1&sha256.linux-x64=4cf9f2741e6c465ffdb7c26f38056a59e2a2544b51f7cc128ef28337eeae4d8e&sha256.linux-arm64=c827481c4ff4ea10c9dc7a4022c8de5db34a5737cb74484d62eb94a95841ab2f&sha256.osx-x64=fc87e78f7cb3fea12d69072e7ef3b21509754717b746368fd40d88963630e2b3&sha256.osx-arm64=24ad76777745fbff131c8fbc466742b011f925bfa4fffa2ded6def23b5b937be"
#tool "download:https://github.com/CycloneDX/cyclonedx-cli/releases/download/v{version}/cyclonedx-{rid}{exe}?package=cyclonedx&version=0.30.0&dialect=dotnet&sha256.win-x64=1f563ba9644d2f2966fc8029fd701ca4af4f388d44c017c1d60559a1ecc9114f&sha256.win-arm64=866809c6e2617c39d0b11713872ae35b88c98941c22dc66d9a4b633fa56db82a&sha256.linux-x64=f89876326620f5fc78a9b27cc1af57d6ed13d019aab87490e1246a44a910babb&sha256.linux-arm64=190da406177311aa1081edd0c717df10271eba7e4356a56215494a70e1a4b459&sha256.osx-x64=1603264fd2968b8d617e48aa7e9cf17bee1d25a8ffe717aec37caf1605a21961&sha256.osx-arm64=dabbaf07e543e7996f708147475e2daa69ddf8a8683c5b06febc7d3f074e5e24"

// --- pipeline ---
var output = MakeAbsolute(Directory(Argument<string>("output")));
var toolNames = new[] { "jq", "gh", "rg", "cyclonedx" };

Task("Default").Does(() =>
{
    EnsureDirectoryExists(output);
    var toolsDirectory = MakeAbsolute(Directory("./tools"));
    var report = new List<Dictionary<string, string>>();
    foreach (var name in toolNames)
    {
        var path = Context.Tools.Resolve(IsRunningOnWindows() ? name + ".exe" : name)
            ?? throw new Exception($"Cake did not resolve the tool '{name}'.");
        var exitCode = StartProcess(path, new ProcessSettings { Arguments = "--version", RedirectStandardOutput = true }, out var lines);
        if (exitCode != 0)
        {
            throw new Exception($"'{name} --version' exited with code {exitCode}.");
        }

        report.Add(new Dictionary<string, string>
        {
            ["name"] = name,
            ["path"] = toolsDirectory.GetRelativePath(path).FullPath,
            ["sha256"] = CalculateFileHash(path).ToHex(),
            ["version"] = string.Join("\n", lines).Trim(),
        });
    }

    System.IO.File.WriteAllText(
        output.CombineWithFilePath("report.json").FullPath,
        System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
});

RunTarget("Default");
```

`test/runners/sdk/cake.cs`. The text from `// --- pipeline ---` to the end must be **identical** to `build.cake`:

```csharp
// Cake.Sdk runner test. RunnerTestsTask renders a copy with the real Cake.Sdk and module versions.
#:sdk Cake.Sdk@6.0.0
#:package Cake.Download.Module@0.0.0

InstallTool("download:https://github.com/jqlang/jq/releases/download/jq-{version}/jq-{os}-{arch}{exe}?package=jq&version=1.8.2&os.darwin=macos&checksums=sha256sum.txt&checksums_sha256=dc86824a41c165ece971ff691aff6e08bbfe6e1d1f531688b47ee78c283a85cd");
InstallTool("download:https://github.com/cli/cli/releases/download/v{version}/gh_{version}_{os}_{arch}.{archive}?package=gh&version=2.62.0&os.darwin=macOS&archive.darwin=zip&checksums=gh_{version}_checksums.txt&checksums_sha256=89dc6f5225aa0c70d6f90950f5246225afff2f423f3d242f7d8813fe9993af4d");
InstallTool("download:https://github.com/BurntSushi/ripgrep/releases/download/{version}/ripgrep-{version}-{triple}.{archive}?package=rg&version=14.1.1&dialect=rust&triple.linux-arm64=aarch64-unknown-linux-gnu&sha256.win-x64=d0f534024c42afd6cb4d38907c25cd2b249b79bbe6cc1dbee8e3e37c2b6e25a1&sha256.linux-x64=4cf9f2741e6c465ffdb7c26f38056a59e2a2544b51f7cc128ef28337eeae4d8e&sha256.linux-arm64=c827481c4ff4ea10c9dc7a4022c8de5db34a5737cb74484d62eb94a95841ab2f&sha256.osx-x64=fc87e78f7cb3fea12d69072e7ef3b21509754717b746368fd40d88963630e2b3&sha256.osx-arm64=24ad76777745fbff131c8fbc466742b011f925bfa4fffa2ded6def23b5b937be");
InstallTool("download:https://github.com/CycloneDX/cyclonedx-cli/releases/download/v{version}/cyclonedx-{rid}{exe}?package=cyclonedx&version=0.30.0&dialect=dotnet&sha256.win-x64=1f563ba9644d2f2966fc8029fd701ca4af4f388d44c017c1d60559a1ecc9114f&sha256.win-arm64=866809c6e2617c39d0b11713872ae35b88c98941c22dc66d9a4b633fa56db82a&sha256.linux-x64=f89876326620f5fc78a9b27cc1af57d6ed13d019aab87490e1246a44a910babb&sha256.linux-arm64=190da406177311aa1081edd0c717df10271eba7e4356a56215494a70e1a4b459&sha256.osx-x64=1603264fd2968b8d617e48aa7e9cf17bee1d25a8ffe717aec37caf1605a21961&sha256.osx-arm64=dabbaf07e543e7996f708147475e2daa69ddf8a8683c5b06febc7d3f074e5e24");

// --- pipeline ---
var output = MakeAbsolute(Directory(Argument<string>("output")));
var toolNames = new[] { "jq", "gh", "rg", "cyclonedx" };

Task("Default").Does(() =>
{
    EnsureDirectoryExists(output);
    var toolsDirectory = MakeAbsolute(Directory("./tools"));
    var report = new List<Dictionary<string, string>>();
    foreach (var name in toolNames)
    {
        var path = Context.Tools.Resolve(IsRunningOnWindows() ? name + ".exe" : name)
            ?? throw new Exception($"Cake did not resolve the tool '{name}'.");
        var exitCode = StartProcess(path, new ProcessSettings { Arguments = "--version", RedirectStandardOutput = true }, out var lines);
        if (exitCode != 0)
        {
            throw new Exception($"'{name} --version' exited with code {exitCode}.");
        }

        report.Add(new Dictionary<string, string>
        {
            ["name"] = name,
            ["path"] = toolsDirectory.GetRelativePath(path).FullPath,
            ["sha256"] = CalculateFileHash(path).ToHex(),
            ["version"] = string.Join("\n", lines).Trim(),
        });
    }

    System.IO.File.WriteAllText(
        output.CombineWithFilePath("report.json").FullPath,
        System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
});

RunTarget("Default");
```

`test/runners/frosting/Frosting.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <!-- RunnerTestsTask sets both; the defaults let the project open and restore on its own. -->
    <CakeVersion Condition="'$(CakeVersion)' == ''">6.*</CakeVersion>
    <ModuleVersion Condition="'$(ModuleVersion)' == ''">*-*</ModuleVersion>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Cake.Frosting" Version="$(CakeVersion)" />
    <PackageReference Include="Cake.Download.Module" Version="$(ModuleVersion)" />
  </ItemGroup>

</Project>
```

`test/runners/frosting/Program.cs`:

```csharp
using Cake.Download.Module;
using Cake.Frosting;

// Cake Frosting runner test. RunnerTestsTask builds this project against the packed module and runs it.
return new CakeHost()
    .UseContext<Frosting.ScenarioContext>()
    .UseModule<DownloadModule>()
    .InstallTool(new Uri("download:https://github.com/jqlang/jq/releases/download/jq-{version}/jq-{os}-{arch}{exe}?package=jq&version=1.8.2&os.darwin=macos&checksums=sha256sum.txt&checksums_sha256=dc86824a41c165ece971ff691aff6e08bbfe6e1d1f531688b47ee78c283a85cd"))
    .InstallTool(new Uri("download:https://github.com/cli/cli/releases/download/v{version}/gh_{version}_{os}_{arch}.{archive}?package=gh&version=2.62.0&os.darwin=macOS&archive.darwin=zip&checksums=gh_{version}_checksums.txt&checksums_sha256=89dc6f5225aa0c70d6f90950f5246225afff2f423f3d242f7d8813fe9993af4d"))
    .InstallTool(new Uri("download:https://github.com/BurntSushi/ripgrep/releases/download/{version}/ripgrep-{version}-{triple}.{archive}?package=rg&version=14.1.1&dialect=rust&triple.linux-arm64=aarch64-unknown-linux-gnu&sha256.win-x64=d0f534024c42afd6cb4d38907c25cd2b249b79bbe6cc1dbee8e3e37c2b6e25a1&sha256.linux-x64=4cf9f2741e6c465ffdb7c26f38056a59e2a2544b51f7cc128ef28337eeae4d8e&sha256.linux-arm64=c827481c4ff4ea10c9dc7a4022c8de5db34a5737cb74484d62eb94a95841ab2f&sha256.osx-x64=fc87e78f7cb3fea12d69072e7ef3b21509754717b746368fd40d88963630e2b3&sha256.osx-arm64=24ad76777745fbff131c8fbc466742b011f925bfa4fffa2ded6def23b5b937be"))
    .InstallTool(new Uri("download:https://github.com/CycloneDX/cyclonedx-cli/releases/download/v{version}/cyclonedx-{rid}{exe}?package=cyclonedx&version=0.30.0&dialect=dotnet&sha256.win-x64=1f563ba9644d2f2966fc8029fd701ca4af4f388d44c017c1d60559a1ecc9114f&sha256.win-arm64=866809c6e2617c39d0b11713872ae35b88c98941c22dc66d9a4b633fa56db82a&sha256.linux-x64=f89876326620f5fc78a9b27cc1af57d6ed13d019aab87490e1246a44a910babb&sha256.linux-arm64=190da406177311aa1081edd0c717df10271eba7e4356a56215494a70e1a4b459&sha256.osx-x64=1603264fd2968b8d617e48aa7e9cf17bee1d25a8ffe717aec37caf1605a21961&sha256.osx-arm64=dabbaf07e543e7996f708147475e2daa69ddf8a8683c5b06febc7d3f074e5e24"))
    .Run(args);
```

`test/runners/frosting/ScenarioContext.cs`:

```csharp
using Cake.Common;
using Cake.Common.IO;
using Cake.Core;
using Cake.Core.IO;
using Cake.Frosting;

namespace Frosting;

public sealed class ScenarioContext : FrostingContext
{
    public ScenarioContext(ICakeContext context)
        : base(context)
    {
        Output = context.MakeAbsolute(new DirectoryPath(context.Argument<string>("output")));
    }

    public DirectoryPath Output { get; }
}
```

`test/runners/frosting/DefaultTask.cs` (the same steps as the script pipeline, written for Frosting):

```csharp
using System.Text.Json;
using Cake.Common;
using Cake.Common.IO;
using Cake.Common.Security;
using Cake.Core;
using Cake.Core.IO;
using Cake.Frosting;

namespace Frosting;

[TaskName("Default")]
public sealed class DefaultTask : FrostingTask<ScenarioContext>
{
    private static readonly string[] ToolNames = ["jq", "gh", "rg", "cyclonedx"];

    public override void Run(ScenarioContext context)
    {
        context.EnsureDirectoryExists(context.Output);
        var toolsDirectory = context.MakeAbsolute(new DirectoryPath("./tools"));
        var report = new List<Dictionary<string, string>>();
        foreach (var name in ToolNames)
        {
            var path = context.Tools.Resolve(context.IsRunningOnWindows() ? name + ".exe" : name)
                ?? throw new CakeException($"Cake did not resolve the tool '{name}'.");
            var exitCode = context.StartProcess(path, new ProcessSettings { Arguments = "--version", RedirectStandardOutput = true }, out var lines);
            if (exitCode != 0)
            {
                throw new CakeException($"'{name} --version' exited with code {exitCode}.");
            }

            report.Add(new Dictionary<string, string>
            {
                ["name"] = name,
                ["path"] = toolsDirectory.GetRelativePath(path).FullPath,
                ["sha256"] = context.CalculateFileHash(path).ToHex(),
                ["version"] = string.Join("\n", lines).Trim(),
            });
        }

        File.WriteAllText(
            context.Output.CombineWithFilePath("report.json").FullPath,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }
}
```

- [ ] **Step 6: Implement the runner harness**

`build/RunnerTests/RunnerTestContext.cs`:

```csharp
using Cake.Core.IO;

namespace Build.RunnerTests;

/// <summary>
/// Everything a runner needs to know about the current runner-test run. Each runner works in
/// <c>&lt;run&gt;/&lt;runner&gt;/src</c> (working directory, so its tools land in <c>src/tools</c>) and writes to
/// <c>&lt;run&gt;/&lt;runner&gt;/out</c>.
/// </summary>
public sealed record RunnerTestContext(string CakeVersion, string ModuleVersion, DirectoryPath RepositoryRoot, DirectoryPath RunDirectory)
{
    public DirectoryPath RunnersDirectory => RepositoryRoot.Combine("test/runners");

    public DirectoryPath ArtifactsDirectory => RepositoryRoot.Combine("artifacts");

    public DirectoryPath NuGetPackagesDirectory => RunDirectory.Combine("nuget-packages");

    public DirectoryPath WorkDirectory(string runner) => RunDirectory.Combine(runner);

    public DirectoryPath SourceDirectory(string runner) => WorkDirectory(runner).Combine("src");

    public DirectoryPath OutputDirectory(string runner) => WorkDirectory(runner).Combine("out");
}
```

`build/RunnerTests/IRunner.cs`:

```csharp
using Cake.Core;

namespace Build.RunnerTests;

/// <summary>
/// A Cake runner under test. <see cref="Prepare"/> runs once; <see cref="Run"/> runs twice to prove the cache hit.
/// </summary>
public interface IRunner
{
    string Name { get; }

    /// <returns>The exit code; 0 means success.</returns>
    int Prepare(ICakeContext context, RunnerTestContext test);

    /// <returns>The exit code; 0 means success.</returns>
    int Run(ICakeContext context, RunnerTestContext test);
}
```

`build/RunnerTests/RunnerResult.cs`:

```csharp
namespace Build.RunnerTests;

public sealed class RunnerResult(string name)
{
    public string Name { get; } = name;

    public List<string> Failures { get; } = [];

    public IReadOnlyList<ToolReportEntry>? Report { get; set; }

    public bool Passed => Failures.Count == 0;
}
```

`build/RunnerTests/NuGetConfig.cs`:

```csharp
using System.Xml.Linq;
using Cake.Core.IO;

namespace Build.RunnerTests;

/// <summary>
/// Writes a nuget.config that takes Cake.Download.Module from the local artifacts feed and everything else from nuget.org.
/// </summary>
public static class NuGetConfig
{
    public static FilePath Write(DirectoryPath directory, DirectoryPath localFeed)
    {
        Directory.CreateDirectory(directory.FullPath);
        var path = directory.CombineWithFilePath("nuget.config");

        new XDocument(
            new XElement(
                "configuration",
                new XElement(
                    "packageSources",
                    new XElement("clear"),
                    new XElement("add", new XAttribute("key", "local"), new XAttribute("value", localFeed.FullPath)),
                    new XElement("add", new XAttribute("key", "nuget.org"), new XAttribute("value", "https://api.nuget.org/v3/index.json"))),
                new XElement(
                    "packageSourceMapping",
                    new XElement(
                        "packageSource",
                        new XAttribute("key", "local"),
                        new XElement("package", new XAttribute("pattern", "Cake.Download.Module"))),
                    new XElement(
                        "packageSource",
                        new XAttribute("key", "nuget.org"),
                        new XElement("package", new XAttribute("pattern", "*"))))))
            .Save(path.FullPath);

        return path;
    }
}
```

`build/RunnerTests/RunnerProcess.cs`:

```csharp
using Cake.Common;
using Cake.Core;
using Cake.Core.IO;

namespace Build.RunnerTests;

/// <summary>
/// Starts runner processes with an isolated NuGet package folder, so a locally rebuilt module that keeps its version is
/// never served from a stale cache.
/// </summary>
public static class RunnerProcess
{
    public static int Run(ICakeContext context, RunnerTestContext test, FilePath executable, ProcessArgumentBuilder arguments, DirectoryPath workingDirectory)
    {
        Directory.CreateDirectory(workingDirectory.FullPath);
        return context.StartProcess(executable, new ProcessSettings
        {
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["NUGET_PACKAGES"] = test.NuGetPackagesDirectory.FullPath,
            },
        });
    }
}
```

`build/RunnerTests/CakeVersionResolver.cs`:

```csharp
using System.Text.Json;
using Cake.Core;

namespace Build.RunnerTests;

/// <summary>
/// Resolves a requested Cake version such as <c>6.0.0</c> or <c>6.*</c> to one published stable version.
/// </summary>
public static class CakeVersionResolver
{
    private const string IndexUrl = "https://api.nuget.org/v3-flatcontainer/cake.tool/index.json";

    public static string Resolve(string requested)
    {
        using var client = new HttpClient();
        var json = client.GetStringAsync(IndexUrl).GetAwaiter().GetResult();
        var published = JsonDocument.Parse(json).RootElement.GetProperty("versions")
            .EnumerateArray()
            .Select(element => element.GetString()!)
            .Where(version => !version.Contains('-'))
            .ToList();

        if (!requested.EndsWith('*'))
        {
            return published.Contains(requested, StringComparer.Ordinal)
                ? requested
                : throw new CakeException($"Cake version '{requested}' does not exist on NuGet.");
        }

        var prefix = requested[..^1];
        var match = published
            .Where(version => version.StartsWith(prefix, StringComparison.Ordinal))
            .Select(Version.Parse)
            .DefaultIfEmpty()
            .Max();

        return match?.ToString() ?? throw new CakeException($"No stable Cake version matches '{requested}'.");
    }
}
```

`build/RunnerTests/ScriptRunner.cs`:

```csharp
using Cake.Common;
using Cake.Core;
using Cake.Core.IO;

namespace Build.RunnerTests;

/// <summary>
/// Runs <c>test/runners/script/build.cake</c> with a private Cake .NET Tool install of the requested version.
/// </summary>
public sealed class ScriptRunner : IRunner
{
    public string Name => "script";

    public int Prepare(ICakeContext context, RunnerTestContext test)
    {
        var source = test.SourceDirectory(Name);
        NuGetConfig.Write(source, test.ArtifactsDirectory);
        ScriptTemplate.Render(
            test.RunnersDirectory.CombineWithFilePath("script/build.cake"),
            source.CombineWithFilePath("build.cake"),
            new Dictionary<string, string>
            {
                [@"^#module nuget:\?package=Cake\.Download\.Module&version=[^\r\n]*"] =
                    $"#module nuget:?package=Cake.Download.Module&version={test.ModuleVersion}&prerelease",
            });

        return RunnerProcess.Run(
            context,
            test,
            "dotnet",
            new ProcessArgumentBuilder()
                .Append("tool")
                .Append("install")
                .Append("Cake.Tool")
                .AppendSwitch("--version", test.CakeVersion)
                .AppendSwitchQuoted("--tool-path", ToolDirectory(test).FullPath),
            test.WorkDirectory(Name));
    }

    public int Run(ICakeContext context, RunnerTestContext test)
    {
        var executable = ToolDirectory(test).CombineWithFilePath(context.IsRunningOnWindows() ? "dotnet-cake.exe" : "dotnet-cake");
        return RunnerProcess.Run(
            context,
            test,
            executable,
            new ProcessArgumentBuilder()
                .Append("build.cake")
                .AppendSwitchQuoted("--output", "=", test.OutputDirectory(Name).FullPath),
            test.SourceDirectory(Name));
    }

    private DirectoryPath ToolDirectory(RunnerTestContext test) => test.WorkDirectory(Name).Combine("cake-tool");
}
```

`build/RunnerTests/SdkRunner.cs`:

```csharp
using Cake.Core;
using Cake.Core.IO;

namespace Build.RunnerTests;

/// <summary>
/// Runs <c>test/runners/sdk/cake.cs</c> as a Cake.Sdk file-based app at the requested Cake version.
/// </summary>
public sealed class SdkRunner : IRunner
{
    public string Name => "sdk";

    public int Prepare(ICakeContext context, RunnerTestContext test)
    {
        var source = test.SourceDirectory(Name);
        NuGetConfig.Write(source, test.ArtifactsDirectory);
        ScriptTemplate.Render(
            test.RunnersDirectory.CombineWithFilePath("sdk/cake.cs"),
            source.CombineWithFilePath("cake.cs"),
            new Dictionary<string, string>
            {
                [@"^#:sdk Cake\.Sdk@[^\r\n]*"] = $"#:sdk Cake.Sdk@{test.CakeVersion}",
                [@"^#:package Cake\.Download\.Module@[^\r\n]*"] = $"#:package Cake.Download.Module@{test.ModuleVersion}",
            });
        return 0;
    }

    public int Run(ICakeContext context, RunnerTestContext test) => RunnerProcess.Run(
        context,
        test,
        "dotnet",
        new ProcessArgumentBuilder()
            .Append("run")
            .Append("--no-cache")
            .AppendSwitchQuoted("--file", test.SourceDirectory(Name).CombineWithFilePath("cake.cs").FullPath)
            .Append("--")
            .AppendSwitchQuoted("--output", "=", test.OutputDirectory(Name).FullPath),
        test.SourceDirectory(Name));
}
```

`build/RunnerTests/FrostingRunner.cs`:

```csharp
using Cake.Core;
using Cake.Core.IO;

namespace Build.RunnerTests;

/// <summary>
/// Builds <c>test/runners/frosting</c> against the requested Cake.Frosting and the packed module, then runs it.
/// </summary>
public sealed class FrostingRunner : IRunner
{
    public string Name => "frosting";

    public int Prepare(ICakeContext context, RunnerTestContext test)
    {
        var nugetConfig = NuGetConfig.Write(test.WorkDirectory(Name), test.ArtifactsDirectory);
        return RunnerProcess.Run(
            context,
            test,
            "dotnet",
            new ProcessArgumentBuilder()
                .Append("build")
                .AppendQuoted(test.RunnersDirectory.CombineWithFilePath("frosting/Frosting.csproj").FullPath)
                .Append("--force")
                .AppendSwitch("--configuration", "Release")
                .AppendSwitchQuoted("--output", BinDirectory(test).FullPath)
                .Append($"--property:CakeVersion={test.CakeVersion}")
                .Append($"--property:ModuleVersion={test.ModuleVersion}")
                .AppendQuoted($"--property:RestoreConfigFile={nugetConfig.FullPath}"),
            test.WorkDirectory(Name));
    }

    public int Run(ICakeContext context, RunnerTestContext test) => RunnerProcess.Run(
        context,
        test,
        "dotnet",
        new ProcessArgumentBuilder()
            .AppendQuoted(BinDirectory(test).CombineWithFilePath("Frosting.dll").FullPath)
            .AppendSwitchQuoted("--output", "=", test.OutputDirectory(Name).FullPath),
        test.SourceDirectory(Name));

    private DirectoryPath BinDirectory(RunnerTestContext test) => test.WorkDirectory(Name).Combine("bin");
}
```

`build/Tasks/RunnerTestsTask.cs`:

```csharp
using Build.RunnerTests;
using Cake.Common;
using Cake.Common.Diagnostics;
using Cake.Common.IO;
using Cake.Core;
using Cake.Frosting;

namespace Build.Tasks;

/// <summary>
/// Runs the packed module in every Cake runner, twice each, against real GitHub releases. Usage:
/// <c>./build.ps1 --target RunnerTests [--cake-version 6.0.0|6.*]</c>.
/// </summary>
[TaskName("RunnerTests")]
[IsDependentOn(typeof(PackTask))]
public sealed class RunnerTestsTask : FrostingTask<BuildContext>
{
    private static readonly IRunner[] Runners = [new ScriptRunner(), new SdkRunner(), new FrostingRunner()];

    public override void Run(BuildContext context)
    {
        var root = context.Environment.WorkingDirectory;
        var runners = root.Combine("test/runners");
        ScriptTemplate.EnsureConsistent(
            File.ReadAllText(runners.CombineWithFilePath("script/build.cake").FullPath),
            File.ReadAllText(runners.CombineWithFilePath("sdk/cake.cs").FullPath),
            File.ReadAllText(runners.CombineWithFilePath("frosting/Program.cs").FullPath));

        if (!context.FileExists(context.PackageFile))
        {
            throw new CakeException($"Package '{context.PackageFile.FullPath}' was not found. Run the Pack target first.");
        }

        var cakeVersion = CakeVersionResolver.Resolve(context.Argument("cake-version", "6.*"));
        var runDirectory = root.Combine($"artifacts/runner-test/{cakeVersion}");
        context.EnsureDirectoryExists(runDirectory);
        context.CleanDirectory(runDirectory);

        var test = new RunnerTestContext(cakeVersion, ThisAssembly.PackageVersion, root, runDirectory);
        context.Information("Runner tests: Cake {0}, Cake.Download.Module {1}", cakeVersion, test.ModuleVersion);

        var results = Runners.Select(runner => RunOne(context, test, runner)).ToList();
        var reference = results.FirstOrDefault(result => result.Report is not null);
        if (reference is not null)
        {
            foreach (var result in results.Where(result => result.Report is not null && result != reference))
            {
                result.Failures.AddRange(ReportAssertions.Compare(reference.Name, reference.Report!, result.Report!));
            }
        }

        context.Information("=== Runner test summary ===");
        foreach (var result in results)
        {
            if (result.Passed)
            {
                context.Information("  {0} passed", result.Name.PadRight(10));
                continue;
            }

            context.Error("  {0} FAILED", result.Name.PadRight(10));
            foreach (var failure in result.Failures)
            {
                context.Error("      {0}", failure);
            }
        }

        if (results.Any(result => !result.Passed))
        {
            throw new CakeException("Runner tests failed. See the summary above.");
        }
    }

    private static RunnerResult RunOne(ICakeContext context, RunnerTestContext test, IRunner runner)
    {
        var result = new RunnerResult(runner.Name);
        var tools = test.SourceDirectory(runner.Name).Combine("tools").FullPath;
        context.EnsureDirectoryExists(test.SourceDirectory(runner.Name));
        context.EnsureDirectoryExists(test.OutputDirectory(runner.Name));
        context.Information("=== Runner: {0} ===", runner.Name);

        try
        {
            var prepared = runner.Prepare(context, test);
            if (prepared != 0)
            {
                result.Failures.Add($"preparing exited with code {prepared}");
                return result;
            }

            var first = runner.Run(context, test);
            if (first != 0)
            {
                result.Failures.Add($"the first run exited with code {first}");
                return result;
            }

            var before = MarkerSnapshot.Take(tools);
            var second = runner.Run(context, test);
            if (second != 0)
            {
                result.Failures.Add($"the second run exited with code {second}");
                return result;
            }

            result.Failures.AddRange(MarkerSnapshot.Compare(before, MarkerSnapshot.Take(tools)));
            result.Report = ToolReport.Read(test.OutputDirectory(runner.Name).CombineWithFilePath("report.json").FullPath);
            result.Failures.AddRange(ReportAssertions.Check(result.Report, context.IsRunningOnWindows()));
        }
        catch (Exception exception)
        {
            result.Failures.Add($"runner threw {exception.GetType().Name}: {exception.Message}");
        }

        return result;
    }
}
```

- [ ] **Step 7: Run the runner tests locally**

Run: `./build.ps1 --target RunnerTests` and then `./build.ps1 --target RunnerTests --cake-version 6.0.0`
Expected: the summary ends with `script passed`, `sdk passed`, `frosting passed` for both versions.

If a runner fails:
1. Read its first-run output in the log. A `CakeException` from the module (for example a 404 with expanded placeholders, or a hash mismatch) points at a directive or module bug. Fix it.
2. If the **sdk** runner cannot install tools through `InstallTool("download:…")`, apply the **stop condition** above.
3. If **cyclonedx** fails to start on Linux because of a missing ICU library, set `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` in `RunnerProcess`'s environment. It is a self-contained .NET app.

- [ ] **Step 8: Commit**

```bash
git add build/ test/runners/ test/Build.Tests/
git commit -F - <<'EOF'
Run the packed module on Cake.Tool, Cake.Sdk and Frosting against real releases

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 13: CI workflows, release tooling and documentation

**Files:**
- Create: `.github/actions/setup-build/action.yml`, `.github/workflows/main.yml`, `.github/workflows/pr.yml`, `.github/dependabot.yml`
- Port from `../Cake.CycloneDX`:
  - `.github/workflows/release.yml`, `release.ps1`, `docs/release-policy.md`
  - `build/ChangelogReleaseNotes.cs`, `build/GitHubRelease.cs`, `build/ReadmeInstallVersion.cs`, `build/Release.psm1`
  - `build/Tasks/DraftReleaseTask.cs`, `build/Tasks/PublishTask.cs`, `build/Tasks/ReleaseNotesTask.cs`, `build/Tasks/ReleaseTask.cs`
  - `test/Release.Tests.ps1`
  - `test/Build.Tests/ChangelogReleaseNotesTests.cs`, `test/Build.Tests/GitHubReleaseTests.cs`, `test/Build.Tests/ReadmeInstallVersionTests.cs`
- Modify: `build/BuildContext.cs` (add the release members)
- Rewrite: `README.md`
- Create: `CHANGELOG.md`, `AGENTS.md`

**Interfaces:**
- Consumes: the Frosting tasks `All` and `RunnerTests` (Tasks 11–12); `PackageVerifier` (Task 11).
- Produces: CI on push and pull request (build on 3 OSes; runner tests on 3 OSes × Cake {6.0.0, 6.\*}); a tag-driven release; user documentation.

- [ ] **Step 1: Add the CI setup action and workflows**

`.github/actions/setup-build/action.yml`:

```yaml
# yaml-language-server: $schema=https://json.schemastore.org/github-action.json
name: Set up build
description: Installs the .NET SDK from global.json.

runs:
  using: composite
  steps:
    - name: Install .NET SDK
      uses: actions/setup-dotnet@v6
      with:
        global-json-file: global.json
```

`.github/workflows/main.yml`:

```yaml
# yaml-language-server: $schema=https://json.schemastore.org/github-workflow.json
name: CI
on:
  push:
    branches: [main]

env:
  DOTNET_SKIP_FIRST_TIME_EXPERIENCE: true
  DOTNET_CLI_TELEMETRY_OPTOUT: true

jobs:
  build:
    runs-on: ${{ matrix.os }}
    strategy:
      fail-fast: false
      matrix:
        os: [windows-latest, ubuntu-latest, macos-latest]
    steps:
      - name: Checkout
        uses: actions/checkout@v7
        with:
          fetch-depth: 0
      - name: Set up .NET
        uses: ./.github/actions/setup-build
      - name: Build, test and pack
        run: dotnet run --project build/Build.csproj -- --target All --verbosity Diagnostic

  runner-tests:
    runs-on: ${{ matrix.os }}
    strategy:
      fail-fast: false
      matrix:
        os: [windows-latest, ubuntu-latest, macos-latest]
        cake: ["6.0.0", "6.*"]
    steps:
      - name: Checkout
        uses: actions/checkout@v7
        with:
          fetch-depth: 0
      - name: Set up .NET
        uses: ./.github/actions/setup-build
      - name: Run runner tests (Cake ${{ matrix.cake }})
        run: dotnet run --project build/Build.csproj -- --target RunnerTests --cake-version "${{ matrix.cake }}" --verbosity Diagnostic

  release-script:
    runs-on: ubuntu-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v7
      - name: Test release.ps1 version logic
        shell: pwsh
        run: |
          Import-Module Pester -MinimumVersion 5.0
          $config = New-PesterConfiguration
          $config.Run.Path = './test/Release.Tests.ps1'
          $config.Run.Exit = $true
          $config.Output.Verbosity = 'Detailed'
          Invoke-Pester -Configuration $config
```

`.github/workflows/pr.yml` is identical to `main.yml` except for the first lines:

```yaml
# yaml-language-server: $schema=https://json.schemastore.org/github-workflow.json
name: Pull Request
on: pull_request
```

Create it from `main.yml`, keeping everything from `env:` onwards:

```bash
{
  printf '# yaml-language-server: $schema=https://json.schemastore.org/github-workflow.json\nname: Pull Request\non: pull_request\n\n'
  sed -n '/^env:/,$p' .github/workflows/main.yml
} > .github/workflows/pr.yml
```

`.github/dependabot.yml`:

```yaml
version: 2
updates:
  - package-ecosystem: github-actions
    directory: /
    schedule:
      interval: weekly
  - package-ecosystem: nuget
    directory: /
    schedule:
      interval: weekly
```

- [ ] **Step 2: Port the release tooling from Cake.CycloneDX**

```bash
CDX=../Cake.CycloneDX
cp $CDX/build/ChangelogReleaseNotes.cs $CDX/build/GitHubRelease.cs $CDX/build/ReadmeInstallVersion.cs $CDX/build/Release.psm1 build/
cp $CDX/build/Tasks/DraftReleaseTask.cs $CDX/build/Tasks/PublishTask.cs $CDX/build/Tasks/ReleaseNotesTask.cs $CDX/build/Tasks/ReleaseTask.cs build/Tasks/
cp $CDX/release.ps1 .
cp $CDX/tests/Release.Tests.ps1 test/
cp $CDX/tests/Build.Tests/ChangelogReleaseNotesTests.cs $CDX/tests/Build.Tests/GitHubReleaseTests.cs $CDX/tests/Build.Tests/ReadmeInstallVersionTests.cs test/Build.Tests/
cp $CDX/.github/workflows/release.yml .github/workflows/
mkdir -p docs && cp $CDX/docs/release-policy.md docs/

PORTED="build/ChangelogReleaseNotes.cs build/GitHubRelease.cs build/ReadmeInstallVersion.cs build/Release.psm1 \
  build/Tasks/DraftReleaseTask.cs build/Tasks/PublishTask.cs build/Tasks/ReleaseNotesTask.cs build/Tasks/ReleaseTask.cs \
  release.ps1 test/Release.Tests.ps1 test/Build.Tests/ChangelogReleaseNotesTests.cs test/Build.Tests/GitHubReleaseTests.cs \
  test/Build.Tests/ReadmeInstallVersionTests.cs .github/workflows/release.yml docs/release-policy.md"

# Package name in regexes first (escaped dots), then in plain text.
sed -i -e 's/Cake\\\.CycloneDX/Cake\\.Download\\.Module/g' $PORTED
sed -i -e 's/Cake\.CycloneDX/Cake.Download.Module/g' $PORTED
# A module is installed with #module, not #addin.
sed -i -e 's/#addin/#module/g' build/ReadmeInstallVersion.cs test/Build.Tests/ReadmeInstallVersionTests.cs docs/release-policy.md
sed -i -e 's/Set up \.NET and CycloneDX/Set up .NET/' .github/workflows/release.yml
# This repository keeps all tests under test/, not tests/.
sed -i -e 's#tests/#test/#g' $PORTED

grep -rn -i "cyclonedx\|addin\|tests/" $PORTED
```

Expected after the last `grep`: no output. If anything is left:
- Prose saying "addin" becomes "module".
- A `Set up .NET and CycloneDX` step name becomes `Set up .NET`.
- Release workflow steps that install the CycloneDX tool are deleted.

Run `git diff --stat` to confirm that only the files listed above changed.

- [ ] **Step 3: Add the release members to BuildContext**

Replace `build/BuildContext.cs` with:

```csharp
#nullable enable
using Cake.Common;
using Cake.Common.Diagnostics;
using Cake.Common.IO;
using Cake.Core;
using Cake.Core.IO;
using Cake.Frosting;

namespace Build;

public class BuildContext : FrostingContext
{
    public const string Solution = "./src/Cake.Download.Module.slnx";

    public BuildContext(ICakeContext context)
        : base(context)
    {
        ArtifactsDirectory = context.Environment.WorkingDirectory.Combine("artifacts");
    }

    public string? NuGetApiKey => Environment.GetEnvironmentVariable("NUGET_API_KEY");

    public string? GitHubRefName => Environment.GetEnvironmentVariable("GITHUB_REF_NAME");

    /// <summary>Gets the directory packages are written to.</summary>
    public DirectoryPath ArtifactsDirectory { get; }

    /// <summary>Gets the package this build produces.</summary>
    public FilePath PackageFile => ArtifactsDirectory.CombineWithFilePath($"Cake.Download.Module.{ThisAssembly.PackageVersion}.nupkg");

    /// <summary>Gets the file the Release-Notes task writes the GitHub Release notes to.</summary>
    public FilePath ReleaseNotesFile => ArtifactsDirectory.CombineWithFilePath("release-notes.md");

    /// <summary>Gets the tag being released, from GITHUB_REF_NAME.</summary>
    public string ReleaseTag => GitHubRefName
        ?? throw new CakeException("GITHUB_REF_NAME environment variable is not set.");

    /// <summary>
    /// Gets the package matching the pushed tag: GITHUB_REF_NAME <c>v1.2.3</c> requires
    /// <c>artifacts/Cake.Download.Module.1.2.3.nupkg</c>.
    /// </summary>
    public FilePath ResolveReleasePackage()
    {
        var expected = ArtifactsDirectory.CombineWithFilePath(GitHubRelease.GetPackageFileName(GitHubRefName));
        if (!this.FileExists(expected))
        {
            var found = string.Join(", ", this.GetFiles(ArtifactsDirectory.FullPath + "/*.nupkg").Select(file => file.GetFilename().FullPath));
            throw new CakeException(
                $"Tag {GitHubRefName} requires {expected.GetFilename()}, but artifacts contains: {(found.Length == 0 ? "(nothing)" : found)}.");
        }

        this.Information("Release package: {0}", expected.GetFilename());
        return expected;
    }

    /// <summary>Gets whether the GitHub Release for the tag is missing, a draft or published.</summary>
    public GitHubReleaseState GetGitHubReleaseState(string tag)
    {
        var exitCode = this.StartProcess(
            "gh",
            new ProcessSettings { Arguments = GitHubRelease.View(tag), RedirectStandardOutput = true, RedirectStandardError = true },
            out var output,
            out var error);
        return GitHubRelease.ParseViewResult(exitCode, output, error);
    }

    /// <summary>Runs the GitHub CLI and throws on a non-zero exit code.</summary>
    public void RunGitHubCli(ProcessArgumentBuilder arguments)
    {
        var exitCode = this.StartProcess("gh", new ProcessSettings { Arguments = arguments });
        if (exitCode != 0)
        {
            throw new CakeException($"'gh {arguments.RenderSafe()}' failed (exit code {exitCode}).");
        }
    }
}
```

- [ ] **Step 4: Write the user documentation**

`README.md`:

````markdown
# Cake.Download.Module

A [Cake](https://cakebuild.net) module that installs command-line tools that are not on NuGet, such as GitHub
release assets or any file on an HTTPS server, with the `download:` scheme. It downloads the file for the current
platform, verifies its SHA-256, extracts it if it is an archive, and registers the tool so Cake's aliases and
`Context.Tools.Resolve(...)` find it.

## Install

Cake .NET Tool (`build.cake`):

```csharp
#module nuget:?package=Cake.Download.Module&version=0.1.0
#tool "download:https://github.com/jqlang/jq/releases/download/jq-{version}/jq-{os}-{arch}{exe}?package=jq&version=1.8.2&os.darwin=macos&checksums=sha256sum.txt&checksums_sha256=dc86824a41c165ece971ff691aff6e08bbfe6e1d1f531688b47ee78c283a85cd"
```

Cake SDK (`cake.cs`):

```csharp
#:sdk Cake.Sdk@6.3.0
#:package Cake.Download.Module@0.1.0

InstallTool("download:https://github.com/jqlang/jq/releases/download/jq-{version}/jq-{os}-{arch}{exe}?package=jq&version=1.8.2&os.darwin=macos&checksums=sha256sum.txt&checksums_sha256=dc86824a41c165ece971ff691aff6e08bbfe6e1d1f531688b47ee78c283a85cd");
```

Cake Frosting:

```csharp
return new CakeHost()
    .UseModule<Cake.Download.Module.DownloadModule>()
    .InstallTool(new Uri("download:https://github.com/jqlang/jq/releases/download/jq-{version}/jq-{os}-{arch}{exe}?package=jq&version=1.8.2&os.darwin=macos&checksums=sha256sum.txt&checksums_sha256=dc86824a41c165ece971ff691aff6e08bbfe6e1d1f531688b47ee78c283a85cd"))
    .Run(args);
```

## The directive

```
download:<url-template>?package=<name>&version=<version>[&<parameter>=<value>]*
```

| Parameter | Meaning |
|---|---|
| `package` | Tool name: install folder, default file name and default `include`. Required. |
| `version` | Exact version; fills `{version}`. Required. `latest` is rejected. |
| `sha256=<hex>` | Pin the downloaded file. |
| `sha256.<rid>=<hex>` | Pin the file per platform, e.g. `sha256.linux-x64=…`. |
| `checksums=<file>` + `checksums_sha256=<hex>` | Pin a checksums file (relative to the download URL, or absolute) and look the asset up in it. |
| `sha256=skip` | Install without verification (a warning is logged). |
| `dialect` | Placeholder vocabulary: `go` (default), `dotnet` or `rust`. |
| `os.<value>=`, `arch.<value>=`, `archive.<os>=` | Override a dialect value, e.g. `os.darwin=macOS`. |
| `triple.<rid>=` | Override the Rust target triple for one platform. |
| `url=` / `url.<rid>=` | Percent-encoded URL template instead of / per platform. |
| `format` | `file`, `zip`, `tar` or `tar.gz`; detected from the URL by default. |
| `filename` | File name for a raw download; default `<package>` plus `.exe` on Windows. |
| `include` / `exclude` | Globs (repeatable) choosing the files registered with Cake. Default: the raw file, or `**/<package>{exe}` in archives. |

Exactly one integrity option is required. Leave it out once, and the error message contains the exact parameter to
paste, with the hash of what was downloaded.

### Placeholders and dialects

| Placeholder | go | dotnet | rust |
|---|---|---|---|
| `{os}` | `windows` / `linux` / `darwin` | `win` / `linux` / `osx` | `windows` / `linux` / `darwin` |
| `{arch}` | `amd64` / `arm64` / `386` / `arm` | `x64` / `arm64` / `x86` / `arm` | `x86_64` / `aarch64` / `i686` / `armv7` |
| `{triple}` | – | – | e.g. `x86_64-pc-windows-msvc`, `aarch64-apple-darwin`, `x86_64-unknown-linux-musl` |

`{version}`, `{rid}` (`win-x64`, `linux-arm64`, `osx-arm64`, …), `{exe}` (`.exe` on Windows) and `{archive}` (`zip` on
Windows, `tar.gz` elsewhere) work in every dialect. Supported platforms: `win-x64`, `win-x86`, `win-arm64`,
`linux-x64`, `linux-arm64`, `linux-arm`, `osx-x64`, `osx-arm64`.

### Examples

```csharp
// Archive whose format differs per OS (gh)
#tool "download:https://github.com/cli/cli/releases/download/v{version}/gh_{version}_{os}_{arch}.{archive}?package=gh&version=2.62.0&os.darwin=macOS&archive.darwin=zip&checksums=gh_{version}_checksums.txt&checksums_sha256=89dc6f5225aa0c70d6f90950f5246225afff2f423f3d242f7d8813fe9993af4d"

// Rust target triples (ripgrep), per-platform pins
#tool "download:https://github.com/BurntSushi/ripgrep/releases/download/{version}/ripgrep-{version}-{triple}.{archive}?package=rg&version=14.1.1&dialect=rust&sha256.win-x64=d0f534024c42afd6cb4d38907c25cd2b249b79bbe6cc1dbee8e3e37c2b6e25a1&sha256.linux-x64=4cf9f2741e6c465ffdb7c26f38056a59e2a2544b51f7cc128ef28337eeae4d8e&sha256.osx-arm64=24ad76777745fbff131c8fbc466742b011f925bfa4fffa2ded6def23b5b937be"

// .NET RIDs (cyclonedx-cli)
#tool "download:https://github.com/CycloneDX/cyclonedx-cli/releases/download/v{version}/cyclonedx-{rid}{exe}?package=cyclonedx&version=0.30.0&dialect=dotnet&sha256.win-x64=1f563ba9644d2f2966fc8029fd701ca4af4f388d44c017c1d60559a1ecc9114f&sha256.linux-x64=f89876326620f5fc78a9b27cc1af57d6ed13d019aab87490e1246a44a910babb&sha256.osx-arm64=dabbaf07e543e7996f708147475e2daa69ddf8a8683c5b06febc7d3f074e5e24"
```

## Where tools go

Each tool is installed into `<tools>/<package>.<version>/` (Cake's tools folder, `./tools` by default). Archives are
extracted verbatim. A `.cake-download.json` file records what was installed, and later builds with the same directive
make no network requests. Different versions are installed side by side and never deleted automatically.

## Limitations

Only public HTTPS downloads: no authentication, private repositories, mirrors or signature verification. Archive
formats are zip, tar and tar.gz.

## License

MIT
````

`CHANGELOG.md`:

```markdown
# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `download:` scheme for `#tool` and `InstallTool`: downloads a tool over HTTPS, verifies its SHA-256 (`sha256`,
  `sha256.<rid>` or a pinned `checksums` file; `sha256=skip` to opt out), extracts zip, tar and tar.gz archives and
  registers the files matched by `include`/`exclude` with Cake's tool locator.
- Platform placeholders `{version}`, `{os}`, `{arch}`, `{rid}`, `{exe}`, `{archive}` and `{triple}` with `go`, `dotnet`
  and `rust` dialects and per-directive overrides.
- Idempotent installs into `<tools>/<package>.<version>/`: unchanged directives make no network requests.
```

`AGENTS.md`:

```markdown
# Agent Instructions

## Cake Module Guidelines

This is a Cake **module**: it is loaded with `#module`, `UseModule<DownloadModule>()` or `#:package`, and Cake never
installs a module's NuGet dependencies.

- **No runtime dependencies:** only BCL APIs. `PackageVerifier` fails the Pack target if the nupkg declares any
  dependency.
- **Cake references:** `Cake.Core` with `PrivateAssets="all"`, at the lowest compatible version (6.0.0). Raise it only
  when a newer Cake API is required, and say why in the commit message.
- **Target frameworks:** `net8.0`, `net9.0` and `net10.0`.
- **Public API:** only `DownloadModule` and `DownloadPackageInstaller` are public. Everything else is `internal`, tested
  through `InternalsVisibleTo`.
- **Design:** read `docs/superpowers/specs/2026-10-02-cake-download-module-design.md` before changing the directive
  grammar, the integrity model or the install layout.
- **Testing:** unit tests in `test/Cake.Download.Module.Tests` (`./build.ps1 --target Test`). Runner tests prove the
  packed module works on the Cake .NET Tool, Cake.Sdk and Cake Frosting against real GitHub releases:
  `./build.ps1 --target RunnerTests` (latest Cake 6.x) and `./build.ps1 --target RunnerTests --cake-version 6.0.0`.
  Keep the pipeline after `// --- pipeline ---` identical in `test/runners/script/build.cake` and
  `test/runners/sdk/cake.cs`, and the `download:` directives identical in all three runners.

## Changelog

`CHANGELOG.md` follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and is the source of the release notes
(see `docs/release-policy.md`). Update it in the same change as any user-visible change, under `## [Unreleased]`.
Leave out CI, tests, the build and refactoring. Breaking changes start with `**Breaking:**`. Don't add version headings
or dates; they are added when a release is tagged.

For a stable release, update the README install snippets (`#module …&version=X.Y.Z` and
`#:package Cake.Download.Module@X.Y.Z`) to the new version; the `Release-Notes` gate enforces this.
```

- [ ] **Step 5: Verify everything**

Run: `./build.ps1 --target All`
Expected: Build, Test (including the ported Build.Tests) and Pack succeed. `Verified Cake.Download.Module.<version>.nupkg` appears in the log.

Run: `pwsh -c "Import-Module Pester -MinimumVersion 5.0; Invoke-Pester ./test/Release.Tests.ps1"`
Expected: all Pester tests pass.

Run: `./build.ps1 --target RunnerTests`
Expected: all three runners pass.

- [ ] **Step 6: Commit**

```bash
git add .github/ build/ test/ release.ps1 docs/release-policy.md README.md CHANGELOG.md AGENTS.md
git commit -F - <<'EOF'
Add CI, release tooling and user documentation

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

## Spec coverage

| Spec section | Task |
|---|---|
| §4.1 parsing facts, §4.2 parameters and validation | 3 |
| §4.3 placeholders, §4.4 platforms and dialects | 1, 2, 4 |
| §5 integrity model and paste-ready messages | 3, 4, 6, 10 |
| §6 platform detection | 1 |
| §7 architecture, public surface, module registration | 10 |
| §8.1–8.4 layout, marker, cache hit, optimistic concurrency | 9, 10 |
| §8.5 extraction safety and file modes | 7, 8 |
| §8.6 HTTP (redirects, stall, retries, https-only, no credentials) | 5 |
| §9 errors and logging | 5, 6, 7, 8, 10 |
| §10 packaging and compatibility | 1, 11 |
| §11.1 unit tests, §11.2 runner tests, §11.3 package verification, §11.4 CI | 1–10, 12, 11, 13 |
| §12 repository conventions | 1, 11, 13 |
| §14 items to verify | 3 (Uri parsing), 12 (SDK, Cake.Tool 6.0.0, Frosting), 1 (compiling against Cake.Core 6.0.0) |
