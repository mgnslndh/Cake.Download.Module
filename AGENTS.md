# Agent Instructions

## Cake Module Guidelines

This is a Cake **module**: it is loaded with `#module`, `UseModule<DownloadModule>()` or `#:package`, and Cake never
installs a module's NuGet dependencies.

- **No runtime dependencies:** only BCL APIs. `PackageVerifier` fails the Pack target if the nupkg declares any
  dependency.
- **Cake references:** `Cake.Core` with `PrivateAssets="all"`, at the lowest compatible version (6.0.0). Raise it only
  when a newer Cake API is required, and say why in the commit message.
- **Target frameworks:** `net8.0`, `net9.0` and `net10.0`.
- **Public API:** only `DownloadModule`, `DownloadPackageInstaller`, `DownloadToolAliases`, `DownloadToolSettings`,
  `DownloadDialect` and `DownloadFormat` are public. Everything else is `internal`, tested through
  `InternalsVisibleTo`.
- **Design:** read `docs/superpowers/specs/2026-10-02-cake-download-module-design.md` before changing the directive
  grammar, the integrity model or the install layout, and `docs/superpowers/specs/2026-10-04-download-tool-alias-design.md`
  before changing the `DownloadTool` alias or `DownloadToolSettings`.
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
