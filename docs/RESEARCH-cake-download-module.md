# Cake module for installing tools from GitHub Releases and arbitrary HTTPS URLs — research & design input

> Handoff document. Written 2026-09-25 from research against a fork of `cake-build/cake`
> (fork at `4ce8f4c5`, upstream `develop` is ~222 commits ahead; upstream latest release **v6.3.0**).
> The extension points described below were checked against upstream `develop` and are unchanged.
> Intended as input for a new session in a clean repo that builds the module.

---

## 1. Goal

Let a Cake build (script via `Cake.Tool`, Frosting, or Cake SDK) declare a CLI tool that is **not on NuGet**
— typically a GitHub Release asset — and have Cake download, verify, extract and register it so that
existing alias tool runners (`Tool<TSettings>` subclasses) resolve it automatically.

```csharp
#module nuget:?package=Cake.Download.Module&version=1.0.0
#tool "github:?package=cli/cli&version=v2.62.0&asset=gh_{version}_{os}_{arch}.{archive}&include=**/bin/gh{exe}"
#tool "download:https://example.com/tool-1.2.3-{os}-{arch}.zip?package=tool&version=1.2.3&sha256.win-x64=...&sha256.linux-x64=..."
```

(Syntax is a sketch — see §8 open questions.)

---

## 2. How Cake installs tools (the extension point)

All paths below are in upstream `cake-build/cake`, `src/`.

| Piece | File | What matters |
|---|---|---|
| `IPackageInstaller` | `Cake.Core/Packaging/IPackageInstaller.cs` | `bool CanInstall(PackageReference, PackageType)` + `IReadOnlyCollection<IFile> Install(PackageReference, PackageType, DirectoryPath)`. The **only** interface we implement. |
| `PackageReference` | `Cake.Core/Packaging/PackageReference.cs` | Parsed from the directive URI. Exposes `Scheme`, `Address`, `Parameters` (multi-valued query dict), `Package`. **Throws if `package=` query parameter is missing.** |
| Script pipeline | `Cake.Core/Scripting/ScriptProcessor.cs` (`InstallPackages`) | Picks the first installer whose `CanInstall` is true, calls `Install`, throws `CakeException` if the result is empty, then **calls `IToolLocator.RegisterFile(file.Path)` for every returned file**. |
| Frosting pipeline | `Cake.Frosting/Internal/ToolInstaller.cs` | Same contract, used by `CakeHost.InstallTool(Uri)`. Modules added via `CakeHost.UseModule<TModule>()`. |
| Tool resolution | `Cake.Core/Tooling/ToolRepository.cs`, `ToolResolutionStrategy.cs` | Registered files are indexed **by file name**; `Tool<T>.GetToolExecutableNames()` hits registrations first, then `./tools/**`, then `PATH`. ⇒ Returning the right executable file(s) from `Install` is all it takes for an alias to find the tool. |
| Version warning | `ScriptProcessor.CheckPackageVersion` | Warns (reproducible-builds nag) if there is no `version` query parameter. ⇒ Always accept/require `version=`. |
| Reference installers | `Cake.DotNetTool.Module/DotNetToolPackageInstaller.cs`, `Cake.NuGet/Installers/InProcessInstaller.cs` | Idempotency pattern: install to a per-package/version folder, skip if it exists. |
| Existing helpers | `Cake.Core/IO/IFile.SetUnixFileMode(UnixFileMode)`; `Cake.Common/IO/Zipper` | Exec bit can be set through Cake's FS abstraction. Cake's zip support is zip-only and loses Unix permissions (issue #2592) — the module should do its own extraction (`System.IO.Compression` + `System.Formats.Tar`). |

**`PackageReference` parsing trick:** the NuGet installer uses `nuget:https://feed/?package=X`, where
`Address` becomes `https://feed/`. The same works for us:
`download:https://host/path/tool.zip?package=tool` → `Scheme="download"`, `Address=https://host/path/tool.zip`.
Limitation: the download URL itself cannot carry its own query string (it collides with Cake parameters);
provide an escape hatch such as `url=<url-encoded>`.
**Do not** try to use `https:` as the scheme: `Address` is built from `uri.AbsolutePath`, which for
`https://host/x.zip` yields `/x.zip` (treated as an absolute *file path* on Unix).

**Tool file discovery gotcha:** `NuGetContentResolver` only returns `**/*.exe` and `**/*.dll` by default;
Unix executables without an extension are missed unless `include=` is given. Our installer must decide
what to register itself (an `include` glob, plus sensible defaults: files matching `{package-name}{exe}`,
or all files with the exec bit set in tar archives).

---

## 3. Module loading constraints (these decide the packaging design)

Checked in `Cake/Infrastructure/Composition/ModuleSearcher.cs`, `Cake.Core/Polyfill/AssemblyHelper.cs`,
`Cake.NuGet/Installers/InProcessInstaller.cs`, `Cake.Core/Reflection/AssemblyVerifier.cs`.

1. **Module assembly file name must match `Cake.*.Module.dll`**, and the assembly must carry
   `[assembly: CakeModule(typeof(MyModule))]` where `MyModule : ICakeModule`.
2. **NuGet dependencies of a `#module` package are NOT installed.**
   `InProcessInstaller.GetDependencyBehavior` returns `DependencyBehavior.Ignore` for everything except
   addins (and addins only with `loaddependencies=true`). ⇒ With `Cake.Tool` scripts, anything the module
   needs besides Cake.Core and the BCL must be **physically inside the module's nupkg `lib/<tfm>/`**.
   Frosting / Cake SDK consume modules as normal `PackageReference`s, so NuGet deps flow there —
   but the module must work in the scripting case, so design as if deps don't exist.
3. **Modules load with `Assembly.LoadFrom` into the default load context.** A sibling DLL in the same
   folder is resolved by probing, so bundling works. But the default context holds **one version per
   assembly name**: if two modules ship the same shared DLL in different versions, the first one loaded
   wins → `MissingMethodException` / `TypeLoadException` for the other.
4. **Cake.Core version check:** a referenced Cake.Core older than `LatestPotentialBreakingChange`
   gives a warning; older than `LatestBreakingChange` (0.26.0) is an error. On upstream `develop` this is
   `7.0.0` (next major). ⇒ Build against the current released Cake.Core (6.x), `PrivateAssets="all"`,
   and plan to rebuild for each Cake major.
5. **Target framework:** Cake 6.x/`develop` builds `net10.0;net11.0`. cake-contrib modules (e.g.
   `Cake.Npm.Module`) target `net8.0` with `<PackageReference Include="Cake.Core" Version="$(CakeVersion)" PrivateAssets="all" />`.
   A `net8.0` module loads on newer runtimes; multi-target if we need newer APIs.
6. **Precedent:** `Cake.Chocolatey.Module`, `Cake.Npm.Module`, `Cake.DNF.Module`,
   `Cake.DotNetLocalTools.Module`, `Cake.Paket.Module` (all cake-contrib) each implement one
   `IPackageInstaller` for one scheme. `Cake.DotNetTool.Module` was eventually merged into Cake core.

---

## 4. Prior discussion in the Cake project

- **cake-build/cake#2731** — *"Feature request: built-in scheme for `#tool` to download an arbitrary HTTPS .zip file"*
  (open since 2020, labels **Up-for-grabs**, **Help wanted**, milestone **Next Major Candidate**).
  - Proposal (jnm2): `download:` scheme, **required SHA256 checksum**, no version, format auto-detected.
  - Maintainer (devlead): *"best implemented and tested as a Cake module"* first.
  - gep13: see `Cake.UrlLoadDirective.Module` as a reference.
  - Nobody picked it up. ⇒ **Comment on #2731 before/when starting** so the module can later be a candidate for core.
- Related: #2830 (install tools to a global packages folder — cache location), #3263 (`.cake` folder instead of `tools`).
- Nothing found in issues/discussions about GitHub Releases specifically.

---

## 5. dotnet-file evaluation (devlooped/dotnet-file, v1.7.7)

**Verdict: cannot solve the problem, but has patterns worth borrowing.**

Verified by running it:

| Command | Result |
|---|---|
| `dotnet file add https://github.com/jqlang/jq/releases/download/jq-1.7.1/jq-windows-amd64.exe tools/` | ❌ falls into `gh` CLI fallback, nothing downloaded |
| `dotnet file add https://aka.ms/dotnet-install.ps1 ...` | ❌ `302: Moved Temporarily` |
| `dotnet file add https://dot.net/v1/dotnet-install.sh ...` | ❌ `301: Moved Permanently` |

Why (from source):
- `GitHubRawHandler` rewrites **every** `github.com/{owner}/{repo}/...` URL to `raw.githubusercontent.com` → 404 for release assets.
- On 404 it shells out to `gh api .../contents`; for `/releases/...` URLs it would enumerate the **repo root** (downloads source, not the asset). *(code-read, not executed)*
- `HttpClientHandler { AllowAutoRedirect = false }` — GitHub release downloads always 302 to `release-assets.githubusercontent.com`.
- No archive extraction, no exec bit, no checksum verification, no OS/arch asset selection.
- Auth handler may invoke Git Credential Manager interactively on 403/404 — can hang CI.
- Its model (`.netconfig` with URL+ETag committed to the repo) targets vendoring source files, not transient build tools.

**Patterns to borrow:**
1. **`DelegatingHandler` pipeline** — one concern per handler: per-host URL rewriting, per-host auth.
   For us: `GitHubAssetHandler` (browser URL → API asset endpoint when a token is present, `Accept: application/octet-stream`),
   `GitHubAuthHandler` (bearer token only for `api.github.com` / GHES host). .NET strips `Authorization` on cross-host redirects.
2. **Credential cascade with graceful fallback** — repo → owner → global → anonymous. For us:
   `GITHUB_TOKEN` / `GH_TOKEN` env → Cake config (`--github_token` / `CAKE_GITHUB_TOKEN`) → optional `gh auth token` → anonymous. **Never prompt.**
3. **Atomic writes** — download to temp, then `File.Move(overwrite: true)`. For us: download → verify → extract into temp dir → rename to final dir.
4. **Persisted provenance** (url, etag, sha) — for us a marker file (e.g. `.cake-download.json`) in each install dir for idempotency and change detection.
5. **Conditional requests** (HEAD / `If-None-Match` with stored ETag) — only relevant for unpinned URLs.
6. **Accept browser URLs** and normalise at request time (e.g. `https://github.com/o/r/releases/tag/v1`).

**Anti-patterns to avoid:** disabling redirects, blanket rewriting of all GitHub URLs, shelling out to `gh` as a fallback,
opening a browser, a short global `HttpClient.Timeout` (binaries can be large).

---

## 6. GitHub Releases facts (verified 2026-09-25)

- `https://github.com/{o}/{r}/releases/download/{tag}/{asset}` → `302` to a signed, short-lived
  `release-assets.githubusercontent.com/...` URL. Plain HTTPS + redirects works for **public** repos with no API call.
- `/releases/latest/download/{asset}` exists but is not reproducible — discourage (warn like the version check).
- **Private repos / GHES:** must use `GET /repos/{o}/{r}/releases/assets/{id}` with `Accept: application/octet-stream` + token.
- **Asset digests:** release API assets carry `digest: "sha256:..."` for recently uploaded assets
  (e.g. `cli/cli` v2.101.0: every asset has a digest, release `immutable: true`).
  Older assets return `digest: null` (e.g. `jqlang/jq` jq-1.7.1) → fall back to a checksums asset
  (`*checksums*.txt`, `SHA256SUMS`, `sha256sum.txt`) or a user-supplied `sha256`.
- **Rate limits:** API 60 req/h unauthenticated (per IP — shared CI runners hit this), 5000 with a token.
  ⇒ Minimise API calls; cache resolved metadata in the marker file; only call the API when needed
  (asset listing for pattern matching, digest lookup, private repos).
- Asset naming is inconsistent across projects (`windows`/`win`/`pc-windows-msvc`, `amd64`/`x86_64`/`x64`,
  `darwin`/`macos`/`apple-darwin`, `.zip`/`.tar.gz`/bare binary) ⇒ placeholders need per-project mapping or pattern matching.

---

## 7. Design recommendation

### 7.1 Two schemes, one layered pipeline

```
github:  ──► GitHub resolver ──┐   (owner/repo + version + asset pattern → concrete URL, expected digest, auth header)
                               ▼
download: ─────────────────► Download pipeline: fetch → verify sha256 → extract (zip / tar.gz / raw) → chmod +x
                                                → atomic move to tools/<package>.<version>/ → pick files → return IFiles
```

| | `download:` (generic HTTPS) | `github:` (Releases) |
|---|---|---|
| Public GitHub release assets | ✅ (redirects followed) | ✅ |
| Non-GitHub hosts | ✅ | ❌ |
| Private repos / GHES | ❌ | ✅ (token + API asset endpoint) |
| Integrity | user-supplied sha256 (per platform) | automatic via API `digest`, fallback checksums asset / user sha256 |
| Platform selection | URL placeholders | pattern match over asset list |
| API / rate-limit dependency | none | yes |

Both are needed: `download:` covers the long tail (vendor sites, Artifactory, blob storage) and closes #2731;
`github:` adds private repos, automatic integrity and asset selection.

### 7.2 Packaging: ONE module package, two schemes (recommended)

Options considered:

| Option | Verdict |
|---|---|
| **A. One package `Cake.Download.Module`, registering two `IPackageInstaller`s (`download:`, `github:`) over a shared internal pipeline** | ✅ **Recommended.** No cross-module coupling, one `#module` line, one version, shared `HttpClient`/cache/locking. The GitHub part is ~a few hundred lines with no extra deps (raw `HttpClient` + `System.Text.Json`) — no reason to split. |
| B. Two modules; `github:` module builds a `download:` `PackageReference` and delegates to whichever installer handles it at runtime | ❌ Stringly-typed runtime contract, no version enforcement, user must remember both `#module` lines (deps aren't installed, §3.2), and injecting `IEnumerable<IPackageInstaller>` into an installer risks a DI cycle. |
| C. Two modules + shared `Cake.Download.Core` library bundled into each nupkg | ⚠️ Works mechanically (LoadFrom probing), but same-name assemblies in the default load context → first-loaded wins → version skew breaks the other module (§3.3). Both modules would also register the same shared services (last wins). Only worth it if a third party wants to add resolvers (GitLab, Gitea, Azure Artifacts…). |
| D. Module B references module A's assembly directly | ⚠️ Possible only because all modules are loaded before registration; relies on load-order/probing details, not a supported contract. Avoid. |

**Extensibility without splitting:** keep an internal seam — e.g. `IAssetResolver` (`github:` today; `gitlab:`,
`gitea:` later) feeding a single `IDownloadPipeline`. Keep it `internal` until there's real demand; if it's
ever made public, do it by merging into Cake core (where #2731 points) rather than a cross-module contract.

**Hard rule from §3.2:** zero third-party runtime dependencies (no Octokit, no Newtonsoft). If something is
unavoidable, it must be bundled in the nupkg `lib/` folder and given a unique assembly name.

### 7.3 Package identity

Constraints: package ID & assembly `Cake.*.Module`; checked availability on nuget.org 2026-09-25.

| Candidate | Available | Notes |
|---|---|---|
| **`Cake.Download.Module`** | ✅ | **Recommended.** Mirrors the `download:` scheme; "download" also covers `github:`. Same naming style as `Cake.Npm.Module` / `Cake.Chocolatey.Module` (named after the source). |
| `Cake.RemoteTool.Module` | ✅ | Most descriptive of purpose; runner-up. |
| `Cake.ToolDownload.Module` / `Cake.DownloadTool.Module` | ✅ | Clear but clunky. |
| `Cake.BinaryTool.Module` | ✅ | Implies binaries only (scripts/jars are valid too). |
| `Cake.GitHubRelease(s).Module` | ✅ | Most discoverable for GitHub, but misrepresents the generic `download:` part. |
| `Cake.GitHub.Module` | ✅ | Confusable with the existing `Cake.GitHub` addin (taken). Avoid. |
| `Cake.Http.Module`, `Cake.Fetch.Module` | ✅ | Too generic / unclear. |

Put "GitHub", "release", "download", "tool", "binary" in `PackageTags` and description for discoverability,
plus the `cake-module` tag used by cake-contrib. Consider hosting under `cake-contrib` later (needs their agreement).

---

## 8. Open design questions (for the brainstorming/design session)

1. **Directive grammar.** Stick with `PackageReference` query parameters (works today, no Cake changes) vs.
   jnm2's comma syntax in #2731 (needs a Cake parser change). Recommendation: query params.
2. **Per-platform checksums in a single directive** — `sha256.win-x64=...&sha256.linux-x64=...`? a checksums-file URL (`checksums=`)?
   Required for `download:`? (#2731 proposed required; suggestion: required for `http:`, strongly recommended/warn for `https:`,
   and when missing print the computed hash so users can paste it.)
3. **Placeholders & mapping:** `{version}`, `{os}`, `{arch}`, `{rid}`, `{exe}`, `{archive}`; per-directive overrides like
   `os.windows=pc-windows-msvc&arch.x64=x86_64`? For `github:`, glob/regex matching against the asset list may be simpler.
4. **What to register:** `include=` glob; default = `**/{package-name}{exe}` + exec-bit files in tar; strip-top-level-folder option?
5. **Install location & cache:** `tools/<package>.<version>/` (consistent with NuGet) vs. a user-level cache (#2830).
   Marker file contents (url, resolved asset id, sha256, etag, installed-at).
6. **Concurrency:** file lock around install dir (parallel builds on same agent).
7. **GHES support:** `baseurl=` parameter / config key.
8. **Token config names:** `GITHUB_TOKEN`, `GH_TOKEN`, `CAKE_GITHUB_TOKEN`, Cake config `[GitHub] Token`?
9. **Archive formats in v1:** zip, tar.gz, raw binary; later tar.xz / tar.bz2 / 7z?
10. **`latest` / version ranges:** allow with warning, or refuse?
11. **Offline / air-gapped:** honour a mirror base URL config (`CAKE_DOWNLOAD_MIRROR`)?
12. **Signature / attestation verification** (GitHub artifact attestations, cosign) — out of scope for v1?

---

## 9. Suggested MVP scope & test plan

**MVP:**
- `download:` scheme: HTTPS with redirects, `version=`, `sha256=` (optionally per-RID), zip / tar.gz / raw file,
  exec bit on Unix, `include=`, idempotent versioned install dir with marker file, atomic install.
- `github:` scheme: public repos, `package=owner/repo`, `version=<tag>`, `asset=` pattern with placeholders,
  digest verification from the API when present, token from env if present.
- Works in Cake.Tool (`#module` + `#tool`), Frosting (`UseModule<>()` + `InstallTool(new Uri(...))`) and Cake SDK.

**Tests:**
- Unit: `PackageReference` → options parsing; placeholder expansion per OS/arch; asset matching; checksum verification;
  archive extraction incl. tar exec bits and path-traversal ("zip slip") protection; idempotency via marker.
- HTTP pipeline via fake `HttpMessageHandler` (redirects, 404, rate-limit 403 with `x-ratelimit-remaining: 0`, cross-host auth stripping).
- Integration (opt-in / CI only): real downloads of small, stable assets, e.g. `jqlang/jq` jq-1.7.1 (no digest → checksum fallback)
  and `cli/cli` recent tag (digest present); run on Windows, Linux, macOS; one end-to-end `.cake` script calling a tool alias
  (or `StartProcess`) with the downloaded tool.

---

## 10. References

- Cake: https://github.com/cake-build/cake — modules docs: https://cakebuild.net/docs/fundamentals/modules
- Issue #2731: https://github.com/cake-build/cake/issues/2731
- Example modules: https://github.com/cake-contrib/Cake.Npm.Module, https://github.com/cake-contrib/Cake.Chocolatey.Module,
  https://github.com/cake-contrib/Cake.DNF.Module, https://github.com/cake-contrib/Cake.DotNetLocalTools.Module,
  https://github.com/cake-contrib/Cake.UrlLoadDirective.Module
- dotnet-file: https://github.com/devlooped/dotnet-file (see `src/File/Http/*.cs`, `AddCommand.cs`, `GitHub.cs`)
- GitHub REST — releases & assets: https://docs.github.com/en/rest/releases/assets
