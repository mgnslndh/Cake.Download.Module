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
