# DownloadTool alias — design

> Status: draft for review · 2026-10-04
> Builds on: [`2026-10-02-cake-download-module-design.md`](2026-10-02-cake-download-module-design.md) (the main
> design). This spec replaces the main design's §13 decision that rejected an alias.
> Motivation: [Cake.CycloneDX's on-demand CLI download](https://github.com/mgnslndh/Cake.CycloneDX) needed custom
> Frosting code (`IToolInstaller` injection) to install a tool only when one task runs.

## 1. Summary

Add a `DownloadTool` alias that installs a `download:` tool **when it is called**, typically inside a task, and
registers it with Cake's tool locator. It accepts either a complete directive string/`Uri`, or a typed
`DownloadToolSettings`. Also add a README section that explains eager and on-demand installs for all three runners.

```csharp
Task("RunnerTests").Does(() =>
{
    DownloadTool(
        package: "cyclonedx",
        version: "0.30.0",
        url: "https://github.com/CycloneDX/cyclonedx-cli/releases/download/v{version}/cyclonedx-{rid}{exe}",
        new DownloadToolSettings()
            .WithSha256("win-x64", "1f56…")
            .WithSha256("linux-x64", "f898…")
            .WithSha256("osx-arm64", "dabb…"));
});
```

### Problem

`#tool`, Frosting's `CakeHost.InstallTool` and a top-level Cake.Sdk `InstallTool` install every declared tool before
the target runs. They do this on every run, `--dryrun`, `--tree` and targets that don't use the tool included. For a
large tool (the CycloneDX CLI is ~74 MB) used by one target, every CI job and every local run pays for the download,
and a release job that must avoid third-party downloads gets one anyway.

What exists today:

| Runner | On-demand install today |
|---|---|
| Cake .NET Tool (script) | **None.** `#tool` is processed before the script runs; script code can't reach the installer. |
| Cake.Sdk | `InstallTool(...)` inside `.Does(...)` installs immediately (it calls `ToolInstaller.Install`). Undocumented by us. |
| Frosting | Inject `Cake.Frosting.IToolInstaller` into a task and call `Install`. Undocumented by us. |

### Success criteria

- A task can install a `download:` tool on demand in all three runners, with the same call.
- A run that doesn't execute that task (other target, `--dryrun`) makes no request for the tool and creates no install
  folder.
- Typed settings and directives produce identical installs; validation, integrity and the install store are not
  duplicated.
- The package still has zero NuGet dependencies and still loads on Cake 6.0.0.

## 2. Scope

### In scope

- The `DownloadTool` alias (four overloads, §3.1).
- `DownloadToolSettings`, `DownloadDialect`, `DownloadFormat` (§3.2).
- `ToDirective()` / `ToDirectiveUri()` on the settings.
- Error hints phrased for the input form the user wrote (§4.4).
- README section "Installing a tool only when a task runs" and the typed-settings reference (§6).

### Out of scope

- A global download cache.
- A task attribute (e.g. `[DownloadTool("…")]` on a Frosting task): Frosting-only, needs a `Cake.Frosting` reference
  and a task-lifetime hook, and saves one line.
- Execution-only settings that the directive can't express (retry count, stall timeout, install folder, verbosity).
  `DownloadToolSettings` can gain them later without breaking changes; logging already follows Cake's `--verbosity`.
- `Action<DownloadToolSettings>` configurator overloads and a plural `DownloadTools`.
- Overloads that take a directive *and* settings (no merging of the two forms).

## 3. Public API

Public types go from two to six:

| Type | Role |
|---|---|
| `DownloadModule` | unchanged |
| `DownloadPackageInstaller` | unchanged public surface |
| `DownloadToolAliases` | static class with the aliases; `[CakeAliasCategory("Download")]` |
| `DownloadToolSettings` | typed description of a download |
| `DownloadDialect` | enum: `Go`, `DotNet`, `Rust` |
| `DownloadFormat` | enum: `File`, `Zip`, `Tar`, `TarGz` |

The assembly gets `[assembly: CakeNamespaceImport("Cake.Download.Module")]` (or the attribute on the alias class), so
scripts need no `using`.

### 3.1 Aliases

All overloads are `[CakeMethodAlias]` extension methods on `ICakeContext` and return
`IReadOnlyCollection<FilePath>`: the files that were registered with `context.Tools`, in the same order the installer
returns them. Plain overloads, no optional parameters (they interact poorly with generated alias proxies and with
binary compatibility).

There are two separate families.

**Directive family — complete, no settings:**

```csharp
IReadOnlyCollection<FilePath> DownloadTool(this ICakeContext context, string directive)
IReadOnlyCollection<FilePath> DownloadTool(this ICakeContext context, Uri directive)
```

- The value is exactly what `#tool "…"` takes: it must start with `download:`. Anything else is an error that names the
  expected scheme. A `#tool` line moves into a task by copy and paste.
- The `Uri` overload uses `Uri.OriginalString` (braces survive), the same input Frosting's `CakeHost.InstallTool(Uri)`
  takes.

**Settings family — typed, never based on a directive:**

```csharp
IReadOnlyCollection<FilePath> DownloadTool(this ICakeContext context, DownloadToolSettings settings)
IReadOnlyCollection<FilePath> DownloadTool(this ICakeContext context, string package, string version, string url, DownloadToolSettings settings)
```

- `package`, `version`, `url` are the identity of the download ("jq 1.8.2 from …"). Order rationale: it reads as a
  sentence, the URL template uses `{version}`, and identifier-then-version is the usual .NET/NuGet order.
- Integrity is mandatory but stays in settings because it has four shapes. The first-run hint (§4.4) tells the user
  what to add.
- In the four-argument overload, the arguments fill `Package`, `Version` and `Url`. If `settings` also sets one of them
  to a **different** value, the call fails with a `CakeException` naming the property and both values. Setting the same
  value twice is harmless. `settings` is not modified.
- Users who don't want three positional strings use `DownloadTool(settings)` with `WithPackage`/`WithVersion`/`WithUrl`.
  The README and XML docs show the four-argument overload with named arguments.
- The four-argument overload requires a non-null `url`. The rare "per-platform URLs only" case uses
  `DownloadTool(settings)` with `WithUrl(rid, …)` for every platform.
- `null` arguments throw `ArgumentNullException`.

### 3.2 `DownloadToolSettings`

A mutable class with a parameterless constructor. Every property is nullable or an empty collection, meaning "not set".
Each property has a fluent `With…` method on the class itself (no separate extensions class) that sets the value and
returns `this`. The fluent methods don't validate; validation happens once, in the directive parser (§4.1).

| Property | Type | Fluent method(s) | Directive parameter |
|---|---|---|---|
| `Package` | `string?` | `WithPackage(string)` | `package` |
| `Version` | `string?` | `WithVersion(string)` | `version` |
| `Url` | `string?` | `WithUrl(string template)` | the template / `url` |
| `UrlByPlatform` | `IDictionary<string, string>` | `WithUrl(string rid, string template)` | `url.<rid>` |
| `Dialect` | `DownloadDialect?` | `WithDialect(DownloadDialect)` | `dialect` |
| `OsOverrides` | `IDictionary<string, string>` | `WithOs(string defaultValue, string value)` | `os.<value>` |
| `ArchOverrides` | `IDictionary<string, string>` | `WithArch(string defaultValue, string value)` | `arch.<value>` |
| `ArchiveOverrides` | `IDictionary<string, string>` | `WithArchive(string os, string extension)` | `archive.<os>` |
| `TripleOverrides` | `IDictionary<string, string>` | `WithTriple(string rid, string triple)` | `triple.<rid>` |
| `Format` | `DownloadFormat?` | `WithFormat(DownloadFormat)` | `format` |
| `FileName` | `string?` | `WithFileName(string)` | `filename` |
| `Include` | `IList<string>` | `WithInclude(string glob)` (appends) | `include` (repeatable) |
| `Exclude` | `IList<string>` | `WithExclude(string glob)` (appends) | `exclude` (repeatable) |
| `Sha256` | `string?` | `WithSha256(string hex)` | `sha256=<hex>` |
| `Sha256ByPlatform` | `IDictionary<string, string>` | `WithSha256(string rid, string hex)` | `sha256.<rid>` |
| `ChecksumsFile` | `string?` | `WithChecksums(string file, string sha256)` | `checksums` |
| `ChecksumsFileSha256` | `string?` | (set by `WithChecksums`) | `checksums_sha256` |
| `SkipVerification` | `bool` | `WithoutVerification()` | `sha256=skip` |

- Dictionaries use `StringComparer.OrdinalIgnoreCase`, like the parser.
- Platforms are named by .NET RID strings (`"linux-x64"`), as in the directive. No platform enum.
- Setting more than one integrity mode is not prevented by the API. It is reported by the parser as the same
  "conflicting integrity options" error the directive gets.
- The method is called `WithoutVerification()` because `SkipVerification` is the property name. It reads as an
  explicit opt-out next to the `With…` integrity methods.

**Directive output:**

```csharp
string ToDirective()
Uri ToDirectiveUri()   // new Uri(ToDirective())
```

- Both throw `CakeException` if `Package` or `Version` is missing, or if neither `Url` nor `UrlByPlatform` is set.
  With only `UrlByPlatform`, the directive has no leading template, as the parser allows
  (`DirectiveParser.cs:93`); a platform without an entry then fails at install time, as it does today.
- They are for using typed settings with eager installs: Cake.Sdk `InstallTool(settings.ToDirective())`, Frosting
  `.InstallTool(settings.ToDirectiveUri())`.

## 4. How it works

### 4.1 One pipeline

The directive remains the single source of truth:

```
DownloadTool(string|Uri directive)          → PackageReference ─┐
DownloadTool(settings) / (p, v, u, settings) → ToDirective()  → PackageReference ─┤
                                                                ▼
                     DirectiveParser.Parse → DownloadPlanner → install pipeline (unchanged)
```

Every settings call round-trips through the parser, so `ToDirective()` and the settings alias can't drift apart, and
all existing validation applies unchanged.

### 4.2 `ToDirective` encoding

- **Leading template:** `Url` is written raw (braces intact) before `?` when it contains none of `?`, `#`, `&`.
  Otherwise it is written as `url=<percent-encoded>` and there is no leading template.
- **Values:** every parameter value is percent-encoded (`Uri.EscapeDataString`); the parser already decodes values.
- **Order is deterministic:** `package`, `version`, `url`, `url.<rid>`, `dialect`, `os.*`, `arch.*`, `archive.*`,
  `triple.*`, `format`, `filename`, `include`, `exclude`, then integrity (`sha256` / `sha256.<rid>` / `checksums` +
  `checksums_sha256`). Keys inside a map are sorted ordinally. `include`/`exclude` keep insertion order.
- Enums map to the grammar's names: `Go` → `go`, `DotNet` → `dotnet`, `Rust` → `rust`; `File` → `file`, `Zip` →
  `zip`, `Tar` → `tar`, `TarGz` → `tar.gz`.
- Unset properties are omitted, so defaults stay the parser's defaults.

### 4.3 Running the install

The alias doesn't use the Cake container, so it works without `#module` / `UseModule<DownloadModule>()`:

1. Build the `PackageReference` (§4.1).
2. Tools folder: `context.Configuration.GetToolPath(context.Environment.WorkingDirectory, context.Environment)`, the
   `Paths_Tools`-aware folder that `#tool` and `InstallTool` use.
3. `new DownloadPackageInstaller(context.Environment, context.FileSystem, context.Log)
   .Install(reference, PackageType.Tool, toolsPath)`, with the input source (§4.4) passed internally.
4. `context.Tools.RegisterFile(file.Path)` for each returned file. Return the paths.

A repeated call in the same or a later run hits the `.cake-download.json` check: no network, files registered again.
Tests reach the installer through an internal seam (an internal overload or factory) to inject the fake HTTP handler
and platform detector.

### 4.4 Error hints per input form

Three messages contain something to paste: missing integrity / missing RID entry, unpinned checksums file, and the
HTTP 404 override hint. The plan carries an internal `DirectiveSource` (`Directive` or `Settings`), and a small hint
formatter renders a parameter in the user's form:

| Parameter | Directive form | Settings form |
|---|---|---|
| `sha256.linux-x64=<hex>` | `&sha256.linux-x64=<hex>` | `.WithSha256("linux-x64", "<hex>")` |
| `sha256=skip` | `&sha256=skip` | `.WithoutVerification()` |
| `checksums_sha256=<hex>` | `&checksums_sha256=<hex>` | `.WithChecksums("<file>", "<hex>")` |
| `os.<value>=`, `arch.<value>=`, `archive.<value>=` | as today | `.WithOs(…)`, `.WithArch(…)`, `.WithArchive(…)` |
| `url.<rid>=` | as today | `.WithUrl("<rid>", …)` |

`#tool` and host `InstallTool` always use the directive form. Other validation errors (bad hex, unknown RID, invalid
package name) keep naming the directive parameter; for settings calls the message also includes the generated directive
for context.

## 5. Packaging and loading

One assembly, one package. The package gets the `cake-addin` tag in addition to `cake-module`; `PackageVerifier`
checks both. `Cake.Core` stays `PrivateAssets="all"` at 6.0.0, which already has `GetToolPath` and
`IToolLocator.RegisterFile`. Zero dependencies.

| Runner | `#tool` / host install | Alias |
|---|---|---|
| Script | `#module nuget:?package=Cake.Download.Module&version=…` | `#addin nuget:?package=Cake.Download.Module&version=…` |
| Cake.Sdk | `#:package Cake.Download.Module@…` | same reference; Cake.Generator generates a top-level `DownloadTool(...)` |
| Frosting | `PackageReference` + `UseModule<DownloadModule>()` | same reference + `using Cake.Download.Module;`, `context.DownloadTool(...)` |

### 5.1 To verify before implementation

1. **Script with `#module` and `#addin` of the same package**, on Cake.Tool 6.0.0 and the latest 6.x: both load,
   `#tool "download:…"` works and the alias is callable in the same script.
   **If this fails, stop and discuss with the user before choosing a fallback.** The candidates are: documenting
   `#module` *or* `#addin` per script, or splitting into two packages built from shared source. Neither is decided.
2. **Cake.Sdk 6.0.0 and latest:** Cake.Generator generates the `DownloadTool` proxies (all four overloads) from the
   module package referenced with `#:package`.
3. `GetToolPath(...)` resolves to the same folder `#tool` / `InstallTool` install into, in each runner (default and
   with `Paths_Tools` set).

## 6. README

New section **"Installing a tool only when a task runs"**:

- Why: `#tool`, host `InstallTool` and top-level SDK `InstallTool` install on every run, including `--dryrun`,
  `--tree` and targets that don't use the tool.
- Per runner, the `DownloadTool` alias inside a task (script, SDK, Frosting), with the `#addin` line for scripts.
- The existing alternatives: Cake.Sdk `InstallTool(...)` inside `.Does(...)`, and Frosting `IToolInstaller` injection.

New section **"Typed settings"**:

- The four overloads; the four-argument overload with named arguments; `DownloadTool(settings)` as the all-named form.
- A table of `With…` methods next to their directive parameters (mirrors the existing parameter table).
- `ToDirective()` / `ToDirectiveUri()` for eager installs with typed settings.

The Install section keeps the eager examples unchanged.

## 7. Testing

### 7.1 Unit tests — `test/Cake.Download.Module.Tests`

| Unit | Covered |
|---|---|
| `DownloadToolSettings.ToDirective` | every property round-trips: `ToDirective()` parses into the expected `DownloadDirective`; raw braces in the leading template; `Url` with `?`/`#`/`&` moves to `url=`; percent-encoding of special characters in values; deterministic order; unset properties omitted; enum names; missing `Package`/`Version`/`Url`; `ToDirectiveUri()` equals `new Uri(ToDirective())` and keeps braces in `OriginalString` |
| Fluent methods | each sets its property and returns the same instance; `WithInclude`/`WithExclude` append; dictionary keys case-insensitive |
| Alias, directive family | non-`download:` value rejected; `string` and `Uri` give identical installs; files registered with `context.Tools`; returned paths; `Paths_Tools` respected |
| Alias, settings family | four-argument overload fills `Package`/`Version`/`Url`; conflicting values rejected, equal values accepted; `settings` not modified; null arguments; settings call and equivalent directive call give identical installs and markers; repeat call makes no HTTP request |
| Hint formatter | each row of §4.4 in both forms, end to end through the installer (missing RID hash, unpinned checksums, 404) |

### 7.2 Runner tests

All three runners (`./build.ps1 --target RunnerTests`, latest 6.x and `--cake-version 6.0.0`):

- A task installs one tool with the settings overload and one with the directive overload, resolves both through
  `Context.Tools.Resolve(...)` and runs them with `--version`. The report includes them, and reports stay identical
  across runners.
- **Laziness:** running the scenario with `--dryrun` leaves the tools folder without those tools' install folders.
- The pipeline after `// --- pipeline ---` stays identical in `script/build.cake` and `sdk/cake.cs`, and the script
  runner template gains the `#addin` line.

### 7.3 Package verification

`PackageVerifier` also requires the `cake-addin` tag.

## 8. Docs and housekeeping

- `AGENTS.md`: the public API rule lists the six public types.
- `CHANGELOG.md`, `## [Unreleased]` → `Added`: the `DownloadTool` alias, `DownloadToolSettings` with
  `ToDirective()`/`ToDirectiveUri()`.
- The main design's §13 "Alias-based installation (approach 2)" and "Typed directive builder (approach 3)" are
  superseded by this spec; add a note there pointing here.
