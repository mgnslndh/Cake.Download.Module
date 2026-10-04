# Cake.Download.Module

A [Cake](https://cakebuild.net) module that installs command-line tools that are not on NuGet, such as GitHub
release assets or any file on an HTTPS server, with the `download:` scheme. It downloads the file for the current
platform, verifies its SHA-256, extracts it if it is an archive, and registers the tool so Cake's aliases and
`Context.Tools.Resolve(...)` find it.

## Install

Cake .NET Tool (`build.cake`):

```csharp
#module nuget:?package=Cake.Download.Module&version=0.1.0-preview.1
#tool "download:https://github.com/jqlang/jq/releases/download/jq-{version}/jq-{os}-{arch}{exe}?package=jq&version=1.8.2&os.darwin=macos&checksums=sha256sum.txt&checksums_sha256=dc86824a41c165ece971ff691aff6e08bbfe6e1d1f531688b47ee78c283a85cd"
```

Cake SDK (`cake.cs`):

```csharp
#:sdk Cake.Sdk@6.3.0
#:package Cake.Download.Module@0.1.0-preview.1

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

Parameter values are percent-decoded, so a value containing `=`, `&`, `?` or `#` (notably `url=`) must be
percent-encoded; otherwise Cake reports "Could not parse query string.". `package` and `version` must start with a
letter or digit. Placeholders are expanded in the URL, `url`, `url.<rid>`, `checksums`, `filename`, `include` and
`exclude`. Validation is strict: unknown parameters are errors.

### Placeholders and dialects

| Placeholder | go | dotnet | rust |
|---|---|---|---|
| `{os}` | `windows` / `linux` / `darwin` | `win` / `linux` / `osx` | `windows` / `linux` / `darwin` |
| `{arch}` | `amd64` / `arm64` / `386` / `arm` | `x64` / `arm64` / `x86` / `arm` | `x86_64` / `aarch64` / `i686` / `armv7` |
| `{triple}` | – | – | e.g. `x86_64-pc-windows-msvc`, `aarch64-apple-darwin`, `x86_64-unknown-linux-musl` |

`{version}`, `{rid}` (`win-x64`, `linux-arm64`, `osx-arm64`, …), `{exe}` (`.exe` on Windows) and `{archive}` (`zip` on
Windows, `tar.gz` elsewhere) work in every dialect. Supported platforms: `win-x64`, `win-x86`, `win-arm64`,
`linux-x64`, `linux-arm64`, `linux-arm`, `osx-x64`, `osx-arm64`.

In the rust dialect, `{triple}` on Linux defaults to musl (`x86_64-unknown-linux-musl`); override it per platform
with `triple.<rid>=`.

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

## Installing a tool only when a task runs

`#tool`, Frosting's `CakeHost.InstallTool` and a top-level Cake.Sdk `InstallTool` install every tool before the
target runs, on every run: other targets, `--dryrun` and `--tree` included. For a large tool that only one task needs,
call the `DownloadTool` alias inside that task instead. It installs and registers the tool when the task runs, and
later runs with an unchanged tool make no network requests.

Cake .NET Tool (`build.cake`): load the package as an addin (keep `#module` too if you also use `#tool "download:…"`):

```csharp
#addin nuget:?package=Cake.Download.Module&version=0.1.0-preview.1

Task("Report").Does(() =>
{
    DownloadTool("download:https://github.com/jqlang/jq/releases/download/jq-{version}/jq-{os}-{arch}{exe}?package=jq&version=1.8.2&os.darwin=macos&checksums=sha256sum.txt&checksums_sha256=dc86824a41c165ece971ff691aff6e08bbfe6e1d1f531688b47ee78c283a85cd");
    StartProcess(Context.Tools.Resolve(IsRunningOnWindows() ? "jq.exe" : "jq"), "--version");
});
```

Cake SDK (`cake.cs`): the same call inside `.Does(...)`. Cake.Sdk's own `InstallTool(...)` also installs immediately
when it is called inside a task.

Cake Frosting: `context.DownloadTool(...)` in the task's `Run`, with `using Cake.Download.Module;`. `UseModule` is
not needed for the alias. Injecting `Cake.Frosting.IToolInstaller` into the task and calling
`Install(new PackageReference("download:…"))` also works, but the installer for `download:` comes from the module, so
that needs `UseModule<DownloadModule>()`.

`DownloadTool` returns the registered files, but Cake's tool aliases and `Context.Tools.Resolve(...)` find the tool
without them.

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
was downloaded. For a checksums file, set the `ChecksumsFile` property alone (for example
`new DownloadToolSettings { ChecksumsFile = "sha256sum.txt" }`), and the error message gives the `.WithChecksums(…)`
call with the checksums file's hash. Validation is the directive's: settings are written out as a directive and parsed.

`ToDirective()` and `ToDirectiveUri()` turn settings into a directive for installs that run up front, e.g.
`InstallTool(settings.ToDirective())` in Cake.Sdk or `.InstallTool(settings.ToDirectiveUri())` in Frosting.

## Limitations

Only public HTTPS downloads: no authentication, private repositories, mirrors or signature verification. Archive
formats are zip, tar and tar.gz. Authenticated (NTLM) proxies are not supported. On Windows, symbolic links to
directories inside tar archives are not materialized.

## License

MIT
