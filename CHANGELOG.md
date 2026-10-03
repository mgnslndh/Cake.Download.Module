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
