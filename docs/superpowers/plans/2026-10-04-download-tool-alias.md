# DownloadTool Alias Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `DownloadTool` Cake alias that installs a `download:` tool when it is called (typically inside a
task), with a directive form and a typed `DownloadToolSettings` form, plus README docs for on-demand installs.

**Architecture:** The alias is a thin public layer over an internal `DownloadToolRunner`. Every input becomes a
`download:` directive string: typed settings are written out by `DownloadToolSettings.ToDirective()`. The string then
goes through the existing `DirectiveParser` → `DownloadPlanner` → `DownloadPackageInstaller` pipeline, unchanged except
for an internal `DirectiveSource` that lets error hints use settings-method syntax. Files are registered with
`context.Tools`, without the Cake container, so the alias works without `#module`.

**Tech Stack:** C# (net8.0/net9.0/net10.0), Cake.Core 6.0.0 (`PrivateAssets="all"`), xUnit v3 on Microsoft Testing
Platform, Cake.Testing fakes, the repo's Frosting `build/` project for runner tests.

**Spec:** `docs/superpowers/specs/2026-10-04-download-tool-alias-design.md` (builds on
`docs/superpowers/specs/2026-10-02-cake-download-module-design.md`). Read both before starting.

## Global Constraints

- No runtime dependencies: BCL only. `PackageVerifier` fails the Pack target if the nupkg declares any dependency.
- `Cake.Core` stays referenced with `PrivateAssets="all"` at 6.0.0. Do not raise it.
- Target frameworks: `net8.0`, `net9.0`, `net10.0`.
- Public types after this work, and no others: `DownloadModule`, `DownloadPackageInstaller`, `DownloadToolAliases`,
  `DownloadToolSettings`, `DownloadDialect`, `DownloadFormat`. Everything else is `internal`, tested through
  `InternalsVisibleTo`.
- Alias overloads are plain overloads; no optional parameters on public alias methods.
- Fluent `With…` methods do not validate content (null checks only). All content validation stays in `DirectiveParser`.
- Keep the pipeline after `// --- pipeline ---` identical in `test/runners/script/build.cake` and
  `test/runners/sdk/cake.cs`, and the `download:` directive strings identical across all three runners.
- `CHANGELOG.md`: user-visible changes go under `## [Unreleased]` in the same commit; no CI/test/build/refactoring
  entries; no version headings or dates.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- **Stop rule (spec §5.1):** if the script runner cannot load the same package through both `#module` and `#addin`,
  or Cake.Sdk does not generate the `DownloadTool` proxies, **stop and report to the user**. Do not pick a fallback.

## Review Focus

1. **`%`-sequences in URLs** (`my%20tool`) passed through settings: users expect them to reach the server unchanged,
   whether `ToDirective()` writes the URL as the leading template or as `url=` → test in Task 4.
2. **Mixed-case RID keys** (`WithSha256("Linux-X64", …)`): users expect them to match `linux-x64` as the directive
   does → test in Task 4.
3. **One settings instance reused** across several four-argument calls with different packages: users expect no
   conflict error and an unchanged settings object → test in Task 5.
4. **Relative `Paths_Tools`** (`./build-tools`): users expect it to resolve against the working directory, like
   `#tool` → test in Task 2.
5. **Empty strings** for `Package`/`Version`/`Url` in settings: users expect the "missing" error, not a confusing
   parser error → test in Task 4.

---

### Task 1: Form-aware error hints (`DirectiveSource`)

Error messages that tell the user what to paste must use settings-method syntax when the input came from
`DownloadToolSettings`. Directive inputs keep today's messages exactly.

**Files:**
- Create: `src/Cake.Download.Module/Directives/DirectiveSource.cs`
- Create: `src/Cake.Download.Module/Directives/Hints.cs`
- Modify: `src/Cake.Download.Module/Directives/DownloadDirective.cs` (add `Source`)
- Modify: `src/Cake.Download.Module/Directives/DownloadPlan.cs` (add `Source` to `DownloadPlan`)
- Modify: `src/Cake.Download.Module/Directives/DirectiveParser.cs:26` (`Parse` gets a `source` parameter)
- Modify: `src/Cake.Download.Module/Directives/DownloadPlanner.cs` (copy `Source` into the plan)
- Modify: `src/Cake.Download.Module/Integrity/IntegrityResolver.cs` (use `Hints`)
- Modify: `src/Cake.Download.Module/DownloadPackageInstaller.cs` (internal `Install` overload with `source`; 404 hint)
- Test: `test/Cake.Download.Module.Tests/DownloadPackageInstallerTests.cs`

**Interfaces:**
- Produces: `internal enum DirectiveSource { Directive, Settings }` (namespace `Cake.Download.Module.Directives`);
  `DirectiveParser.Parse(PackageReference reference, DirectiveSource source = DirectiveSource.Directive)`;
  `internal IReadOnlyCollection<IFile> DownloadPackageInstaller.Install(PackageReference package, PackageType type, DirectoryPath path, DirectiveSource source)`.

- [ ] **Step 1: Write the failing tests**

In `DownloadPackageInstallerTests.cs`, add `using Cake.Download.Module.Directives;`, change the `Install` helper to
take a source, and add the tests:

```csharp
    [Theory]
    [InlineData(JqDirective, ".WithSha256(\"linux-x64\", \"{0}\")")]
    [InlineData("download:https://example.com/jq-linux-amd64?package=jq&version=1.8.2", ".WithSha256(\"{0}\")")]
    public void Install_From_Settings_Without_Integrity_Suggests_A_Settings_Call(string directive, string call)
    {
        var exception = Assert.Throws<CakeException>(() => Install(directive, source: DirectiveSource.Settings));

        Assert.StartsWith("The DownloadToolSettings for 'jq' have no integrity check.", exception.Message);
        Assert.Contains($"Add {string.Format(call, TestHashes.Sha256(RawContent))} to the DownloadToolSettings, or .WithoutVerification()", exception.Message);
        Assert.DoesNotContain("&sha256", exception.Message);
    }

    [Fact]
    public void Install_From_Settings_With_An_Unpinned_Checksums_File_Suggests_WithChecksums()
    {
        const string Sums = "0000000000000000000000000000000000000000000000000000000000000000  jq-linux-amd64\n";
        _handler.Respond("https://example.com/SHA256SUMS", FakeHttpHandler.Ok(Sums));

        var exception = Assert.Throws<CakeException>(() => Install(JqDirective + "&checksums=SHA256SUMS", source: DirectiveSource.Settings));

        Assert.Contains($"Use .WithChecksums(\"https://example.com/SHA256SUMS\", \"{TestHashes.Sha256(Sums)}\") in the DownloadToolSettings for 'jq'.", exception.Message);
    }

    [Fact]
    public void Install_From_A_Directive_With_An_Unpinned_Checksums_File_Suggests_The_Parameter()
    {
        const string Sums = "0000000000000000000000000000000000000000000000000000000000000000  jq-linux-amd64\n";
        _handler.Respond("https://example.com/SHA256SUMS", FakeHttpHandler.Ok(Sums));

        var exception = Assert.Throws<CakeException>(() => Install(JqDirective + "&checksums=SHA256SUMS"));

        Assert.Contains($"Add '&checksums_sha256={TestHashes.Sha256(Sums)}' to the directive for 'jq'.", exception.Message);
    }

    [Fact]
    public void Install_From_Settings_Explains_A_404_With_Settings_Overrides()
    {
        var exception = Assert.Throws<CakeException>(() => Install(JqDirective + "&sha256=skip", "osx-arm64", source: DirectiveSource.Settings));

        Assert.StartsWith("https://example.com/jq-darwin-arm64 was not found (HTTP 404).", exception.Message);
        Assert.Contains(".WithOs(…), .WithArch(…), .WithArchive(…) or .WithUrl(\"osx-arm64\", …) to the DownloadToolSettings.", exception.Message);
        Assert.DoesNotContain("'url.osx-arm64='", exception.Message);
    }
```

Replace the existing `Install` helper with:

```csharp
    private IReadOnlyCollection<IFile> Install(
        string directive,
        string rid = "linux-x64",
        string? tools = null,
        FakeLog? log = null,
        DirectiveSource source = DirectiveSource.Directive) =>
        CreateInstaller(rid, log).Install(new PackageReference(directive), PackageType.Tool, new DirectoryPath(tools ?? _tools), source);
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project test/Cake.Download.Module.Tests/Cake.Download.Module.Tests.csproj --filter-class "*DownloadPackageInstallerTests"`
Expected: build FAILS with `CS0246: The type or namespace name 'DirectiveSource' could not be found`.

- [ ] **Step 3: Add `DirectiveSource`**

`src/Cake.Download.Module/Directives/DirectiveSource.cs`:

```csharp
namespace Cake.Download.Module.Directives;

/// <summary>
/// The form the user wrote a download in, so error hints can be phrased in that form.
/// </summary>
internal enum DirectiveSource
{
    /// <summary>A <c>download:</c> directive: <c>#tool</c>, <c>InstallTool</c> or the directive alias.</summary>
    Directive,

    /// <summary>A <c>DownloadToolSettings</c> passed to the settings alias.</summary>
    Settings,
}
```

- [ ] **Step 4: Add `Hints`**

`src/Cake.Download.Module/Directives/Hints.cs`. The directive-form strings are exactly today's messages:

```csharp
namespace Cake.Download.Module.Directives;

/// <summary>
/// The paste-ready parts of error messages, in the form the user wrote: directive parameters or
/// <c>DownloadToolSettings</c> methods.
/// </summary>
internal static class Hints
{
    public static string NoIntegrity(DirectiveSource source, string package) => source == DirectiveSource.Settings
        ? $"The DownloadToolSettings for '{package}' have no integrity check."
        : $"The download directive for '{package}' has no integrity check.";

    public static string AddPin(DirectiveSource source, string parameter, string sha256) => source == DirectiveSource.Settings
        ? $"Add {PinCall(parameter, sha256)} to the DownloadToolSettings, or .WithoutVerification() to install without verification (not recommended)."
        : $"Add '&{parameter}={sha256}' to the directive, or '&sha256=skip' to install without verification (not recommended).";

    public static string PinChecksums(DirectiveSource source, string package, string checksumsUrl, string sha256) => source == DirectiveSource.Settings
        ? $"Use .WithChecksums(\"{checksumsUrl}\", \"{sha256}\") in the DownloadToolSettings for '{package}'."
        : $"Add '&checksums_sha256={sha256}' to the directive for '{package}'.";

    public static string NotFoundOverrides(DirectiveSource source, string rid) => source == DirectiveSource.Settings
        ? "If the asset is named differently on this platform, add an override such as .WithOs(…), .WithArch(…), .WithArchive(…) " +
          $"or .WithUrl(\"{rid}\", …) to the DownloadToolSettings."
        : "If the asset is named differently on this platform, add an override such as 'os.<value>=', 'arch.<value>=', 'archive.<value>=' " +
          $"or 'url.{rid}=' to the directive.";

    private static string PinCall(string parameter, string sha256) =>
        parameter.StartsWith("sha256.", StringComparison.Ordinal)
            ? $".WithSha256(\"{parameter["sha256.".Length..]}\", \"{sha256}\")"
            : $".WithSha256(\"{sha256}\")";
}
```

- [ ] **Step 5: Carry the source from parser to plan**

In `DownloadDirective` (after `Integrity`):

```csharp
    public DirectiveSource Source { get; init; } = DirectiveSource.Directive;
```

In `DownloadPlan` (after `Integrity`):

```csharp
    public DirectiveSource Source { get; init; } = DirectiveSource.Directive;
```

In `DirectiveParser.Parse`, change the signature and set the property in the returned object initializer:

```csharp
    public static DownloadDirective Parse(PackageReference reference, DirectiveSource source = DirectiveSource.Directive)
```

```csharp
            Integrity = ParseIntegrity(Single("sha256"), sha256ByRid, Single("checksums"), Single("checksums_sha256"), Fail),
            Source = source,
        };
```

In `DownloadPlanner.Create`, add to the returned `DownloadPlan` initializer:

```csharp
            Integrity = PlanIntegrity(directive, rid, url, placeholders, platformSpecific),
            Source = directive.Source,
        };
```

- [ ] **Step 6: Use `Hints` in `IntegrityResolver`**

Replace the `MissingIntegrityPlan` throw in `Verify`:

```csharp
        if (plan.Integrity is MissingIntegrityPlan missing)
        {
            throw new CakeException(
                $"{Hints.NoIntegrity(plan.Source, plan.Package)} {plan.AssetName} ({plan.Platform.Rid}) has SHA-256 {actualSha256}. " +
                $"{Hints.AddPin(plan.Source, missing.Parameter, actualSha256)} " +
                "The download was discarded.");
        }
```

Replace the unpinned-checksums throw in `FromChecksumsFile`:

```csharp
        if (checksums.Sha256 is null)
        {
            throw new CakeException(
                $"The checksums file {url} is not pinned. Its SHA-256 is {download.Sha256}. " +
                Hints.PinChecksums(plan.Source, plan.Package, url, download.Sha256));
        }
```

- [ ] **Step 7: Internal `Install` overload and 404 hint in `DownloadPackageInstaller`**

Replace the body of the public `Install` with a call to a new internal overload that holds the old body, parsing
with the source:

```csharp
    public IReadOnlyCollection<IFile> Install(PackageReference package, PackageType type, DirectoryPath path) =>
        Install(package, type, path, DirectiveSource.Directive);

    internal IReadOnlyCollection<IFile> Install(PackageReference package, PackageType type, DirectoryPath path, DirectiveSource source)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(path);

        var directive = DirectiveParser.Parse(package, source);
        // … the rest of the old body, unchanged …
    }
```

Keep the existing XML `<summary>`/`<param>`/`<returns>`/`<exception>` docs on the public method. In `NotFoundMessage`,
replace the last two string pieces with the hint:

```csharp
        return $"{plan.Url.AbsoluteUri} was not found (HTTP 404). Detected platform {plan.Platform.Rid}; dialect '{plan.Dialect}' expanded {expanded}. " +
            Hints.NotFoundOverrides(plan.Source, plan.Platform.Rid);
```

- [ ] **Step 8: Run all unit tests**

Run: `dotnet test --project test/Cake.Download.Module.Tests/Cake.Download.Module.Tests.csproj`
Expected: PASS, including the existing directive-form message tests (`Install_Without_Integrity_Fails_With_The_Parameter_To_Paste`,
`Install_Explains_A_404_With_The_Expanded_Placeholders`) unchanged.

- [ ] **Step 9: Commit**

```bash
git add src/Cake.Download.Module test/Cake.Download.Module.Tests
git commit -m "Phrase paste-ready error hints in the form the user wrote

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

No CHANGELOG entry: nothing is user-visible until the settings alias exists.

---

### Task 2: Directive-family alias and `DownloadToolRunner`

**Files:**
- Create: `src/Cake.Download.Module/DownloadToolRunner.cs`
- Create: `src/Cake.Download.Module/DownloadToolAliases.cs`
- Create: `test/Cake.Download.Module.Tests/Fakes/TestCakeContext.cs`
- Create: `test/Cake.Download.Module.Tests/Fakes/RecordingToolLocator.cs`
- Create: `test/Cake.Download.Module.Tests/DownloadToolAliasesTests.cs`
- Modify: `src/Directory.Build.props:25` (add `cake-addin` tag)
- Modify: `build/PackageVerifier.cs` (require `cake-addin`)
- Modify: `test/Build.Tests/PackageVerifierTests.cs`
- Modify: `CHANGELOG.md`

**Interfaces:**
- Consumes: `DownloadPackageInstaller.Install(PackageReference, PackageType, DirectoryPath, DirectiveSource)` (Task 1).
- Produces:
  - `public static IReadOnlyCollection<FilePath> DownloadToolAliases.DownloadTool(this ICakeContext context, string directive)`
  - `public static IReadOnlyCollection<FilePath> DownloadToolAliases.DownloadTool(this ICakeContext context, Uri directive)`
  - `internal sealed class DownloadToolRunner(ICakeContext context, DownloadPackageInstaller installer)` with
    `public IReadOnlyCollection<FilePath> Install(string directive)` and
    `internal IReadOnlyCollection<FilePath> Install(PackageReference reference, DirectiveSource source)`.
  - Test fakes `TestCakeContext` (property `RecordingToolLocator Tools`) and `RecordingToolLocator`
    (property `List<FilePath> Registered`).

- [ ] **Step 1: Add the test fakes**

`test/Cake.Download.Module.Tests/Fakes/RecordingToolLocator.cs`:

```csharp
using Cake.Core.IO;
using Cake.Core.Tooling;

namespace Cake.Download.Module.Tests.Fakes;

internal sealed class RecordingToolLocator : IToolLocator
{
    public List<FilePath> Registered { get; } = [];

    public void RegisterFile(FilePath path) => Registered.Add(path);

    public FilePath? Resolve(string tool) =>
        Registered.LastOrDefault(path => string.Equals(path.GetFilename().FullPath, tool, StringComparison.OrdinalIgnoreCase));

    public FilePath? Resolve(IEnumerable<string> toolExeNames) =>
        toolExeNames.Select(Resolve).FirstOrDefault(path => path is not null);
}
```

`test/Cake.Download.Module.Tests/Fakes/TestCakeContext.cs` (if Cake.Core 6.0.0 declares more `ICakeContext` members
than listed, implement them as `throw new NotSupportedException()`):

```csharp
using Cake.Core;
using Cake.Core.Configuration;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using Cake.Core.Tooling;

namespace Cake.Download.Module.Tests.Fakes;

internal sealed class TestCakeContext(ICakeEnvironment environment, ICakeLog log, ICakeConfiguration configuration) : ICakeContext
{
    public RecordingToolLocator Tools { get; } = new();

    public IFileSystem FileSystem { get; } = new FileSystem();

    public ICakeEnvironment Environment => environment;

    public IGlobber Globber => throw new NotSupportedException();

    public ICakeLog Log => log;

    public ICakeArguments Arguments => throw new NotSupportedException();

    public IProcessRunner ProcessRunner => throw new NotSupportedException();

    public IRegistry Registry => throw new NotSupportedException();

    public ICakeDataResolver Data => throw new NotSupportedException();

    public ICakeConfiguration Configuration => configuration;

    IToolLocator ICakeContext.Tools => Tools;
}
```

- [ ] **Step 2: Write the failing tests**

`test/Cake.Download.Module.Tests/DownloadToolAliasesTests.cs`:

```csharp
using Cake.Core;
using Cake.Core.IO;
using Cake.Core.Packaging;
using Cake.Download.Module.Directives;
using Cake.Download.Module.Http;
using Cake.Download.Module.Platforms;
using Cake.Download.Module.Tests.Fakes;
using Cake.Testing;
using Path = System.IO.Path;

namespace Cake.Download.Module.Tests;

public sealed class DownloadToolAliasesTests : IDisposable
{
    private const string RawContent = "#!/bin/sh\necho jq\n";
    private const string JqUrl = "https://example.com/jq-linux-amd64";

    private static readonly string JqDirective =
        "download:https://example.com/jq-{os}-{arch}?package=jq&version=1.8.2&sha256.linux-x64=" + TestHashes.Sha256(RawContent);

    private readonly TestDirectory _directory = new();
    private readonly FakeHttpHandler _handler = new();
    private readonly FakeLog _log = new();
    private readonly FakeConfiguration _configuration = new();
    private readonly TestCakeContext _context;

    public DownloadToolAliasesTests()
    {
        var environment = FakeEnvironment.CreateUnixEnvironment();
        environment.WorkingDirectory = new DirectoryPath(_directory.Root);
        _context = new TestCakeContext(environment, _log, _configuration);
        _handler.Respond(JqUrl, FakeHttpHandler.Ok(RawContent));
    }

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void Install_Registers_And_Returns_The_Files_In_The_Default_Tools_Folder()
    {
        var path = Assert.Single(CreateRunner().Install(JqDirective));

        Assert.Equal(_directory.Combine("tools", "jq.1.8.2", "jq"), Normalize(path));
        Assert.Equal([path.FullPath], _context.Tools.Registered.Select(registered => registered.FullPath));
        Assert.Equal(RawContent, File.ReadAllText(path.FullPath));
    }

    [Fact]
    public void Install_Uses_An_Absolute_Paths_Tools()
    {
        _configuration.SetValue("Paths_Tools", _directory.Combine("custom-tools"));

        var path = Assert.Single(CreateRunner().Install(JqDirective));

        Assert.Equal(_directory.Combine("custom-tools", "jq.1.8.2", "jq"), Normalize(path));
    }

    [Fact]
    public void Install_Resolves_A_Relative_Paths_Tools_Against_The_Working_Directory()
    {
        _configuration.SetValue("Paths_Tools", "./build-tools");

        var path = Assert.Single(CreateRunner().Install(JqDirective));

        Assert.Equal(_directory.Combine("build-tools", "jq.1.8.2", "jq"), Normalize(path));
    }

    // Cake paths use '/' and may keep a "./" segment; compare as normalized OS paths.
    private static string Normalize(FilePath path) => Path.GetFullPath(path.FullPath);

    [Fact]
    public void Install_Twice_Makes_No_Second_Request_And_Registers_Again()
    {
        CreateRunner().Install(JqDirective);
        var requests = _handler.RequestedUrls.Count;

        CreateRunner().Install(JqDirective);

        Assert.Equal(requests, _handler.RequestedUrls.Count);
        Assert.Equal(2, _context.Tools.Registered.Count);
    }

    [Fact]
    public void DownloadTool_Rejects_A_Value_That_Is_Not_A_Download_Directive()
    {
        var exception = Assert.Throws<CakeException>(() => _context.DownloadTool("nuget:?package=jq&version=1.8.2"));

        Assert.Equal("Invalid download directive 'nuget:?package=jq&version=1.8.2': it must start with 'download:'.", exception.Message);
        Assert.Empty(_context.Tools.Registered);
    }

    [Fact]
    public void DownloadTool_With_A_Uri_Uses_The_Original_String()
    {
        var exception = Assert.Throws<CakeException>(
            () => _context.DownloadTool(new Uri("download:https://example.com/{version}/jq?version=1.8.2")));

        Assert.Equal(
            "Invalid download directive 'download:https://example.com/{version}/jq?version=1.8.2': the 'package' parameter is required.",
            exception.Message);
    }

    [Fact]
    public void DownloadTool_Rejects_Null_Arguments()
    {
        Assert.Throws<ArgumentNullException>(() => DownloadToolAliases.DownloadTool(null!, JqDirective));
        Assert.Throws<ArgumentNullException>(() => _context.DownloadTool((string)null!));
        Assert.Throws<ArgumentNullException>(() => _context.DownloadTool((Uri)null!));
    }

    private DownloadToolRunner CreateRunner() => new(_context, new DownloadPackageInstaller(
        _context.Environment,
        _context.FileSystem,
        _log,
        new FixedPlatformDetector(PlatformInfo.FromRid("linux-x64")),
        _handler,
        new DownloadOptions { Delay = (_, _) => Task.CompletedTask },
        TimeProvider.System));
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test --project test/Cake.Download.Module.Tests/Cake.Download.Module.Tests.csproj --filter-class "*DownloadToolAliasesTests"`
Expected: build FAILS with `CS0246` for `DownloadToolRunner` and `DownloadToolAliases`.

- [ ] **Step 4: Implement `DownloadToolRunner`**

`src/Cake.Download.Module/DownloadToolRunner.cs`:

```csharp
using Cake.Core;
using Cake.Core.IO;
using Cake.Core.Packaging;
using Cake.Download.Module.Directives;

namespace Cake.Download.Module;

/// <summary>
/// What the <see cref="DownloadToolAliases"/> do: install into Cake's tools folder and register the files with
/// <c>context.Tools</c>. The installer is passed in so tests can fake the network and the platform.
/// </summary>
internal sealed class DownloadToolRunner(ICakeContext context, DownloadPackageInstaller installer)
{
    public IReadOnlyCollection<FilePath> Install(string directive) =>
        Install(new PackageReference(directive), DirectiveSource.Directive);

    internal IReadOnlyCollection<FilePath> Install(PackageReference reference, DirectiveSource source)
    {
        var toolsPath = context.Configuration.GetToolPath(context.Environment.WorkingDirectory, context.Environment);
        var files = installer.Install(reference, PackageType.Tool, toolsPath, source);

        var paths = new List<FilePath>(files.Count);
        foreach (var file in files)
        {
            context.Tools.RegisterFile(file.Path);
            paths.Add(file.Path);
        }

        return paths;
    }
}
```

- [ ] **Step 5: Implement `DownloadToolAliases` (directive family)**

`src/Cake.Download.Module/DownloadToolAliases.cs`:

```csharp
using Cake.Core;
using Cake.Core.Annotations;
using Cake.Core.IO;

namespace Cake.Download.Module;

/// <summary>
/// Installs <c>download:</c> tools when the alias is called, typically inside a task, and registers them with Cake's
/// tool locator. Unlike <c>#tool</c> and <c>InstallTool</c>, runs that don't execute the call download nothing.
/// </summary>
[CakeAliasCategory("Download")]
[CakeNamespaceImport("Cake.Download.Module")]
public static class DownloadToolAliases
{
    /// <summary>
    /// Installs the tool described by a complete <c>download:</c> directive (the text <c>#tool</c> takes), unless an
    /// identical install is already there, and registers its files with <c>context.Tools</c>.
    /// </summary>
    /// <param name="context">The Cake context.</param>
    /// <param name="directive">The directive, starting with <c>download:</c>.</param>
    /// <returns>The registered files.</returns>
    /// <exception cref="CakeException">The directive is invalid, or downloading, verifying or extracting failed.</exception>
    /// <example>
    /// <code>
    /// Task("Sbom").Does(() =>
    /// {
    ///     DownloadTool("download:https://github.com/jqlang/jq/releases/download/jq-{version}/jq-{os}-{arch}{exe}?package=jq&amp;version=1.8.2&amp;os.darwin=macos&amp;checksums=sha256sum.txt&amp;checksums_sha256=dc86824a41c165ece971ff691aff6e08bbfe6e1d1f531688b47ee78c283a85cd");
    /// });
    /// </code>
    /// </example>
    [CakeMethodAlias]
    public static IReadOnlyCollection<FilePath> DownloadTool(this ICakeContext context, string directive)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(directive);

        return CreateRunner(context).Install(directive);
    }

    /// <summary>
    /// Installs the tool described by a complete <c>download:</c> directive, the same <see cref="Uri"/> Frosting's
    /// <c>CakeHost.InstallTool</c> takes, and registers its files with <c>context.Tools</c>.
    /// </summary>
    /// <param name="context">The Cake context.</param>
    /// <param name="directive">The directive; its <see cref="Uri.OriginalString"/> is used, so <c>{…}</c> placeholders survive.</param>
    /// <returns>The registered files.</returns>
    /// <exception cref="CakeException">The directive is invalid, or downloading, verifying or extracting failed.</exception>
    [CakeMethodAlias]
    public static IReadOnlyCollection<FilePath> DownloadTool(this ICakeContext context, Uri directive)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(directive);

        return CreateRunner(context).Install(directive.OriginalString);
    }

    private static DownloadToolRunner CreateRunner(ICakeContext context) =>
        new(context, new DownloadPackageInstaller(context.Environment, context.FileSystem, context.Log));
}
```

If the compiler rejects `[CakeNamespaceImport]` on a class, move it to the assembly in `DownloadModule.cs`:
`[assembly: CakeNamespaceImport("Cake.Download.Module")]`.

- [ ] **Step 6: Run the alias tests**

Run: `dotnet test --project test/Cake.Download.Module.Tests/Cake.Download.Module.Tests.csproj --filter-class "*DownloadToolAliasesTests"`
Expected: PASS (7 tests).

- [ ] **Step 7: Require the `cake-addin` tag (test first)**

In `test/Build.Tests/PackageVerifierTests.cs`, change the constant and the missing-tag test, and add one for the
addin tag:

```csharp
    private const string Tags = "cake,cake-module,cake-addin,cake-build,download,tool";
```

```csharp
    [Fact]
    public void Verify_Reports_A_Missing_Cake_Module_Tag()
    {
        var package = CreatePackage(Libraries.Concat(["icon.png", "README.md"]), Nuspec("cake,cake-addin"));

        Assert.Equal(["nuspec tags do not contain 'cake-module'"], PackageVerifier.Verify(package));
    }

    [Fact]
    public void Verify_Reports_A_Missing_Cake_Addin_Tag()
    {
        var package = CreatePackage(Libraries.Concat(["icon.png", "README.md"]), Nuspec("cake,cake-module"));

        Assert.Equal(["nuspec tags do not contain 'cake-addin'"], PackageVerifier.Verify(package));
    }
```

Run: `dotnet test --project test/Build.Tests/Build.Tests.csproj`
Expected: FAIL in `Verify_Reports_A_Missing_Cake_Addin_Tag` (no problem reported).

In `build/PackageVerifier.cs`, replace the single tag check with a loop, and update the class `<summary>` to mention
both tags:

```csharp
        var tagList = tags.Split(TagSeparators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var required in RequiredTags)
        {
            if (!tagList.Contains(required))
            {
                problems.Add($"nuspec tags do not contain '{required}'");
            }
        }
```

with `private static readonly string[] RequiredTags = ["cake-module", "cake-addin"];` next to `TargetFrameworks`.

In `src/Directory.Build.props:25`:

```xml
    <PackageTags>cake,cake-module,cake-addin,cake-build,download,tool,github,release,binary</PackageTags>
```

Run: `dotnet test --project test/Build.Tests/Build.Tests.csproj`
Expected: PASS.

- [ ] **Step 8: CHANGELOG**

Under `## [Unreleased]` → `### Added` in `CHANGELOG.md`, append:

```markdown
- `DownloadTool` alias: installs a `download:` tool when it is called, for example inside a task, and registers it with
  Cake's tool locator, so runs that don't execute that task (other targets, `--dryrun`) download nothing. It takes the
  same directive as `#tool`, as a `string` or `Uri`. Scripts load the alias with
  `#addin nuget:?package=Cake.Download.Module`; Frosting and Cake.Sdk need only the package reference.
```

- [ ] **Step 9: Run everything and commit**

Run: `./build.ps1 --target Test`
Expected: PASS.

```bash
git add src build test CHANGELOG.md
git commit -m "Add the DownloadTool alias for directives

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Runner tests for the directive alias — verification gate

This task verifies spec §5.1 in real runners. It moves `jq` from an eager install to an on-demand `DownloadTool` call
in all three runners, and proves `--dryrun` doesn't install it.

**Files:**
- Modify: `test/runners/script/build.cake`
- Modify: `test/runners/sdk/cake.cs`
- Modify: `test/runners/frosting/Program.cs`
- Create: `test/runners/frosting/OnDemandTask.cs`
- Modify: `test/runners/frosting/DefaultTask.cs`
- Modify: `build/RunnerTests/IRunner.cs`, `ScriptRunner.cs`, `SdkRunner.cs`, `FrostingRunner.cs`
- Modify: `build/RunnerTests/ReportAssertions.cs`
- Modify: `build/RunnerTests/ScriptTemplate.cs` (error message wording only)
- Modify: `build/Tasks/RunnerTestsTask.cs`
- Test: `test/Build.Tests/ReportAssertionsTests.cs` (create if missing; otherwise add to it)

**Interfaces:**
- Consumes: `DownloadTool(string directive)` alias (Task 2), the packed package from `./build.ps1 --target Pack`.
- Produces: `IRunner.Run(ICakeContext context, RunnerTestContext test, bool dryRun)`;
  `ReportAssertions.OnDemandInstallFolders` (`string[]`), `ReportAssertions.CheckDryRun(string toolsDirectory)`
  (`IReadOnlyList<string>`).

- [ ] **Step 1: Write the failing `CheckDryRun` test**

`test/Build.Tests/ReportAssertionsTests.cs` (add the class if the file doesn't exist; add the methods if it does):

```csharp
using Build.RunnerTests;

namespace Build.Tests;

public sealed class ReportAssertionsTests : IDisposable
{
    private readonly string _tools = Directory.CreateTempSubdirectory("ReportAssertionsTests").FullName;

    public void Dispose() => Directory.Delete(_tools, recursive: true);

    [Fact]
    public void CheckDryRun_Accepts_A_Tools_Folder_Without_On_Demand_Installs()
    {
        Directory.CreateDirectory(Path.Combine(_tools, "gh.2.62.0"));

        Assert.Empty(ReportAssertions.CheckDryRun(_tools));
    }

    [Fact]
    public void CheckDryRun_Reports_An_On_Demand_Install()
    {
        Directory.CreateDirectory(Path.Combine(_tools, "jq.1.8.2"));

        Assert.Equal(["--dryrun installed jq.1.8.2; DownloadTool must only install when its task runs"], ReportAssertions.CheckDryRun(_tools));
    }
}
```

Run: `dotnet test --project test/Build.Tests/Build.Tests.csproj`
Expected: build FAILS (`CheckDryRun` not found).

- [ ] **Step 2: Implement `CheckDryRun`**

In `build/RunnerTests/ReportAssertions.cs`:

```csharp
    /// <summary>Install folders of the tools the scenario installs with <c>DownloadTool</c> inside a task.</summary>
    public static readonly string[] OnDemandInstallFolders = ["jq.1.8.2"];

    public static IReadOnlyList<string> CheckDryRun(string toolsDirectory) =>
        OnDemandInstallFolders
            .Where(folder => Directory.Exists(Path.Combine(toolsDirectory, folder)))
            .Select(folder => $"--dryrun installed {folder}; DownloadTool must only install when its task runs")
            .ToList();
```

Run: `dotnet test --project test/Build.Tests/Build.Tests.csproj`
Expected: PASS.

- [ ] **Step 3: Add `dryRun` to the runners**

`IRunner.cs`:

```csharp
    /// <param name="dryRun">Pass <c>--dryrun</c>, so Cake runs no task.</param>
    /// <returns>The exit code; 0 means success.</returns>
    int Run(ICakeContext context, RunnerTestContext test, bool dryRun);
```

In each runner's `Run`, build the arguments as today and append `--dryrun` when asked. `ScriptRunner`:

```csharp
    public int Run(ICakeContext context, RunnerTestContext test, bool dryRun)
    {
        var executable = ToolDirectory(test).CombineWithFilePath(context.IsRunningOnWindows() ? "dotnet-cake.exe" : "dotnet-cake");
        var arguments = new ProcessArgumentBuilder()
            .Append("build.cake")
            .AppendSwitchQuoted("--output", "=", test.OutputDirectory(Name).FullPath);
        if (dryRun)
        {
            arguments.Append("--dryrun");
        }

        return RunnerProcess.Run(context, test, executable, arguments, test.SourceDirectory(Name));
    }
```

`SdkRunner` (after the `--` separator, so Cake gets it):

```csharp
    public int Run(ICakeContext context, RunnerTestContext test, bool dryRun)
    {
        var arguments = new ProcessArgumentBuilder()
            .Append("run")
            .Append("--no-cache")
            .AppendSwitchQuoted("--file", test.SourceDirectory(Name).CombineWithFilePath("cake.cs").FullPath)
            .Append("--")
            .AppendSwitchQuoted("--output", "=", test.OutputDirectory(Name).FullPath);
        if (dryRun)
        {
            arguments.Append("--dryrun");
        }

        return RunnerProcess.Run(context, test, "dotnet", arguments, test.SourceDirectory(Name));
    }
```

`FrostingRunner`:

```csharp
    public int Run(ICakeContext context, RunnerTestContext test, bool dryRun)
    {
        var arguments = new ProcessArgumentBuilder()
            .AppendQuoted(BinDirectory(test).CombineWithFilePath("Frosting.dll").FullPath)
            .AppendSwitchQuoted("--output", "=", test.OutputDirectory(Name).FullPath);
        if (dryRun)
        {
            arguments.Append("--dryrun");
        }

        return RunnerProcess.Run(context, test, "dotnet", arguments, test.SourceDirectory(Name));
    }
```

`ScriptRunner.Prepare`: render the `#addin` line too:

```csharp
            new Dictionary<string, string>
            {
                [@"^#module nuget:\?package=Cake\.Download\.Module&version=[^\r\n]*"] =
                    $"#module nuget:?package=Cake.Download.Module&version={test.ModuleVersion}&prerelease",
                [@"^#addin nuget:\?package=Cake\.Download\.Module&version=[^\r\n]*"] =
                    $"#addin nuget:?package=Cake.Download.Module&version={test.ModuleVersion}&prerelease",
            });
```

- [ ] **Step 4: Dry run first in `RunnerTestsTask`**

In `RunOne`, between `Prepare` and the first run:

```csharp
            var dryRun = runner.Run(context, test, dryRun: true);
            if (dryRun != 0)
            {
                result.Failures.Add($"the --dryrun run exited with code {dryRun}");
                return result;
            }

            result.Failures.AddRange(ReportAssertions.CheckDryRun(tools));
```

Change the two existing calls to `runner.Run(context, test, dryRun: false)`.

The Frosting `download:` directives now live in two files. In `Run`, pass both to `EnsureConsistent`:

```csharp
        ScriptTemplate.EnsureConsistent(
            File.ReadAllText(runners.CombineWithFilePath("script/build.cake").FullPath),
            File.ReadAllText(runners.CombineWithFilePath("sdk/cake.cs").FullPath),
            File.ReadAllText(runners.CombineWithFilePath("frosting/Program.cs").FullPath)
                + File.ReadAllText(runners.CombineWithFilePath("frosting/OnDemandTask.cs").FullPath));
```

In `ScriptTemplate.EnsureConsistent`, rename the Frosting label from `test/runners/frosting/Program.cs` to
`test/runners/frosting` in the tuple, so the message stays accurate. Update `test/Build.Tests/ScriptTemplateTests.cs:46`
to expect `"The download: directives in test/runners/frosting differ from test/runners/script/build.cake."`.
Also add `test/Build.Tests/ScriptTemplateTests.cs` to the Task 3 commit.

- [ ] **Step 5: Move `jq` to an on-demand task in the script and SDK runners**

`test/runners/script/build.cake`: add the `#addin` line after `#module`, and delete the `jq` `#tool` line:

```csharp
#module nuget:?package=Cake.Download.Module&version=0.0.0&prerelease
#addin nuget:?package=Cake.Download.Module&version=0.0.0&prerelease
```

`test/runners/sdk/cake.cs`: delete the `jq` `InstallTool(...)` line.

In **both** files, replace the pipeline's `Task("Default").Does(() =>` with an on-demand task followed by the existing
`Default` task, which now depends on it (the body of `Default` is unchanged):

```csharp
Task("OnDemand").Does(() =>
{
    DownloadTool("download:https://github.com/jqlang/jq/releases/download/jq-{version}/jq-{os}-{arch}{exe}?package=jq&version=1.8.2&os.darwin=macos&checksums=sha256sum.txt&checksums_sha256=dc86824a41c165ece971ff691aff6e08bbfe6e1d1f531688b47ee78c283a85cd");
});

Task("Default").IsDependentOn("OnDemand").Does(() =>
```

- [ ] **Step 6: Move `jq` to an on-demand task in the Frosting runner**

`test/runners/frosting/Program.cs`: delete the `jq` `.InstallTool(new Uri(...))` line.

`test/runners/frosting/OnDemandTask.cs`:

```csharp
using Cake.Download.Module;
using Cake.Frosting;

namespace Frosting;

[TaskName("OnDemand")]
public sealed class OnDemandTask : FrostingTask<ScenarioContext>
{
    public override void Run(ScenarioContext context)
    {
        context.DownloadTool("download:https://github.com/jqlang/jq/releases/download/jq-{version}/jq-{os}-{arch}{exe}?package=jq&version=1.8.2&os.darwin=macos&checksums=sha256sum.txt&checksums_sha256=dc86824a41c165ece971ff691aff6e08bbfe6e1d1f531688b47ee78c283a85cd");
    }
}
```

`test/runners/frosting/DefaultTask.cs`: add `[IsDependentOn(typeof(OnDemandTask))]` under `[TaskName("Default")]`.

- [ ] **Step 7: Run the build tests**

Run: `dotnet test --project test/Build.Tests/Build.Tests.csproj`
Expected: PASS.

- [ ] **Step 8: GATE — run the runner tests on both Cake versions**

Run: `./build.ps1 --target RunnerTests`
Then: `./build.ps1 --target RunnerTests --cake-version 6.0.0`
Expected: the summary shows `script passed`, `sdk passed`, `frosting passed` for both runs.

Check each item in spec §5.1 against the logs:
1. **Script:** no assembly-load error for `Cake.Download.Module` and no "unknown alias/method `DownloadTool`" compile
   error. The `#tool` lines (gh, rg, cyclonedx) still install and `jq` installs during the real run.
2. **SDK:** `DownloadTool(...)` compiles in `cake.cs`, so Cake.Generator generated the proxy.
3. **All runners:** `jq` is installed under `<run>/<runner>/src/tools/jq.1.8.2/`, next to the eager tools. The report
   assertions check that path, so a pass means `GetToolPath` matches the folder `#tool`/`InstallTool` use.

**If item 1 or 2 fails: STOP.** Don't change the package layout, don't remove `#module` or `#addin`, and don't
continue to Task 4. Report the failing runner, Cake version and the exact error text to the user, and wait for a
decision (spec §5.1). An unrelated failure (network, a GitHub outage) is not a gate failure: re-run.

- [ ] **Step 9: Commit**

```bash
git add build test/runners test/Build.Tests
git commit -m "Install jq on demand with DownloadTool in the runner tests

Every runner now calls the directive alias inside a task, and a --dryrun run
first proves the tool is not installed until the task runs.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: `DownloadToolSettings`, enums and `ToDirective()`

**Files:**
- Create: `src/Cake.Download.Module/DownloadDialect.cs`
- Create: `src/Cake.Download.Module/DownloadFormat.cs`
- Create: `src/Cake.Download.Module/DownloadToolSettings.cs`
- Test: `test/Cake.Download.Module.Tests/DownloadToolSettingsTests.cs`

**Interfaces:**
- Produces: the public types below, with exactly these member names. `internal DownloadToolSettings Clone()` (used by Task 5).

- [ ] **Step 1: Write the failing tests**

`test/Cake.Download.Module.Tests/DownloadToolSettingsTests.cs`:

```csharp
using Cake.Core;
using Cake.Core.Packaging;
using Cake.Download.Module.Directives;

namespace Cake.Download.Module.Tests;

public sealed class DownloadToolSettingsTests
{
    private static readonly string HexA = new('a', 64);
    private static readonly string HexB = new('b', 64);

    [Fact]
    public void ToDirective_Writes_The_Url_As_The_Leading_Template()
    {
        var settings = new DownloadToolSettings()
            .WithPackage("jq").WithVersion("1.8.2").WithUrl("https://example.com/jq-{os}-{arch}{exe}");

        Assert.Equal("download:https://example.com/jq-{os}-{arch}{exe}?package=jq&version=1.8.2", settings.ToDirective());
    }

    [Fact]
    public void ToDirective_Writes_Every_Parameter_In_A_Fixed_Order()
    {
        var settings = new DownloadToolSettings()
            .WithSha256("win-x64", HexB)
            .WithSha256("linux-x64", HexA)
            .WithExclude("**/doc/**")
            .WithInclude("**/rg{exe}")
            .WithFileName("rg")
            .WithFormat(DownloadFormat.TarGz)
            .WithTriple("linux-x64", "x86_64-unknown-linux-gnu")
            .WithArchive("darwin", "zip")
            .WithArch("x86_64", "x64")
            .WithOs("darwin", "macos")
            .WithDialect(DownloadDialect.Rust)
            .WithUrl("win-x64", "https://e.com/win.zip")
            .WithUrl("https://e.com/rg-{triple}.{archive}")
            .WithVersion("14.1.1")
            .WithPackage("rg");

        Assert.Equal(
            "download:https://e.com/rg-{triple}.{archive}?package=rg&version=14.1.1&url.win-x64=https%3A%2F%2Fe.com%2Fwin.zip" +
            "&dialect=rust&os.darwin=macos&arch.x86_64=x64&archive.darwin=zip&triple.linux-x64=x86_64-unknown-linux-gnu" +
            "&format=tar.gz&filename=rg&include=%2A%2A%2Frg%7Bexe%7D&exclude=%2A%2A%2Fdoc%2F%2A%2A" +
            $"&sha256.linux-x64={HexA}&sha256.win-x64={HexB}",
            settings.ToDirective());
    }

    [Fact]
    public void ToDirective_Round_Trips_Through_The_Parser()
    {
        var settings = new DownloadToolSettings()
            .WithPackage("rg").WithVersion("14.1.1").WithUrl("https://e.com/rg-{triple}.{archive}")
            .WithUrl("win-x64", "https://e.com/win.zip").WithDialect(DownloadDialect.Rust)
            .WithOs("darwin", "macos").WithArch("x86_64", "x64").WithArchive("darwin", "zip")
            .WithTriple("linux-x64", "x86_64-unknown-linux-gnu").WithFormat(DownloadFormat.TarGz)
            .WithInclude("**/rg{exe}").WithInclude("**/rg-extra").WithExclude("**/doc/**")
            .WithSha256("linux-x64", HexA);

        var directive = Parse(settings);

        Assert.Equal("rg", directive.Package);
        Assert.Equal("14.1.1", directive.Version);
        Assert.Equal("https://e.com/rg-{triple}.{archive}", directive.UrlTemplate);
        Assert.Null(directive.Url);
        Assert.Equal("https://e.com/win.zip", directive.UrlByRid["win-x64"]);
        Assert.Equal("rust", directive.Dialect);
        Assert.Equal("macos", directive.OsOverrides["darwin"]);
        Assert.Equal("x64", directive.ArchOverrides["x86_64"]);
        Assert.Equal("zip", directive.ArchiveOverrides["darwin"]);
        Assert.Equal("x86_64-unknown-linux-gnu", directive.TripleOverrides["linux-x64"]);
        Assert.Equal("tar.gz", directive.Format);
        Assert.Equal(["**/rg{exe}", "**/rg-extra"], directive.Include);
        Assert.Equal(["**/doc/**"], directive.Exclude);
        Assert.Equal(HexA, Assert.IsType<PerRidSha256Integrity>(directive.Integrity).Sha256ByRid["linux-x64"]);
    }

    [Theory]
    [InlineData("https://example.com/dl?file=jq-{os}")]
    [InlineData("https://example.com/jq#{os}")]
    [InlineData("https://example.com/a&b-{os}")]
    public void ToDirective_Moves_A_Url_With_Query_Characters_To_The_Url_Parameter(string url)
    {
        var settings = new DownloadToolSettings().WithPackage("jq").WithVersion("1.8.2").WithUrl(url).WithSha256(HexA);

        var directive = Parse(settings);

        Assert.StartsWith("download:?package=jq&version=1.8.2&url=", settings.ToDirective(), StringComparison.Ordinal);
        Assert.Null(directive.UrlTemplate);
        Assert.Equal(url, directive.Url);
    }

    [Theory]
    [InlineData("https://example.com/my%20tool-{version}.zip")]
    [InlineData("https://example.com/dl?name=my%20tool")]
    public void ToDirective_Keeps_Percent_Sequences_In_The_Url(string url)
    {
        var directive = Parse(new DownloadToolSettings().WithPackage("jq").WithVersion("1.8.2").WithUrl(url).WithSha256(HexA));

        Assert.Equal(url, directive.UrlTemplate ?? directive.Url);
    }

    [Fact]
    public void ToDirective_Encodes_Special_Characters_In_Values()
    {
        var directive = Parse(new DownloadToolSettings()
            .WithPackage("jq").WithVersion("1.8.2").WithUrl("https://example.com/jq").WithSha256(HexA)
            .WithInclude("a&b=c?d#e"));

        Assert.Equal(["a&b=c?d#e"], directive.Include);
    }

    [Fact]
    public void ToDirective_Matches_Mixed_Case_Rid_Keys()
    {
        var directive = Parse(new DownloadToolSettings()
            .WithPackage("jq").WithVersion("1.8.2").WithUrl("https://example.com/jq-{rid}")
            .WithSha256("Linux-X64", HexA));

        Assert.Equal(HexA, Assert.IsType<PerRidSha256Integrity>(directive.Integrity).Sha256ByRid["linux-x64"]);
    }

    [Theory]
    [InlineData(DownloadDialect.Go, "dialect=go")]
    [InlineData(DownloadDialect.DotNet, "dialect=dotnet")]
    [InlineData(DownloadDialect.Rust, "dialect=rust")]
    public void ToDirective_Writes_Dialect_Names(DownloadDialect dialect, string expected)
    {
        Assert.Contains("&" + expected, Minimal().WithDialect(dialect).ToDirective(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DownloadFormat.File, "format=file")]
    [InlineData(DownloadFormat.Zip, "format=zip")]
    [InlineData(DownloadFormat.Tar, "format=tar")]
    [InlineData(DownloadFormat.TarGz, "format=tar.gz")]
    public void ToDirective_Writes_Format_Names(DownloadFormat format, string expected)
    {
        Assert.Contains("&" + expected, Minimal().WithFormat(format).ToDirective(), StringComparison.Ordinal);
    }

    [Fact]
    public void ToDirective_Writes_Each_Integrity_Mode()
    {
        Assert.IsType<Sha256Integrity>(Parse(Minimal().WithSha256(HexA)).Integrity);
        Assert.IsType<SkipIntegrity>(Parse(Minimal().WithoutVerification()).Integrity);
        var checksums = Assert.IsType<ChecksumsFileIntegrity>(Parse(Minimal().WithChecksums("sha256sum.txt", HexA)).Integrity);
        Assert.Equal("sha256sum.txt", checksums.Reference);
        Assert.Equal(HexA, checksums.Sha256);
        Assert.IsType<MissingIntegrity>(Parse(Minimal()).Integrity);
    }

    [Fact]
    public void ToDirective_Lets_The_Parser_Reject_Conflicting_Integrity_Options()
    {
        var exception = Assert.Throws<CakeException>(() => Parse(Minimal().WithSha256(HexA).WithSha256("linux-x64", HexB)));

        Assert.Contains("specify only one integrity option", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToDirective_Rejects_Skip_Together_With_A_Hash()
    {
        var exception = Assert.Throws<CakeException>(() => Minimal().WithSha256(HexA).WithoutVerification().ToDirective());

        Assert.Equal(
            "DownloadToolSettings for 'jq': specify only one integrity option: WithSha256(…), WithSha256(rid, …), WithChecksums(…) or WithoutVerification().",
            exception.Message);
    }

    [Fact]
    public void ToDirective_Allows_Per_Platform_Urls_Only()
    {
        var directive = Parse(new DownloadToolSettings()
            .WithPackage("jq").WithVersion("1.8.2").WithUrl("linux-x64", "https://example.com/jq").WithSha256(HexA));

        Assert.Null(directive.UrlTemplate);
        Assert.Null(directive.Url);
        Assert.Equal("https://example.com/jq", directive.UrlByRid["linux-x64"]);
    }

    [Theory]
    [InlineData(null, "1.8.2", "https://example.com/jq", "DownloadToolSettings needs a package: use WithPackage(…) or DownloadTool(package, version, url, settings).")]
    [InlineData("", "1.8.2", "https://example.com/jq", "DownloadToolSettings needs a package: use WithPackage(…) or DownloadTool(package, version, url, settings).")]
    [InlineData("jq", null, "https://example.com/jq", "DownloadToolSettings for 'jq' needs a version: use WithVersion(…) or DownloadTool(package, version, url, settings).")]
    [InlineData("jq", "", "https://example.com/jq", "DownloadToolSettings for 'jq' needs a version: use WithVersion(…) or DownloadTool(package, version, url, settings).")]
    [InlineData("jq", "1.8.2", null, "DownloadToolSettings for 'jq' needs a URL: use WithUrl(…), WithUrl(rid, …) or DownloadTool(package, version, url, settings).")]
    [InlineData("jq", "1.8.2", "", "DownloadToolSettings for 'jq' needs a URL: use WithUrl(…), WithUrl(rid, …) or DownloadTool(package, version, url, settings).")]
    public void ToDirective_Rejects_A_Missing_Identity(string? package, string? version, string? url, string message)
    {
        var settings = new DownloadToolSettings { Package = package, Version = version, Url = url };

        Assert.Equal(message, Assert.Throws<CakeException>(() => settings.ToDirective()).Message);
    }

    [Fact]
    public void ToDirectiveUri_Keeps_The_Directive_As_Its_Original_String()
    {
        var settings = Minimal().WithSha256(HexA);

        Assert.Equal(settings.ToDirective(), settings.ToDirectiveUri().OriginalString);
    }

    [Fact]
    public void Fluent_Methods_Return_The_Same_Instance_And_Append_Globs()
    {
        var settings = new DownloadToolSettings();

        Assert.Same(settings, settings.WithInclude("a").WithInclude("b").WithExclude("c"));
        Assert.Equal(["a", "b"], settings.Include);
        Assert.Equal(["c"], settings.Exclude);
    }

    [Fact]
    public void Platform_Keys_Are_Case_Insensitive()
    {
        var settings = new DownloadToolSettings().WithSha256("LINUX-X64", HexA).WithSha256("linux-x64", HexB);

        Assert.Equal(HexB, Assert.Single(settings.Sha256ByPlatform).Value);
    }

    [Fact]
    public void Clone_Copies_Every_Property_Independently()
    {
        var original = Minimal().WithSha256("linux-x64", HexA).WithInclude("a").WithDialect(DownloadDialect.DotNet);

        var clone = original.Clone();
        Assert.Equal(original.ToDirective(), clone.ToDirective());

        clone.WithInclude("b").WithSha256("win-x64", HexB);

        Assert.Equal(["a"], original.Include);
        Assert.Single(original.Sha256ByPlatform);
        Assert.Equal(["a", "b"], clone.Include);
        Assert.Equal(DownloadDialect.DotNet, clone.Dialect);
        Assert.Equal("jq", clone.Package);
    }

    private static DownloadToolSettings Minimal() =>
        new DownloadToolSettings().WithPackage("jq").WithVersion("1.8.2").WithUrl("https://example.com/jq-{os}");

    private static DownloadDirective Parse(DownloadToolSettings settings) =>
        DirectiveParser.Parse(new PackageReference(settings.ToDirective()), DirectiveSource.Settings);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project test/Cake.Download.Module.Tests/Cake.Download.Module.Tests.csproj --filter-class "*DownloadToolSettingsTests"`
Expected: build FAILS (`DownloadToolSettings`, `DownloadDialect`, `DownloadFormat` not found).

- [ ] **Step 3: Add the enums**

`src/Cake.Download.Module/DownloadDialect.cs`:

```csharp
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
```

`src/Cake.Download.Module/DownloadFormat.cs`:

```csharp
namespace Cake.Download.Module;

/// <summary>
/// The format of the downloaded file (the directive's <c>format</c> parameter). Detected from the URL when not set.
/// </summary>
public enum DownloadFormat
{
    /// <summary>A raw file, installed as is.</summary>
    File,

    /// <summary>A zip archive.</summary>
    Zip,

    /// <summary>An uncompressed tar archive.</summary>
    Tar,

    /// <summary>A gzip-compressed tar archive (<c>.tar.gz</c> or <c>.tgz</c>).</summary>
    TarGz,
}
```

- [ ] **Step 4: Add `DownloadToolSettings`**

`src/Cake.Download.Module/DownloadToolSettings.cs`. Every public member needs an XML `<summary>` (the project
generates documentation, and analyzers require it). Name each fluent method's directive parameter in its summary, as
shown on the first few:

```csharp
using System.Text;
using Cake.Core;

namespace Cake.Download.Module;

/// <summary>
/// A typed description of a <c>download:</c> tool for the <c>DownloadTool</c> alias. Each property maps to one directive
/// parameter, and unset properties keep the directive's defaults. Nothing is validated until the tool is installed.
/// </summary>
public sealed class DownloadToolSettings
{
    /// <summary>Gets or sets the tool name (<c>package</c>): install folder, default file name and default include.</summary>
    public string? Package { get; set; }

    /// <summary>Gets or sets the exact version (<c>version</c>); fills <c>{version}</c>.</summary>
    public string? Version { get; set; }

    /// <summary>Gets or sets the URL template used on every platform without a <see cref="UrlByPlatform"/> entry.</summary>
    public string? Url { get; set; }

    /// <summary>Gets URL templates per .NET RID (<c>url.&lt;rid&gt;</c>).</summary>
    public IDictionary<string, string> UrlByPlatform { get; } = NewMap();

    /// <summary>Gets or sets the placeholder dialect (<c>dialect</c>); <see cref="DownloadDialect.Go"/> when not set.</summary>
    public DownloadDialect? Dialect { get; set; }

    /// <summary>Gets <c>{os}</c> overrides, keyed by the dialect's default value (<c>os.&lt;value&gt;</c>).</summary>
    public IDictionary<string, string> OsOverrides { get; } = NewMap();

    /// <summary>Gets <c>{arch}</c> overrides, keyed by the dialect's default value (<c>arch.&lt;value&gt;</c>).</summary>
    public IDictionary<string, string> ArchOverrides { get; } = NewMap();

    /// <summary>Gets <c>{archive}</c> overrides, keyed by the dialect's default <c>{os}</c> value (<c>archive.&lt;os&gt;</c>).</summary>
    public IDictionary<string, string> ArchiveOverrides { get; } = NewMap();

    /// <summary>Gets <c>{triple}</c> overrides per .NET RID (<c>triple.&lt;rid&gt;</c>, rust dialect only).</summary>
    public IDictionary<string, string> TripleOverrides { get; } = NewMap();

    /// <summary>Gets or sets the download format (<c>format</c>); detected from the URL when not set.</summary>
    public DownloadFormat? Format { get; set; }

    /// <summary>Gets or sets the file name for a raw download (<c>filename</c>).</summary>
    public string? FileName { get; set; }

    /// <summary>Gets the globs choosing the files to register (<c>include</c>).</summary>
    public IList<string> Include { get; } = new List<string>();

    /// <summary>Gets the globs removed from the <see cref="Include"/> result (<c>exclude</c>).</summary>
    public IList<string> Exclude { get; } = new List<string>();

    /// <summary>Gets or sets the SHA-256 of the download on every platform (<c>sha256</c>).</summary>
    public string? Sha256 { get; set; }

    /// <summary>Gets the SHA-256 of the download per .NET RID (<c>sha256.&lt;rid&gt;</c>).</summary>
    public IDictionary<string, string> Sha256ByPlatform { get; } = NewMap();

    /// <summary>Gets or sets the checksums file, relative to the download URL or absolute (<c>checksums</c>).</summary>
    public string? ChecksumsFile { get; set; }

    /// <summary>Gets or sets the SHA-256 of the checksums file itself (<c>checksums_sha256</c>).</summary>
    public string? ChecksumsFileSha256 { get; set; }

    /// <summary>Gets or sets a value indicating whether to install without verification (<c>sha256=skip</c>). Not recommended.</summary>
    public bool SkipVerification { get; set; }

    /// <summary>Sets <see cref="Package"/>.</summary>
    /// <param name="package">The tool name.</param>
    /// <returns>This instance.</returns>
    public DownloadToolSettings WithPackage(string package) => Set(() => Package = package, package);

    /// <summary>Sets <see cref="Version"/>.</summary>
    /// <param name="version">The exact version.</param>
    /// <returns>This instance.</returns>
    public DownloadToolSettings WithVersion(string version) => Set(() => Version = version, version);

    /// <summary>Sets <see cref="Url"/>.</summary>
    /// <param name="template">The URL template.</param>
    /// <returns>This instance.</returns>
    public DownloadToolSettings WithUrl(string template) => Set(() => Url = template, template);

    /// <summary>Sets the URL template for one platform (<c>url.&lt;rid&gt;</c>).</summary>
    /// <param name="rid">The .NET RID, e.g. <c>linux-x64</c>.</param>
    /// <param name="template">The URL template.</param>
    /// <returns>This instance.</returns>
    public DownloadToolSettings WithUrl(string rid, string template) => Put(UrlByPlatform, rid, template);

    /// <summary>Sets <see cref="Dialect"/>.</summary>
    /// <param name="dialect">The dialect.</param>
    /// <returns>This instance.</returns>
    public DownloadToolSettings WithDialect(DownloadDialect dialect)
    {
        Dialect = dialect;
        return this;
    }

    /// <summary>Overrides one <c>{os}</c> value (<c>os.&lt;value&gt;</c>), e.g. <c>WithOs("darwin", "macOS")</c>.</summary>
    /// <param name="defaultValue">The dialect's default value.</param>
    /// <param name="value">The value to use instead.</param>
    /// <returns>This instance.</returns>
    public DownloadToolSettings WithOs(string defaultValue, string value) => Put(OsOverrides, defaultValue, value);

    /// <summary>Overrides one <c>{arch}</c> value (<c>arch.&lt;value&gt;</c>), e.g. <c>WithArch("amd64", "x86_64")</c>.</summary>
    /// <param name="defaultValue">The dialect's default value.</param>
    /// <param name="value">The value to use instead.</param>
    /// <returns>This instance.</returns>
    public DownloadToolSettings WithArch(string defaultValue, string value) => Put(ArchOverrides, defaultValue, value);

    /// <summary>Overrides <c>{archive}</c> for one OS (<c>archive.&lt;os&gt;</c>), e.g. <c>WithArchive("darwin", "zip")</c>.</summary>
    /// <param name="os">The dialect's default <c>{os}</c> value.</param>
    /// <param name="extension">The archive extension.</param>
    /// <returns>This instance.</returns>
    public DownloadToolSettings WithArchive(string os, string extension) => Put(ArchiveOverrides, os, extension);

    /// <summary>Overrides <c>{triple}</c> for one platform (<c>triple.&lt;rid&gt;</c>, rust dialect only).</summary>
    /// <param name="rid">The .NET RID.</param>
    /// <param name="triple">The Rust target triple.</param>
    /// <returns>This instance.</returns>
    public DownloadToolSettings WithTriple(string rid, string triple) => Put(TripleOverrides, rid, triple);

    /// <summary>Sets <see cref="Format"/>.</summary>
    /// <param name="format">The format.</param>
    /// <returns>This instance.</returns>
    public DownloadToolSettings WithFormat(DownloadFormat format)
    {
        Format = format;
        return this;
    }

    /// <summary>Sets <see cref="FileName"/>.</summary>
    /// <param name="fileName">The file name for a raw download.</param>
    /// <returns>This instance.</returns>
    public DownloadToolSettings WithFileName(string fileName) => Set(() => FileName = fileName, fileName);

    /// <summary>Adds an <c>include</c> glob.</summary>
    /// <param name="glob">The glob, relative to the install folder.</param>
    /// <returns>This instance.</returns>
    public DownloadToolSettings WithInclude(string glob) => Set(() => Include.Add(glob), glob);

    /// <summary>Adds an <c>exclude</c> glob.</summary>
    /// <param name="glob">The glob, relative to the install folder.</param>
    /// <returns>This instance.</returns>
    public DownloadToolSettings WithExclude(string glob) => Set(() => Exclude.Add(glob), glob);

    /// <summary>Pins the download's SHA-256 on every platform (<c>sha256</c>).</summary>
    /// <param name="sha256">64 hexadecimal characters.</param>
    /// <returns>This instance.</returns>
    public DownloadToolSettings WithSha256(string sha256) => Set(() => Sha256 = sha256, sha256);

    /// <summary>Pins the download's SHA-256 for one platform (<c>sha256.&lt;rid&gt;</c>).</summary>
    /// <param name="rid">The .NET RID.</param>
    /// <param name="sha256">64 hexadecimal characters.</param>
    /// <returns>This instance.</returns>
    public DownloadToolSettings WithSha256(string rid, string sha256) => Put(Sha256ByPlatform, rid, sha256);

    /// <summary>Verifies the download against a pinned checksums file (<c>checksums</c> and <c>checksums_sha256</c>).</summary>
    /// <param name="file">The checksums file, relative to the download URL or absolute.</param>
    /// <param name="sha256">The SHA-256 of the checksums file itself.</param>
    /// <returns>This instance.</returns>
    public DownloadToolSettings WithChecksums(string file, string sha256)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(sha256);
        ChecksumsFile = file;
        ChecksumsFileSha256 = sha256;
        return this;
    }

    /// <summary>Installs without verification (<c>sha256=skip</c>). A warning is logged on every fresh install. Not recommended.</summary>
    /// <returns>This instance.</returns>
    public DownloadToolSettings WithoutVerification()
    {
        SkipVerification = true;
        return this;
    }

    /// <summary>Writes these settings as a <c>download:</c> directive, for <c>#tool</c>-style eager installs such as Cake.Sdk's <c>InstallTool</c>.</summary>
    /// <returns>The directive.</returns>
    /// <exception cref="CakeException"><see cref="Package"/> or <see cref="Version"/> is missing, there is no URL, or <see cref="SkipVerification"/> is combined with <see cref="Sha256"/>.</exception>
    public string ToDirective()
    {
        const string Overloads = "DownloadTool(package, version, url, settings)";
        if (string.IsNullOrEmpty(Package))
        {
            throw new CakeException($"DownloadToolSettings needs a package: use WithPackage(…) or {Overloads}.");
        }

        if (string.IsNullOrEmpty(Version))
        {
            throw new CakeException($"DownloadToolSettings for '{Package}' needs a version: use WithVersion(…) or {Overloads}.");
        }

        if (string.IsNullOrEmpty(Url) && UrlByPlatform.Count == 0)
        {
            throw new CakeException($"DownloadToolSettings for '{Package}' needs a URL: use WithUrl(…), WithUrl(rid, …) or {Overloads}.");
        }

        if (SkipVerification && Sha256 is not null)
        {
            throw new CakeException(
                $"DownloadToolSettings for '{Package}': specify only one integrity option: WithSha256(…), WithSha256(rid, …), WithChecksums(…) or WithoutVerification().");
        }

        var leading = !string.IsNullOrEmpty(Url) && Url.IndexOfAny(['?', '#', '&']) < 0 ? Url : null;
        var builder = new StringBuilder("download:").Append(leading);
        var separator = '?';

        void Add(string key, string value)
        {
            builder.Append(separator).Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
            separator = '&';
        }

        void AddMap(string prefix, IDictionary<string, string> map)
        {
            foreach (var (key, value) in map.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                Add(prefix + key, value);
            }
        }

        Add("package", Package);
        Add("version", Version);
        if (!string.IsNullOrEmpty(Url) && leading is null)
        {
            Add("url", Url);
        }

        AddMap("url.", UrlByPlatform);
        if (Dialect is { } dialect)
        {
            Add("dialect", DialectName(dialect));
        }

        AddMap("os.", OsOverrides);
        AddMap("arch.", ArchOverrides);
        AddMap("archive.", ArchiveOverrides);
        AddMap("triple.", TripleOverrides);
        if (Format is { } format)
        {
            Add("format", FormatName(format));
        }

        if (FileName is not null)
        {
            Add("filename", FileName);
        }

        foreach (var glob in Include)
        {
            Add("include", glob);
        }

        foreach (var glob in Exclude)
        {
            Add("exclude", glob);
        }

        if (SkipVerification)
        {
            Add("sha256", "skip");
        }

        if (Sha256 is not null)
        {
            Add("sha256", Sha256);
        }

        AddMap("sha256.", Sha256ByPlatform);
        if (ChecksumsFile is not null)
        {
            Add("checksums", ChecksumsFile);
        }

        if (ChecksumsFileSha256 is not null)
        {
            Add("checksums_sha256", ChecksumsFileSha256);
        }

        return builder.ToString();
    }

    /// <summary>Writes these settings as a <c>download:</c> directive <see cref="Uri"/>, for Frosting's <c>CakeHost.InstallTool</c>.</summary>
    /// <returns>The directive; its <see cref="Uri.OriginalString"/> equals <see cref="ToDirective"/>.</returns>
    /// <exception cref="CakeException">See <see cref="ToDirective"/>.</exception>
    public Uri ToDirectiveUri() => new(ToDirective());

    internal DownloadToolSettings Clone()
    {
        var clone = new DownloadToolSettings
        {
            Package = Package,
            Version = Version,
            Url = Url,
            Dialect = Dialect,
            Format = Format,
            FileName = FileName,
            Sha256 = Sha256,
            ChecksumsFile = ChecksumsFile,
            ChecksumsFileSha256 = ChecksumsFileSha256,
            SkipVerification = SkipVerification,
        };
        Copy(UrlByPlatform, clone.UrlByPlatform);
        Copy(OsOverrides, clone.OsOverrides);
        Copy(ArchOverrides, clone.ArchOverrides);
        Copy(ArchiveOverrides, clone.ArchiveOverrides);
        Copy(TripleOverrides, clone.TripleOverrides);
        Copy(Sha256ByPlatform, clone.Sha256ByPlatform);
        foreach (var glob in Include)
        {
            clone.Include.Add(glob);
        }

        foreach (var glob in Exclude)
        {
            clone.Exclude.Add(glob);
        }

        return clone;
    }

    private static Dictionary<string, string> NewMap() => new(StringComparer.OrdinalIgnoreCase);

    private static void Copy(IDictionary<string, string> from, IDictionary<string, string> to)
    {
        foreach (var (key, value) in from)
        {
            to[key] = value;
        }
    }

    private static string DialectName(DownloadDialect dialect) => dialect switch
    {
        DownloadDialect.Go => "go",
        DownloadDialect.DotNet => "dotnet",
        DownloadDialect.Rust => "rust",
        _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, "Unknown dialect."),
    };

    private static string FormatName(DownloadFormat format) => format switch
    {
        DownloadFormat.File => "file",
        DownloadFormat.Zip => "zip",
        DownloadFormat.Tar => "tar",
        DownloadFormat.TarGz => "tar.gz",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown format."),
    };

    private DownloadToolSettings Set(Action set, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        set();
        return this;
    }

    private DownloadToolSettings Put(IDictionary<string, string> map, string key, string value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        map[key] = value;
        return this;
    }
}
```

The `ArgumentNullException.ThrowIfNull(value)` in `Set` reports the parameter as `value`. If an analyzer requires
the caller's parameter name, call `ArgumentNullException.ThrowIfNull` directly in each method instead.

- [ ] **Step 5: Run the settings tests**

Run: `dotnet test --project test/Cake.Download.Module.Tests/Cake.Download.Module.Tests.csproj --filter-class "*DownloadToolSettingsTests"`
Expected: PASS.

If `ToDirective_Writes_Every_Parameter_In_A_Fixed_Order` fails only on how a character is escaped, check
`Uri.EscapeDataString`'s output for that character on .NET 8 and fix the expected string, not the encoder. `*`, `{`,
`}`, `/` and `:` must be percent-encoded.

- [ ] **Step 6: Run all unit tests and commit**

Run: `./build.ps1 --target Test`
Expected: PASS.

```bash
git add src/Cake.Download.Module test/Cake.Download.Module.Tests
git commit -m "Add DownloadToolSettings with ToDirective and ToDirectiveUri

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

The CHANGELOG entry comes with the settings alias in Task 5, in the same entry.

---

### Task 5: Settings-family alias

**Files:**
- Modify: `src/Cake.Download.Module/DownloadToolSettings.cs` (add `WithIdentity`)
- Modify: `src/Cake.Download.Module/DownloadToolRunner.cs`
- Modify: `src/Cake.Download.Module/DownloadToolAliases.cs`
- Test: `test/Cake.Download.Module.Tests/DownloadToolSettingsTests.cs`, `test/Cake.Download.Module.Tests/DownloadToolAliasesTests.cs`
- Modify: `CHANGELOG.md`

**Interfaces:**
- Consumes: `DownloadToolSettings.ToDirective()`, `Clone()` (Task 4); `DownloadToolRunner.Install(PackageReference, DirectiveSource)` (Task 2).
- Produces:
  - `public static IReadOnlyCollection<FilePath> DownloadToolAliases.DownloadTool(this ICakeContext context, DownloadToolSettings settings)`
  - `public static IReadOnlyCollection<FilePath> DownloadToolAliases.DownloadTool(this ICakeContext context, string package, string version, string url, DownloadToolSettings settings)`
  - `DownloadToolRunner.Install(DownloadToolSettings settings)` and `Install(string package, string version, string url, DownloadToolSettings settings)`
  - `internal DownloadToolSettings DownloadToolSettings.WithIdentity(string package, string version, string url)`

- [ ] **Step 1: Write the failing `WithIdentity` tests**

Add to `DownloadToolSettingsTests`:

```csharp
    [Fact]
    public void WithIdentity_Fills_A_Copy_And_Leaves_The_Settings_Unchanged()
    {
        var settings = new DownloadToolSettings().WithSha256(HexA);

        var filled = settings.WithIdentity("jq", "1.8.2", "https://example.com/jq");

        Assert.NotSame(settings, filled);
        Assert.Equal("download:https://example.com/jq?package=jq&version=1.8.2&sha256=" + HexA, filled.ToDirective());
        Assert.Null(settings.Package);
        Assert.Null(settings.Version);
        Assert.Null(settings.Url);
    }

    [Fact]
    public void WithIdentity_Accepts_Equal_Values()
    {
        var settings = Minimal();

        Assert.Equal(settings.ToDirective(), settings.WithIdentity("jq", "1.8.2", "https://example.com/jq-{os}").ToDirective());
    }

    [Theory]
    [InlineData("rg", "1.8.2", "https://example.com/jq-{os}", "The package argument 'rg' conflicts with DownloadToolSettings.Package 'jq'. Set it in one place.")]
    [InlineData("jq", "1.9.0", "https://example.com/jq-{os}", "The version argument '1.9.0' conflicts with DownloadToolSettings.Version '1.8.2'. Set it in one place.")]
    [InlineData("jq", "1.8.2", "https://example.com/other", "The url argument 'https://example.com/other' conflicts with DownloadToolSettings.Url 'https://example.com/jq-{os}'. Set it in one place.")]
    public void WithIdentity_Rejects_Conflicting_Values(string package, string version, string url, string message)
    {
        var exception = Assert.Throws<CakeException>(() => Minimal().WithIdentity(package, version, url));

        Assert.Equal(message, exception.Message);
    }
```

Run: `dotnet test --project test/Cake.Download.Module.Tests/Cake.Download.Module.Tests.csproj --filter-class "*DownloadToolSettingsTests"`
Expected: build FAILS (`WithIdentity` not found).

- [ ] **Step 2: Implement `WithIdentity`**

In `DownloadToolSettings`, after `Clone()`:

```csharp
    internal DownloadToolSettings WithIdentity(string package, string version, string url)
    {
        var filled = Clone();
        filled.Package = Merge("package", package, nameof(Package), Package);
        filled.Version = Merge("version", version, nameof(Version), Version);
        filled.Url = Merge("url", url, nameof(Url), Url);
        return filled;
    }

    private static string Merge(string argument, string value, string property, string? current) =>
        current is null || string.Equals(current, value, StringComparison.Ordinal)
            ? value
            : throw new CakeException($"The {argument} argument '{value}' conflicts with DownloadToolSettings.{property} '{current}'. Set it in one place.");
```

Run the same command. Expected: PASS.

- [ ] **Step 3: Write the failing alias tests**

Add to `DownloadToolAliasesTests`:

```csharp
    private static DownloadToolSettings JqSettings() =>
        new DownloadToolSettings().WithSha256("linux-x64", TestHashes.Sha256(RawContent));

    [Fact]
    public void Install_With_Identity_Arguments_Registers_And_Returns_The_Files()
    {
        var path = Assert.Single(CreateRunner().Install("jq", "1.8.2", "https://example.com/jq-{os}-{arch}", JqSettings()));

        Assert.EndsWith("/tools/jq.1.8.2/jq", path.FullPath, StringComparison.Ordinal);
        Assert.Equal([path.FullPath], _context.Tools.Registered.Select(registered => registered.FullPath));
    }

    [Fact]
    public void Install_With_Settings_Only_Uses_The_Identity_From_The_Settings()
    {
        var settings = JqSettings().WithPackage("jq").WithVersion("1.8.2").WithUrl("https://example.com/jq-{os}-{arch}");

        var path = Assert.Single(CreateRunner().Install(settings));

        Assert.EndsWith("/tools/jq.1.8.2/jq", path.FullPath, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_And_The_Equivalent_Directive_Share_One_Install()
    {
        CreateRunner().Install("jq", "1.8.2", "https://example.com/jq-{os}-{arch}", JqSettings());
        var requests = _handler.RequestedUrls.Count;

        CreateRunner().Install(JqDirective);

        Assert.Equal(requests, _handler.RequestedUrls.Count);
    }

    [Fact]
    public void One_Settings_Instance_Can_Be_Reused_For_Several_Tools()
    {
        _handler.Respond("https://example.com/yq-linux-amd64", FakeHttpHandler.Ok(RawContent));
        var settings = new DownloadToolSettings().WithSha256(TestHashes.Sha256(RawContent));

        CreateRunner().Install("jq", "1.8.2", "https://example.com/jq-linux-amd64", settings);
        var yq = Assert.Single(CreateRunner().Install("yq", "4.0.0", "https://example.com/yq-linux-amd64", settings));

        Assert.EndsWith("/tools/yq.4.0.0/yq", yq.FullPath, StringComparison.Ordinal);
        Assert.Null(settings.Package);
    }

    [Fact]
    public void Install_With_Settings_Without_Integrity_Suggests_A_Settings_Call()
    {
        var exception = Assert.Throws<CakeException>(
            () => CreateRunner().Install("jq", "1.8.2", "https://example.com/jq-{os}-{arch}", new DownloadToolSettings()));

        Assert.Contains($"Add .WithSha256(\"linux-x64\", \"{TestHashes.Sha256(RawContent)}\") to the DownloadToolSettings", exception.Message);
        Assert.Empty(_context.Tools.Registered);
    }

    [Fact]
    public void DownloadTool_With_Settings_Rejects_Null_Arguments()
    {
        var settings = new DownloadToolSettings();
        Assert.Throws<ArgumentNullException>(() => _context.DownloadTool((DownloadToolSettings)null!));
        Assert.Throws<ArgumentNullException>(() => DownloadToolAliases.DownloadTool(null!, settings));
        Assert.Throws<ArgumentNullException>(() => _context.DownloadTool(null!, "1.8.2", "https://example.com/jq", settings));
        Assert.Throws<ArgumentNullException>(() => _context.DownloadTool("jq", null!, "https://example.com/jq", settings));
        Assert.Throws<ArgumentNullException>(() => _context.DownloadTool("jq", "1.8.2", null!, settings));
        Assert.Throws<ArgumentNullException>(() => _context.DownloadTool("jq", "1.8.2", "https://example.com/jq", null!));
    }

    [Fact]
    public void DownloadTool_With_Incomplete_Settings_Fails_Before_Any_Request()
    {
        var exception = Assert.Throws<CakeException>(() => _context.DownloadTool(new DownloadToolSettings().WithPackage("jq")));

        Assert.StartsWith("DownloadToolSettings for 'jq' needs a version", exception.Message, StringComparison.Ordinal);
        Assert.Empty(_handler.RequestedUrls);
    }
```

Run: `dotnet test --project test/Cake.Download.Module.Tests/Cake.Download.Module.Tests.csproj --filter-class "*DownloadToolAliasesTests"`
Expected: build FAILS (no settings overloads).

- [ ] **Step 4: Implement the runner methods**

In `DownloadToolRunner`, add:

```csharp
    public IReadOnlyCollection<FilePath> Install(DownloadToolSettings settings) =>
        Install(new PackageReference(settings.ToDirective()), DirectiveSource.Settings);

    public IReadOnlyCollection<FilePath> Install(string package, string version, string url, DownloadToolSettings settings) =>
        Install(settings.WithIdentity(package, version, url));
```

- [ ] **Step 5: Implement the alias overloads**

In `DownloadToolAliases`, add:

```csharp
    /// <summary>
    /// Installs the tool described by <paramref name="settings"/>, which must set the package, version and URL, unless
    /// an identical install is already there, and registers its files with <c>context.Tools</c>.
    /// </summary>
    /// <param name="context">The Cake context.</param>
    /// <param name="settings">The settings, including <c>WithPackage</c>, <c>WithVersion</c> and <c>WithUrl</c>.</param>
    /// <returns>The registered files.</returns>
    /// <exception cref="CakeException">The settings are incomplete or invalid, or downloading, verifying or extracting failed.</exception>
    /// <example>
    /// <code>
    /// DownloadTool(new DownloadToolSettings()
    ///     .WithPackage("jq")
    ///     .WithVersion("1.8.2")
    ///     .WithUrl("https://github.com/jqlang/jq/releases/download/jq-{version}/jq-{os}-{arch}{exe}")
    ///     .WithOs("darwin", "macos")
    ///     .WithChecksums("sha256sum.txt", "dc86824a41c165ece971ff691aff6e08bbfe6e1d1f531688b47ee78c283a85cd"));
    /// </code>
    /// </example>
    [CakeMethodAlias]
    public static IReadOnlyCollection<FilePath> DownloadTool(this ICakeContext context, DownloadToolSettings settings)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(settings);

        return CreateRunner(context).Install(settings);
    }

    /// <summary>
    /// Installs <paramref name="package"/> <paramref name="version"/> from <paramref name="url"/>, with
    /// <paramref name="settings"/> for integrity and everything else, and registers its files with <c>context.Tools</c>.
    /// <paramref name="settings"/> is not modified; if it also sets the package, version or URL to a different value,
    /// the call fails.
    /// </summary>
    /// <param name="context">The Cake context.</param>
    /// <param name="package">The tool name: install folder, default file name and default include.</param>
    /// <param name="version">The exact version; fills <c>{version}</c>.</param>
    /// <param name="url">The URL template, e.g. <c>https://example.com/tool-{version}-{os}-{arch}{exe}</c>.</param>
    /// <param name="settings">Integrity (required) and optional overrides.</param>
    /// <returns>The registered files.</returns>
    /// <exception cref="CakeException">The settings are invalid or conflict with the arguments, or downloading, verifying or extracting failed.</exception>
    /// <example>
    /// <code>
    /// DownloadTool(
    ///     package: "cyclonedx",
    ///     version: "0.30.0",
    ///     url: "https://github.com/CycloneDX/cyclonedx-cli/releases/download/v{version}/cyclonedx-{rid}{exe}",
    ///     settings: new DownloadToolSettings()
    ///         .WithSha256("win-x64", "1f563ba9644d2f2966fc8029fd701ca4af4f388d44c017c1d60559a1ecc9114f")
    ///         .WithSha256("linux-x64", "f89876326620f5fc78a9b27cc1af57d6ed13d019aab87490e1246a44a910babb"));
    /// </code>
    /// </example>
    [CakeMethodAlias]
    public static IReadOnlyCollection<FilePath> DownloadTool(this ICakeContext context, string package, string version, string url, DownloadToolSettings settings)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(settings);

        return CreateRunner(context).Install(package, version, url, settings);
    }
```

- [ ] **Step 6: Run the alias tests**

Run: `dotnet test --project test/Cake.Download.Module.Tests/Cake.Download.Module.Tests.csproj --filter-class "*DownloadToolAliasesTests"`
Expected: PASS.

- [ ] **Step 7: CHANGELOG**

Under `## [Unreleased]` → `### Added`, append after the `DownloadTool` entry from Task 2:

```markdown
- `DownloadToolSettings`, a typed alternative to the directive string: `DownloadTool(package, version, url, settings)`
  and `DownloadTool(settings)` take fluent settings such as `WithSha256(rid, hash)`, `WithChecksums(file, hash)`,
  `WithOs(…)` and `WithDialect(DownloadDialect.DotNet)`. `ToDirective()` and `ToDirectiveUri()` write the settings as
  a `download:` directive for `InstallTool`. Error messages for settings suggest the method to add instead of a
  directive parameter.
```

- [ ] **Step 8: Run everything and commit**

Run: `./build.ps1 --target Test`
Expected: PASS.

```bash
git add src/Cake.Download.Module test/Cake.Download.Module.Tests CHANGELOG.md
git commit -m "Add the DownloadTool overloads for DownloadToolSettings

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Runner tests for the settings alias

Moves `cyclonedx` from an eager install to the four-argument settings overload in all three runners.

**Files:**
- Modify: `test/runners/script/build.cake`, `test/runners/sdk/cake.cs`, `test/runners/frosting/Program.cs`,
  `test/runners/frosting/OnDemandTask.cs`
- Modify: `build/RunnerTests/ReportAssertions.cs`
- Test: `test/Build.Tests/ReportAssertionsTests.cs`

**Interfaces:**
- Consumes: `DownloadTool(string package, string version, string url, DownloadToolSettings settings)`,
  `DownloadToolSettings`, `DownloadDialect` (Tasks 4–5); `ReportAssertions.CheckDryRun` (Task 3).

- [ ] **Step 1: Extend the dry-run test first**

In `ReportAssertionsTests`, add:

```csharp
    [Fact]
    public void CheckDryRun_Reports_The_Settings_Install_Too()
    {
        Directory.CreateDirectory(Path.Combine(_tools, "cyclonedx.0.30.0"));

        Assert.Equal(["--dryrun installed cyclonedx.0.30.0; DownloadTool must only install when its task runs"], ReportAssertions.CheckDryRun(_tools));
    }
```

Run: `dotnet test --project test/Build.Tests/Build.Tests.csproj`
Expected: FAIL (empty result).

In `ReportAssertions`: `public static readonly string[] OnDemandInstallFolders = ["jq.1.8.2", "cyclonedx.0.30.0"];`

Run again. Expected: PASS.

- [ ] **Step 2: Script and SDK runners**

`test/runners/script/build.cake`: delete the `cyclonedx` `#tool` line, and add `using Cake.Download.Module;` as the
last line before the blank line above `// --- pipeline ---`.

`test/runners/sdk/cake.cs`: delete the `cyclonedx` `InstallTool(...)` line, and add `using Cake.Download.Module;` right
after the `#:package` line (C# requires `using` before top-level statements).

In **both** pipelines, the `OnDemand` task becomes:

```csharp
Task("OnDemand").Does(() =>
{
    DownloadTool("download:https://github.com/jqlang/jq/releases/download/jq-{version}/jq-{os}-{arch}{exe}?package=jq&version=1.8.2&os.darwin=macos&checksums=sha256sum.txt&checksums_sha256=dc86824a41c165ece971ff691aff6e08bbfe6e1d1f531688b47ee78c283a85cd");
    DownloadTool(
        package: "cyclonedx",
        version: "0.30.0",
        url: "https://github.com/CycloneDX/cyclonedx-cli/releases/download/v{version}/cyclonedx-{rid}{exe}",
        settings: new DownloadToolSettings()
            .WithDialect(DownloadDialect.DotNet)
            .WithSha256("win-x64", "1f563ba9644d2f2966fc8029fd701ca4af4f388d44c017c1d60559a1ecc9114f")
            .WithSha256("win-arm64", "866809c6e2617c39d0b11713872ae35b88c98941c22dc66d9a4b633fa56db82a")
            .WithSha256("linux-x64", "f89876326620f5fc78a9b27cc1af57d6ed13d019aab87490e1246a44a910babb")
            .WithSha256("linux-arm64", "190da406177311aa1081edd0c717df10271eba7e4356a56215494a70e1a4b459")
            .WithSha256("osx-x64", "1603264fd2968b8d617e48aa7e9cf17bee1d25a8ffe717aec37caf1605a21961")
            .WithSha256("osx-arm64", "dabbaf07e543e7996f708147475e2daa69ddf8a8683c5b06febc7d3f074e5e24"));
});
```

- [ ] **Step 3: Frosting runner**

`test/runners/frosting/Program.cs`: delete the `cyclonedx` `.InstallTool(new Uri(...))` line.

`test/runners/frosting/OnDemandTask.cs`, `Run` becomes:

```csharp
    public override void Run(ScenarioContext context)
    {
        context.DownloadTool("download:https://github.com/jqlang/jq/releases/download/jq-{version}/jq-{os}-{arch}{exe}?package=jq&version=1.8.2&os.darwin=macos&checksums=sha256sum.txt&checksums_sha256=dc86824a41c165ece971ff691aff6e08bbfe6e1d1f531688b47ee78c283a85cd");
        context.DownloadTool(
            package: "cyclonedx",
            version: "0.30.0",
            url: "https://github.com/CycloneDX/cyclonedx-cli/releases/download/v{version}/cyclonedx-{rid}{exe}",
            settings: new DownloadToolSettings()
                .WithDialect(DownloadDialect.DotNet)
                .WithSha256("win-x64", "1f563ba9644d2f2966fc8029fd701ca4af4f388d44c017c1d60559a1ecc9114f")
                .WithSha256("win-arm64", "866809c6e2617c39d0b11713872ae35b88c98941c22dc66d9a4b633fa56db82a")
                .WithSha256("linux-x64", "f89876326620f5fc78a9b27cc1af57d6ed13d019aab87490e1246a44a910babb")
                .WithSha256("linux-arm64", "190da406177311aa1081edd0c717df10271eba7e4356a56215494a70e1a4b459")
                .WithSha256("osx-x64", "1603264fd2968b8d617e48aa7e9cf17bee1d25a8ffe717aec37caf1605a21961")
                .WithSha256("osx-arm64", "dabbaf07e543e7996f708147475e2daa69ddf8a8683c5b06febc7d3f074e5e24"));
    }
```

- [ ] **Step 4: Run the runner tests on both Cake versions**

Run: `./build.ps1 --target RunnerTests`
Then: `./build.ps1 --target RunnerTests --cake-version 6.0.0`
Expected: all three runners pass in both runs. The `cyclonedx` report entry is unchanged
(`cyclonedx.0.30.0/cyclonedx[.exe]`), which shows the settings install matches the old directive install. If the SDK
runner fails to compile `DownloadToolSettings`/`DownloadDialect` or the named-argument call, that is a spec §5.1
item 2 failure: **stop and report** as described in Task 3 Step 8.

- [ ] **Step 5: Commit**

```bash
git add build test/runners test/Build.Tests
git commit -m "Install cyclonedx on demand with DownloadToolSettings in the runner tests

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: README, AGENTS.md and design note

**Files:**
- Modify: `README.md` (two new sections after "Where tools go")
- Modify: `AGENTS.md` (public API rule)
- Modify: `docs/superpowers/specs/2026-10-02-cake-download-module-design.md` §13

- [ ] **Step 1: README — on-demand section**

Insert after the "Where tools go" section:

````markdown
## Installing a tool only when a task runs

`#tool`, Frosting's `CakeHost.InstallTool` and a top-level Cake.Sdk `InstallTool` install every tool before the
target runs, on every run: other targets, `--dryrun` and `--tree` included. For a large tool that only one task needs,
call the `DownloadTool` alias inside that task instead. It installs and registers the tool when the task runs, and
later runs with an unchanged tool make no network requests.

Cake .NET Tool (`build.cake`): load the package as an addin (keep `#module` too if you also use `#tool "download:…"`):

```csharp
#addin nuget:?package=Cake.Download.Module&version=0.1.0-preview.1

Task("Sbom").Does(() =>
{
    DownloadTool("download:https://github.com/jqlang/jq/releases/download/jq-{version}/jq-{os}-{arch}{exe}?package=jq&version=1.8.2&os.darwin=macos&checksums=sha256sum.txt&checksums_sha256=dc86824a41c165ece971ff691aff6e08bbfe6e1d1f531688b47ee78c283a85cd");
    StartProcess(Context.Tools.Resolve(IsRunningOnWindows() ? "jq.exe" : "jq"), "--version");
});
```

Cake SDK (`cake.cs`): the same call inside `.Does(...)`. Cake.Sdk's own `InstallTool(...)` also installs immediately
when it is called inside a task.

Cake Frosting: `context.DownloadTool(...)` in the task's `Run`, with `using Cake.Download.Module;`. `UseModule` is
not needed for the alias. Injecting `Cake.Frosting.IToolInstaller` into the task and calling
`Install(new PackageReference("download:…"))` also works.

`DownloadTool` returns the registered files, but Cake's tool aliases and `Context.Tools.Resolve(...)` find the tool
without them.
````

The README's install snippets must use the current release version. Copy it from the existing `#module` line rather
than the `0.1.0-preview.1` shown here, if they differ.

- [ ] **Step 2: README — typed settings section**

Insert after the section from Step 1:

````markdown
## Typed settings

`DownloadToolSettings` describes the same download as a directive, with methods instead of parameters:

```csharp
DownloadTool(
    package: "cyclonedx",
    version: "0.30.0",
    url: "https://github.com/CycloneDX/cyclonedx-cli/releases/download/v{version}/cyclonedx-{rid}{exe}",
    settings: new DownloadToolSettings()
        .WithDialect(DownloadDialect.DotNet)
        .WithSha256("win-x64", "1f563ba9644d2f2966fc8029fd701ca4af4f388d44c017c1d60559a1ecc9114f")
        .WithSha256("linux-x64", "f89876326620f5fc78a9b27cc1af57d6ed13d019aab87490e1246a44a910babb")
        .WithSha256("osx-arm64", "dabbaf07e543e7996f708147475e2daa69ddf8a8683c5b06febc7d3f074e5e24"));
```

To avoid three positional strings, put them in the settings and call `DownloadTool(settings)`:

```csharp
DownloadTool(new DownloadToolSettings()
    .WithPackage("cyclonedx")
    .WithVersion("0.30.0")
    .WithUrl("https://github.com/CycloneDX/cyclonedx-cli/releases/download/v{version}/cyclonedx-{rid}{exe}")
    .WithDialect(DownloadDialect.DotNet)
    .WithSha256("linux-x64", "f89876326620f5fc78a9b27cc1af57d6ed13d019aab87490e1246a44a910babb"));
```

| Method | Directive parameter |
|---|---|
| `WithPackage(name)`, `WithVersion(version)` | `package`, `version` |
| `WithUrl(template)` / `WithUrl(rid, template)` | the URL template or `url` / `url.<rid>` |
| `WithSha256(hash)` / `WithSha256(rid, hash)` | `sha256` / `sha256.<rid>` |
| `WithChecksums(file, hash)` | `checksums` + `checksums_sha256` |
| `WithoutVerification()` | `sha256=skip` |
| `WithDialect(DownloadDialect.Go \| DotNet \| Rust)` | `dialect` |
| `WithOs(value, override)`, `WithArch(value, override)`, `WithArchive(os, extension)` | `os.<value>`, `arch.<value>`, `archive.<os>` |
| `WithTriple(rid, triple)` | `triple.<rid>` |
| `WithFormat(DownloadFormat.File \| Zip \| Tar \| TarGz)` | `format` |
| `WithFileName(name)` | `filename` |
| `WithInclude(glob)`, `WithExclude(glob)` (repeatable) | `include`, `exclude` |

Leave out the integrity option once, and the error message contains the exact method to add, with the hash of what
was downloaded. Validation is the directive's: settings are written out as a directive and parsed.

`ToDirective()` and `ToDirectiveUri()` turn settings into a directive for installs that run up front, e.g.
`InstallTool(settings.ToDirective())` in Cake.Sdk or `.InstallTool(settings.ToDirectiveUri())` in Frosting.
````

- [ ] **Step 3: AGENTS.md**

Replace the public API bullet with:

```markdown
- **Public API:** only `DownloadModule`, `DownloadPackageInstaller`, `DownloadToolAliases`, `DownloadToolSettings`,
  `DownloadDialect` and `DownloadFormat` are public. Everything else is `internal`, tested through
  `InternalsVisibleTo`.
```

And in the Design bullet, add the alias spec:

```markdown
- **Design:** read `docs/superpowers/specs/2026-10-02-cake-download-module-design.md` before changing the directive
  grammar, the integrity model or the install layout, and `docs/superpowers/specs/2026-10-04-download-tool-alias-design.md`
  before changing the `DownloadTool` alias or `DownloadToolSettings`.
```

- [ ] **Step 4: Design note**

In `docs/superpowers/specs/2026-10-02-cake-download-module-design.md` §13, append to the "Alias-based installation
(approach 2)" bullet and the "Typed directive builder (approach 3)" bullet:

```markdown
  *Superseded by [`2026-10-04-download-tool-alias-design.md`](2026-10-04-download-tool-alias-design.md), which adds
  both as a complement to `#tool`, not a replacement.*
```

- [ ] **Step 5: Verify the release gates still pass, then commit**

Run: `./build.ps1 --target Release-Notes`
Expected: PASS (README install snippets still point at the current version; CHANGELOG parses).

```bash
git add README.md AGENTS.md docs/superpowers/specs/2026-10-02-cake-download-module-design.md
git commit -m "Document on-demand installs and DownloadToolSettings

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
