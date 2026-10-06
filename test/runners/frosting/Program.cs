using Cake.Download.Module;
using Cake.Frosting;

// Cake Frosting runner test. RunnerTestsTask builds this project against the packed module and runs it.
return new CakeHost()
    .UseContext<Frosting.ScenarioContext>()
    .UseModule<DownloadModule>()
    .InstallTool(new Uri("download:https://github.com/cli/cli/releases/download/v{version}/gh_{version}_{os}_{arch}.{archive}?package=gh&version=2.62.0&os.darwin=macOS&archive.darwin=zip&checksums=gh_{version}_checksums.txt&checksums_sha256=89dc6f5225aa0c70d6f90950f5246225afff2f423f3d242f7d8813fe9993af4d"))
    .InstallTool(new Uri("download:https://github.com/BurntSushi/ripgrep/releases/download/{version}/ripgrep-{version}-{triple}.{archive}?package=rg&version=14.1.1&dialect=rust&triple.linux-arm64=aarch64-unknown-linux-gnu&sha256.win-x64=d0f534024c42afd6cb4d38907c25cd2b249b79bbe6cc1dbee8e3e37c2b6e25a1&sha256.linux-x64=4cf9f2741e6c465ffdb7c26f38056a59e2a2544b51f7cc128ef28337eeae4d8e&sha256.linux-arm64=c827481c4ff4ea10c9dc7a4022c8de5db34a5737cb74484d62eb94a95841ab2f&sha256.osx-x64=fc87e78f7cb3fea12d69072e7ef3b21509754717b746368fd40d88963630e2b3&sha256.osx-arm64=24ad76777745fbff131c8fbc466742b011f925bfa4fffa2ded6def23b5b937be"))
    .Run(args);
