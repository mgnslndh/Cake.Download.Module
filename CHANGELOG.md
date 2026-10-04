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
- `package` and `version` must start with a letter or digit, so values such as `.` or `..` are rejected.
- Idempotent installs into `<tools>/<package>.<version>/`: unchanged directives make no network requests.

### Fixed

- `filename` values that would place the download outside the install folder (e.g. `C:evil.exe` on Windows) are now rejected.
  `filename` must now start with a letter or digit and contain only letters, digits, `.`, `_`, `+` and `-`, so names with
  spaces or colons are rejected. Windows reserved device names (`CON`, `PRN`, `AUX`, `NUL`, `COM0`-`COM9`, `LPT0`-`LPT9`, and their superscript variants `COM¹`–`COM³`, `LPT¹`–`LPT³`,
  with or without an extension) are rejected for `filename` and `package`.
- Tar archives with symlink chains (e.g. `libfoo.so -> libfoo.so.1 -> libfoo.so.1.2.3`) no longer fail to extract.
- On Windows, a tar symlink to a directory is now extracted as a copy of the directory instead of being dropped, and a
  symlink whose target isn't in the archive is logged (verbose) instead of being dropped silently.
- Symlinks in zip archives are now extracted as symlinks (copies on Windows) instead of as small text files that
  contain the link target.
- Archive symlinks whose target leaves the extraction folder and comes back by its name (e.g. `../../content/x`) are now
  rejected; after install they would have pointed outside the install folder.
