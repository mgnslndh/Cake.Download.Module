# Cake.Download.Module — design

> Status: draft for review · 2026-10-02
> Input: [`docs/RESEARCH-cake-download-module.md`](../../RESEARCH-cake-download-module.md)
> Related upstream issue: [cake-build/cake#2731](https://github.com/cake-build/cake/issues/2731)

## 1. Summary

A Cake module that adds a `download:` scheme to `#tool` / `InstallTool`. It downloads a CLI tool from a
public HTTPS URL, verifies its SHA-256, extracts it if it is an archive, and registers the right files
with Cake's tool locator. Existing `Tool<TSettings>` aliases and `Context.Tools.Resolve(...)` then find
the tool with no further configuration.

```csharp
#module nuget:?package=Cake.Download.Module&version=1.0.0
#tool "download:https://github.com/jqlang/jq/releases/download/jq-{version}/jq-{os}-{arch}{exe}?package=jq&version=1.8.2&os.darwin=macos&checksums=sha256sum.txt&checksums_sha256=<hex>"
```

Placeholders such as `{os}`, `{arch}` and `{exe}` make one directive work on Windows, Linux and macOS.
This covers GitHub Releases without a dedicated `github:` scheme.

### Success criteria

- A public GitHub release asset (raw binary **and** archive) installs and resolves end-to-end in all
  three Cake runners (Cake.Tool script, Cake SDK, Frosting), on Windows, Linux and macOS, for both the
  oldest supported (6.0.0) and the latest Cake 6.x.
- Every install is integrity-checked unless the directive explicitly opts out.
- A second build with an unchanged directive makes **no network requests**.
- The package has zero NuGet dependencies, so it works as a `#module` in scripts.

## 2. Scope

### In scope (v1)

- `download:` scheme for `PackageType.Tool`.
- Public, unauthenticated **HTTPS** URLs, following redirects (for example GitHub's 302 to
  `release-assets.githubusercontent.com`).
- Platform placeholders with three dialects: `go` (default), `dotnet` and `rust`.
- Mandatory integrity: a pinned asset hash, per-platform pinned hashes, or a pinned checksums file.
  Opting out is explicit.
- Formats: raw file, `zip`, `tar`, `tar.gz` / `tgz`.
- Idempotent, atomic install into `<tools>/<package>.<version>/`, safe under concurrent builds.
- `include` / `exclude` globs choosing which files are registered.

### Out of scope (v1)

- Authentication, private repositories, GitHub Enterprise Server.
- A `github:` scheme or any GitHub API usage (see §13).
- Plain `http:` URLs.
- A global, cross-repository cache (see §13).
- Mirrors / air-gapped redirection.
- Signature or attestation verification (cosign, GitHub artifact attestations).
- `tar.xz`, `tar.bz2`, `7z` (no BCL support, and the module may not take dependencies).
- `latest` or version ranges.

## 3. Usage examples

```csharp
// Raw binary, Go-style names, pinned checksums file (jq)
#tool "download:https://github.com/jqlang/jq/releases/download/jq-{version}/jq-{os}-{arch}{exe}?package=jq&version=1.8.2&os.darwin=macos&checksums=sha256sum.txt&checksums_sha256=<hex>"

// Archive whose format differs per OS (gh)
#tool "download:https://github.com/cli/cli/releases/download/v{version}/gh_{version}_{os}_{arch}.{archive}?package=gh&version=2.62.0&os.darwin=macOS&archive.darwin=zip&checksums=gh_{version}_checksums.txt&checksums_sha256=<hex>"

// Rust target triples (ripgrep)
#tool "download:https://github.com/BurntSushi/ripgrep/releases/download/{version}/ripgrep-{version}-{triple}.{archive}?package=rg&version=14.1.1&dialect=rust&checksums=…&checksums_sha256=<hex>"

// .NET RIDs, raw file, per-platform pinned hashes (cyclonedx-cli)
#tool "download:https://github.com/CycloneDX/cyclonedx-cli/releases/download/v{version}/cyclonedx-{rid}{exe}?package=cyclonedx&version=0.30.0&dialect=dotnet&sha256.win-x64=<hex>&sha256.linux-x64=<hex>&sha256.osx-arm64=<hex>"

// Single URL, same file on every platform
#tool "download:https://example.com/tools/mytool-3.1.jar?package=mytool&version=3.1&sha256=<hex>&filename=mytool.jar"
```

Frosting:

```csharp
new CakeHost()
    .UseModule<DownloadModule>()
    .InstallTool(new Uri("download:https://github.com/jqlang/jq/releases/download/jq-{version}/jq-{os}-{arch}{exe}?package=jq&version=1.8.2&os.darwin=macos&checksums=sha256sum.txt&checksums_sha256=<hex>"))
    .Run(args);
```

## 4. Directive grammar

```
download:<url-template>?package=<name>&version=<version>[&<parameter>=<value>]*
```

### 4.1 How Cake parses the directive (verified against upstream `develop`)

- `PackageReference.Parameters` comes from `UriExtensions.GetQueryString`. Keys are
  **case-insensitive**. Values are **not URL-decoded**. A value containing a raw `=` throws
  `CakeException("Could not parse query string.")`. A bare key (`?flag`) parses as a key with no values.
- `PackageReference.Address` is built from `Uri.AbsolutePath`, which percent-escapes `{` and `}`.
  **The module therefore ignores `Address`.** It takes the URL template from `OriginalString`: the text
  between the `download:` prefix and the first `?`.
- The module percent-decodes every parameter value itself (`Uri.UnescapeDataString`). Values that need
  a literal `=`, `&`, `?` or `#` (typically `url=`) must be percent-encoded by the user.

### 4.2 Parameters

| Parameter | Required | Multi | Meaning |
|---|---|---|---|
| `package` | ✅ | – | Tool name. Used for the install folder, the default `filename` and the default `include`. Must match `^[A-Za-z0-9._-]+$`. |
| `version` | ✅ | – | Pinned version. Fills `{version}` and names the install folder. Must match `^[A-Za-z0-9._+-]+$`. `latest` (any case) is rejected. |
| `sha256` | integrity, see §5 | – | 64 hex characters, or the literal `skip`. |
| `sha256.<rid>` | integrity | ✅ (one per RID) | Per-platform pinned asset hash. |
| `checksums` | integrity | – | URI reference to a checksums file, relative to the expanded download URL or absolute. |
| `checksums_sha256` | with `checksums` | – | Pinned hash of the checksums file itself. |
| `dialect` | | – | `go` (default), `dotnet` or `rust`. |
| `os.<os>` | | ✅ | Overrides the dialect's `{os}` value. `<os>` is the dialect's **default** value, e.g. `os.darwin=macOS` in `go`. |
| `arch.<arch>` | | ✅ | Overrides `{arch}`. `<arch>` is the dialect's default value, e.g. `arch.amd64=x86_64` in `go`. |
| `archive.<os>` | | ✅ | Overrides `{archive}` for that OS. `<os>` is the dialect's default `{os}` value. |
| `triple.<rid>` | | ✅ | Overrides `{triple}` (rust dialect only), e.g. `triple.linux-x64=x86_64-unknown-linux-gnu`. |
| `url` | | – | Full URL template, used instead of the part before `?`. Specifying both is an error. |
| `url.<rid>` | | ✅ | URL template for that RID. Takes precedence over the default template. |
| `format` | | – | `file`, `zip`, `tar` or `tar.gz`. Default: detected from the expanded URL path (`.zip`, `.tar`, `.tar.gz`, `.tgz`), otherwise `file`. |
| `filename` | | – | Name for a raw (`file`) download. Default `{package}{exe}`. Error if given with an archive format. |
| `include` | | ✅ | Glob relative to the install folder. Files to register. Default: `filename` for raw files, `**/{package}{exe}` for archives. |
| `exclude` | | ✅ | Glob relative to the install folder. Files removed from the `include` result. |

`<rid>` keys **always** use .NET RIDs (`win-x64`, `linux-arm64`, `osx-arm64`, …) whatever the dialect,
so users have one stable way to name a platform.

**Validation is strict.** Unknown parameters, repeated single-valued parameters, `rid` keys that are
not a supported RID, override keys that are not a value of the active dialect, `triple.*` outside the
rust dialect, and conflicting integrity options are errors. Every error names the offending parameter
and the directive.

### 4.3 Placeholders

| Placeholder | Value |
|---|---|
| `{version}` | `version` parameter |
| `{os}` | dialect value for the current OS (after overrides) |
| `{arch}` | dialect value for the current architecture (after overrides) |
| `{rid}` | .NET RID, e.g. `win-x64` (dialect-independent) |
| `{exe}` | `.exe` on Windows, otherwise empty |
| `{archive}` | `zip` on Windows, `tar.gz` otherwise (after `archive.*` overrides) |
| `{triple}` | Rust target triple (rust dialect only, after `triple.*` overrides) |

Placeholders are expanded in: the URL template, `url`, `url.<rid>`, `checksums`, `filename`, `include`
and `exclude`. An unknown placeholder (or `{triple}` outside the rust dialect) is an error.

### 4.4 Supported platforms and dialect tables

Supported RIDs: `win-x64`, `win-x86`, `win-arm64`, `linux-x64`, `linux-arm64`, `linux-arm`,
`osx-x64`, `osx-arm64`. Any other combination (FreeBSD, unknown OS, unsupported architecture) fails
with an error that lists the supported RIDs.

| Dialect | `{os}` win / linux / osx | `{arch}` x64 / arm64 / x86 / arm |
|---|---|---|
| `go` | `windows` / `linux` / `darwin` | `amd64` / `arm64` / `386` / `arm` |
| `dotnet` | `win` / `linux` / `osx` | `x64` / `arm64` / `x86` / `arm` |
| `rust` | `windows` / `linux` / `darwin` | `x86_64` / `aarch64` / `i686` / `armv7` |

Rust `{triple}` defaults (Linux defaults to statically linked **musl**):

| RID | `{triple}` |
|---|---|
| `win-x64` | `x86_64-pc-windows-msvc` |
| `win-x86` | `i686-pc-windows-msvc` |
| `win-arm64` | `aarch64-pc-windows-msvc` |
| `linux-x64` | `x86_64-unknown-linux-musl` |
| `linux-arm64` | `aarch64-unknown-linux-musl` |
| `linux-arm` | `armv7-unknown-linux-musleabihf` |
| `osx-x64` | `x86_64-apple-darwin` |
| `osx-arm64` | `aarch64-apple-darwin` |

## 5. Integrity model

**Exactly one** of these must be present, or the install fails:

| Mode | Parameters | Expected asset hash |
|---|---|---|
| Pinned | `sha256=<hex>` | the given hash; intended for URLs that resolve to the same file everywhere |
| Pinned per platform | one or more `sha256.<rid>=<hex>` | the entry for the current RID; a missing entry is an error |
| Pinned checksums file | `checksums=<ref>` **and** `checksums_sha256=<hex>` | looked up in the checksums file after its own hash is verified |
| Opt-out | `sha256=skip` | none; a warning is logged on every fresh install |

Rules:

- `checksums` without `checksums_sha256` (or the reverse) is an error. An unpinned checksums file adds
  nothing over HTTPS, because whoever can replace the asset can replace the file next to it.
- Mixing modes (e.g. `sha256` with `sha256.<rid>`, or `sha256=skip` with `checksums`) is an error.
- **Checksums file:**
  - The file is fetched from the expanded `checksums` reference, resolved against the expanded download
    URL (RFC 3986), and capped at 1 MiB.
  - The parser accepts the GNU format (`<hex>  name`, `<hex> *name`) and the BSD format
    (`SHA256 (name) = <hex>`). It ignores blank lines and tolerates CRLF and uppercase hex.
  - The asset is looked up by the percent-decoded last path segment of the expanded download URL.
    No match, or two different hashes for the same name, is an error.
- **Missing or failing integrity always gives the user something to paste.** If no integrity parameter
  is present, or the current RID has no `sha256.<rid>` entry, the asset is still downloaded so its hash
  can be computed. The install then fails with an error containing the exact parameter, e.g.
  `Add '&sha256.linux-x64=<hex>' to the directive`, and the download is discarded. For a checksums file
  without a pin, the error contains `&checksums_sha256=<hex>`.
- Verification happens **before** anything is extracted.

## 6. Platform detection

- **OS** comes from Cake's `ICakePlatform.Family`.
- **Architecture** comes from `RuntimeInformation.OSArchitecture` (Cake.Core 6.3.0's `ICakePlatform`
  only exposes `Is64Bit`). The *OS* architecture is used, not the process architecture, because the
  downloaded tool runs as its own process: an arm64 Mac running x64 .NET under Rosetta gets the native
  arm64 binary.
- Both are wrapped by an internal `IPlatformDetector` returning `PlatformInfo(Family, Architecture,
  Rid)`, so tests can simulate any platform.

## 7. Architecture

One assembly, `Cake.Download.Module.dll`, targeting `net8.0`.

### 7.1 Public surface

- `[assembly: CakeModule(typeof(DownloadModule))]`
- `public sealed class DownloadModule : ICakeModule` registers `DownloadPackageInstaller` as a
  singleton `IPackageInstaller`.
- `public sealed class DownloadPackageInstaller : IPackageInstaller`
  - `CanInstall`: `Scheme` equals `download` (case-insensitive) **and** type is `PackageType.Tool`.
  - `Install(package, type, path)`: runs the pipeline. `path` is the tools directory Cake passes in,
    which already reflects `Paths_Tools`.

Everything else is `internal`. Internal services are created by the installer, not registered in Cake's
container, so they can't collide with other modules' registrations. An internal constructor lets tests
inject fakes (`InternalsVisibleTo` the test project).

### 7.2 Internal units

| Unit | Responsibility | Depends on |
|---|---|---|
| `DirectiveParser` | `PackageReference` → immutable `DownloadDirective`; all validation in §4 | – |
| `PlatformDetector` (`IPlatformDetector`) | → `PlatformInfo` | `ICakePlatform`, `RuntimeInformation` |
| `GoDialect`, `DotNetDialect`, `RustDialect` (`IPlatformDialect`) + `DialectRegistry` | `PlatformInfo` → default placeholder values; known override keys | – |
| `PlaceholderExpander` | template + values (+ overrides) → string; errors on unknown placeholders | – |
| `DownloadPlan` (record) | the fully resolved request: expanded URL, format, filename, include/exclude, integrity spec, RID, install folder name | – |
| `IntegrityResolver` | integrity spec → `ExpectedHash` or `Skip`; fetches and verifies the checksums file | `HttpDownloader`, `ChecksumsFileParser` |
| `ChecksumsFileParser` | text → name→hash map | – |
| `HttpDownloader` | streams a URL to a file while computing SHA-256; retries; stall timeout | `HttpClient` |
| `ArchiveExtractor` | zip / tar / tar.gz → directory, with the safety rules in §8.5 | BCL |
| `InstallStore` | install/staging paths, marker read/write, cache-hit check, atomic publish, stale staging cleanup | BCL file system |
| `FileSelector` | include/exclude globs → `IFile`s; applies `+x` on Unix | Cake `IGlobber`, `IFileSystem` |

### 7.3 Flow of `Install`

1. `DirectiveParser` → `DownloadDirective`. `PlatformDetector` → `PlatformInfo`.
2. Resolve the dialect and expand all templates → `DownloadPlan`.
3. `InstallStore.IsCurrent(plan)` → on a hit, skip to step 7 (no network).
4. Create a staging folder. `IntegrityResolver` determines the expected hash (it may fetch the checksums
   file). `HttpDownloader` downloads the asset and computes its hash.
5. Compare the hashes (or fail with the paste-ready message from §5). Extract, or move the raw file to
   `filename`.
6. Write the marker into the staged content. `InstallStore.Publish` moves it into place (§8.4).
7. `FileSelector` returns the registered files. An empty result is an error.

## 8. Install lifecycle

### 8.1 Layout

Under the tools directory Cake passes to `Install` (default `./tools`, configurable through
`Paths_Tools`):

```
tools/
├── jq.1.8.2/                         ← <package>.<version>
│   ├── jq                            ← raw "jq-linux-amd64" saved as {package}{exe}, +x
│   └── .cake-download.json           ← marker
├── gh.2.62.0/
│   ├── gh_2.62.0_linux_amd64/        ← archive contents extracted verbatim (no stripping, no flattening)
│   │   ├── bin/gh                    ← matched by default include **/gh{exe} → registered
│   │   ├── share/man/man1/…          ← extracted, not registered
│   │   └── LICENSE
│   └── .cake-download.json
└── .gh.2.62.0.tmp-<guid>/            ← staging, exists only during an install
    ├── download/<asset>              ← downloaded + hashed here
    └── content/                      ← extracted here; becomes tools/gh.2.62.0/
```

- The downloaded archive and checksums file are never kept.
- Staging lives inside the tools directory so the final move stays on one volume and is atomic.
- Different versions get side-by-side folders. Old folders are never deleted automatically.

### 8.2 Marker (`.cake-download.json`)

```json
{
  "schema": 1,
  "package": "jq",
  "version": "1.8.2",
  "rid": "linux-x64",
  "url": "https://github.com/jqlang/jq/releases/download/jq-1.8.2/jq-linux-amd64",
  "format": "file",
  "filename": "jq",
  "integrity": "checksums:https://github.com/jqlang/jq/releases/download/jq-1.8.2/sha256sum.txt#<checksums_sha256>",
  "sha256": "<actual asset hash>",
  "installedAt": "2026-10-02T14:03:11Z"
}
```

`integrity` is a canonical fingerprint of the integrity spec: `sha256:<hex>`, `sha256:<hex>` for the
selected RID entry, `checksums:<resolved url>#<hex>`, or `skip`.

### 8.3 Cache hit

The install is current, and nothing is downloaded (not even the checksums file), when the marker
exists, parses, and its `schema`, `rid`, `url`, `format`, `filename` and `integrity` all equal the
plan. Any mismatch, an unreadable marker, or a folder without a marker means **reinstall**. `include`
and `exclude` are not part of the comparison, because everything is always extracted. Changing them
only changes which files are selected.

### 8.4 Concurrency (optimistic, no lock file)

- Each install stages into its own `.<package>.<version>.tmp-<guid>/`.
- Publishing is `Directory.Move(staging/content, final)`:
  - If it succeeds, done.
  - If it fails because `final` exists, re-read `final`'s marker:
    - Current: another build won. Discard staging and use `final`.
    - Not current: delete `final` and retry the move once.
  - On Windows, transient `IOException` / `UnauthorizedAccessException` (antivirus, indexers) are
    retried a few times with a short delay.
- The staging folder is always deleted afterwards (in `finally`).
- Before staging, `.<package>.<version>.tmp-*` folders older than one hour are deleted as leftovers from
  crashed builds.
- Worst case for concurrent first installs is a duplicate download. Corruption is not possible.

### 8.5 Extraction safety and file modes

- Every entry's destination path is normalized and must stay inside the content root. Absolute paths
  and `..` traversal are rejected with an error ("zip slip").
- **Tar:**
  - Uses `System.Formats.Tar` (`TarReader`, with `GZipStream` for `tar.gz`).
  - Regular files and directories are extracted, keeping Unix permission bits on Unix hosts.
  - Symlinks and hardlinks are only allowed when their target resolves inside the root. No entry or link
    may pass *through* a symlink, but a symlink may point *at* another symlink (`libfoo.so -> libfoo.so.1`),
    so following a chain stays inside the root. Every step of a symlink target must stay inside the root,
    because the root is renamed on publish: `../../content/x` would point outside the install folder.
  - Hardlinks, and symlinks on Windows, are materialized as copies (a symlink to a directory as a copy of
    the directory). A link is copied once its target no longer is, or contains, a link waiting to be
    copied, so archive order doesn't matter; links that never become ready are a link cycle and an error.
    On Windows, a symlink whose target isn't in the archive is skipped with a verbose log; a hardlink to a
    missing target is an error.
  - Other entry types (devices, FIFOs) are skipped with a verbose log.
- **Zip:** uses `System.IO.Compression`. On Unix hosts, if an entry has Unix mode bits
  (`ExternalAttributes >> 16`), the permission bits are applied. An entry whose file type bits mark a
  symlink (`0xA000`) is a symlink whose target is the entry's content (at most 4096 bytes), handled with
  the same rules as tar symlinks.
- On Unix, every **selected** file (and a raw download) also gets `u+x,g+x,o+x`. This covers zips
  created on Windows, which carry no modes.

### 8.6 HTTP

- One shared `HttpClient` over `SocketsHttpHandler`:
  - redirects followed (max 10)
  - system proxy honoured
  - `User-Agent: Cake.Download.Module/<version>`
  - `HttpClient.Timeout` infinite
- Requests use synchronous `HttpClient.Send` and stream reads, because `IPackageInstaller.Install` is
  synchronous; this avoids sync-over-async.
- **Stall timeout:** an attempt is aborted if no bytes arrive for 60 s. There is no total-time limit,
  because binaries can be large.
- **Retries:** 3 attempts with 1 s / 2 s / 4 s backoff (honouring `Retry-After`, capped at 30 s) on
  `HttpRequestException`, `IOException`, stall timeout, 408, 429 and 5xx. Other 4xx responses fail
  immediately.
- Only `https` URLs are accepted, including after placeholder expansion and for `checksums`. Redirect
  targets must also be `https`.
- No authentication headers are ever sent, and no interactive prompts ever happen.

## 9. Errors and logging

All failures are `CakeException`s with actionable messages:

| Situation | Message contains |
|---|---|
| Invalid directive | offending parameter, the directive, what is allowed |
| Missing integrity / missing RID entry / unpinned checksums | computed hash and the exact `&param=<hex>` to add |
| Hash mismatch | expected vs actual, URL, where the expectation came from |
| Checksums file hash mismatch | expected vs actual `checksums_sha256`, checksums URL |
| Asset not in checksums file | asset name, the names that *are* listed |
| HTTP 404 | expanded URL, RID, dialect, `{os}`/`{arch}` values, hint about `os.`/`arch.`/`url.<rid>` overrides |
| Other HTTP / network failure | URL, status or exception, attempts made |
| Unsupported platform | detected family/architecture, supported RIDs |
| Unsafe archive entry | entry name, rule violated |
| No files selected | include/exclude used, up to 50 entries of the install folder |

Logging through `ICakeLog`:
- **Information:** one line per actual download, e.g. `Downloading jq 1.8.2 (linux-x64) from <url>`.
- **Warning:** `sha256=skip`, on every fresh install.
- **Verbose:** each pipeline step, cache hits, retries.

## 10. Packaging and compatibility

- Package and assembly ID: `Cake.Download.Module`. Tags: `cake-module`, `cake`, `download`, `tool`,
  `github`, `release`, `binary`.
- `net8.0` (loads on the net8/9/10 hosts that Cake 6.x ships for).
- `Cake.Core` referenced with `PrivateAssets="all"` at the lowest supported 6.x version, so the module
  loads on Cake 6.0.0+. It is rebuilt for each Cake major.
- **Zero NuGet dependencies.** BCL only: `System.Net.Http`, `System.Text.Json`,
  `System.IO.Compression`, `System.Formats.Tar`, `System.Security.Cryptography`.
- XML documentation for the public types ships in the package.

## 11. Testing

### 11.1 Unit and component tests — `test/Cake.Download.Module.Tests`

xUnit v3 on Microsoft Testing Platform. No network access. File-system-heavy components use real,
per-test temporary directories, because atomic moves, file locks and Unix modes must be exercised on a
real disk.

| Unit | Covered |
|---|---|
| `DirectiveParser` | every parameter in §4.2; missing `package`/`version`; `latest`; unknown and duplicate parameters; every integrity combination; percent-decoding; template from `OriginalString` with `{}` intact; `url` vs template conflict |
| Dialects + `PlaceholderExpander` | table tests per dialect × supported RID; overrides; `url.<rid>`; `triple.<rid>`; unknown placeholder; unsupported platform |
| `ChecksumsFileParser` | GNU text and binary (`*`) formats, BSD format, CRLF, blank lines, uppercase hex, duplicates, missing entry |
| `IntegrityResolver` | each mode; checksums file mismatch; relative and absolute `checksums`; paste-ready messages |
| `HttpDownloader` (fake `HttpMessageHandler`) | redirect chains; https-only redirects; 404; retries on 5xx/429 then failure; `Retry-After`; stall timeout; hash computed while streaming |
| `ArchiveExtractor` | archives generated in-test (no binary fixtures): zip, tar, tar.gz; tar and zip Unix modes; zip-slip and absolute paths rejected; escaping symlinks/hardlinks rejected |
| `InstallStore` | cache hit makes no HTTP call; each marker field mismatch triggers reinstall; marker-less folder replaced; two parallel installs both succeed with one final folder; stale staging cleanup |
| `FileSelector` | default include for raw and archive; custom include/exclude; empty selection error; `+x` on selected files (Unix only) |
| `DownloadPackageInstaller` | `CanInstall` matrix; end-to-end with a fake handler serving a generated archive |

Platform-specific tests use `WindowsTheory`-style attributes (as in Cake.CycloneDX's
`Cake.Testing.Xunit.v3` helper project).

### 11.2 Runner tests — modelled on Cake.CycloneDX

A `RunnerTests` task in the `build/` Frosting project runs the **packed** module in every Cake runner:

- **`IRunner` implementations:**
  - `ScriptRunner`: installs `Cake.Tool` at the requested version into a private `--tool-path`.
  - `SdkRunner`: `dotnet run --file cake.cs` with `#:sdk Cake.Sdk@<version>`.
  - `FrostingRunner`: builds `tests/runners/frosting/Frosting.csproj` with
    `--property:CakeVersion=…;ModuleVersion=…;RestoreConfigFile=…`.
- **Cake version:** `--cake-version 6.0.0|6.*`, resolved against nuget.org's flat container.
- **Templates** in `tests/runners/{script,sdk,frosting}` are real, compilable files with a dummy version
  `0.0.0`. They are rendered by per-line regex replacement, which fails if a line is not found.
- **Drift guard:** everything after `// --- pipeline ---` must be identical in `script/build.cake` and
  `sdk/cake.cs`.
- **Isolation:**
  - a generated `nuget.config` (local artifacts feed + `packageSourceMapping` for
    `Cake.Download.Module`)
  - a per-run `NUGET_PACKAGES` folder
  - a per-run, initially empty tools directory
- **Scenario** (one directive per code path):

  | Tool | Exercises |
  |---|---|
  | jq | raw file, `go` dialect + `os.` override, `checksums` + `checksums_sha256` |
  | gh | `tar.gz` / `zip` per OS, `go` dialect, `archive.` override, default `include` inside a nested folder |
  | ripgrep | `rust` dialect, `{triple}` |
  | cyclonedx-cli | raw file, `dotnet` dialect, `sha256.<rid>` |

- **Pipeline:**
  - Each runner resolves every tool through `Context.Tools.Resolve(...)` and runs it with `--version`.
  - It then writes `out/report.json`: tool → path relative to the tools directory, SHA-256 of the
    resolved file, and the version output.
- **Assertions:**
  - `ReportAssertions` checks the expected tools, paths and version strings.
  - Reports are compared across runners after normalization, so all runners must produce
    byte-identical installs.
  - Each runner runs **twice**. The second run must leave every `.cake-download.json` unchanged
    (same `installedAt`), proving the cache hit.
  - Failures are collected into a summary table, which fails the task at the end.

### 11.3 Package verification

`PackageVerifier` (in `build/`, with tests in `tests/Build.Tests`) asserts that the nupkg:
- contains `lib/net8.0/Cake.Download.Module.dll` and its XML docs, README and icon
- carries the `cake-module` tag
- declares **no dependencies at all**

### 11.4 CI (GitHub Actions)

- `build` job: `windows-latest`, `ubuntu-latest`, `macos-latest` → `--target All`
  (build, unit tests, pack and verify).
- `runner-tests` job: the same three OSes × Cake `6.0.0` and `6.*` → `--target RunnerTests`.

## 12. Repository conventions

Follow Cake.CycloneDX (`../Cake.CycloneDX`):
- `build/` Frosting build project (`Build → Test → Pack → RunnerTests`, release tasks), `build.ps1` /
  `build.sh`
- `global.json` with the MTP test runner
- `src/Directory.Build.props/.targets`, central package management, StyleCop / analyzers
- `CHANGELOG.md` (Keep a Changelog) as the source of release notes
- `AGENTS.md`
- MinVer versioning
- `release.ps1` and the PR, main and release workflows

## 13. Future options (explicitly not v1)

- **Alias-based installation (approach 2).** An addin alias such as
  `DownloadTool(new DownloadToolSettings { … })` called from `Setup`. It is strongly typed with
  IntelliSense, but it runs at run time instead of install time, doesn't use `#tool`, and diverges from
  Cake's tool model and #2731. Rejected as the primary API.
  *Superseded by [`2026-10-04-download-tool-alias-design.md`](2026-10-04-download-tool-alias-design.md), which adds
  both as a complement to `#tool`, not a replacement.*
- **Typed directive builder (approach 3).** A public builder for Frosting/SDK users, e.g.
  `DownloadTool.For("jq").Version("1.8.2").From("…").WithChecksums("sha256sum.txt", "<hex>").ToUri()`,
  that produces the `download:` URI. The directive stays the single source of truth. It can be added
  later without breaking changes.
  *Superseded by [`2026-10-04-download-tool-alias-design.md`](2026-10-04-download-tool-alias-design.md), which adds
  both as a complement to `#tool`, not a replacement.*
- **`github:` scheme.** Shorter syntax, automatic integrity from the release API's `digest` field,
  asset auto-selection, private repos and GHES via a token. It would plug into the same pipeline through
  an internal resolver seam.
- **Global download cache.** For example `Download_CachePath` keyed by SHA-256, or following Cake's
  decision on #2830. The install root is a single injected path in `InstallStore`, so this stays a
  localized change. Needs real locking and eviction.
- **RID in the install folder name** (`jq.1.8.2.linux-x64`), to stop reinstall churn when Windows and
  WSL share one checkout.
- **More formats:** `tar.xz`, `tar.bz2`, `7z`, if they can be done without dependencies.
- **Dogfooding:** replace Cake.CycloneDX's hand-written `build/Tools/GitHubReleaseDownloader.cs` and
  `CycloneDxCliDownloader` (about 370 lines) with a single `download:` directive.
- Comment on cake-build/cake#2731 to offer the module as a candidate for core.

## 14. To verify at the start of implementation

1. **Cake SDK:** how a file-based `cake.cs` loads a *module* from `#:package` (Frosting needs
   `UseModule<>()`), and which API installs a tool from a URI. If the SDK needs a different mechanism,
   the design is revisited with the user before the SDK runner is built.
2. **Cake.Tool 6.0.0:** `#module nuget:?package=…` resolves from the generated `nuget.config` source
   (local feed), and the module is loaded before `#tool` directives are processed.
3. **Frosting:** `InstallTool` passes the configured tools path to `IPackageInstaller.Install`.
4. **Cake 6.0.0 vs 6.3.0 Cake.Core API:** the members the module uses (`IPackageInstaller`,
   `PackageReference`, `ICakePlatform`, `IGlobber`, `ICakeContainerRegistrar`) exist unchanged in 6.0.0.
