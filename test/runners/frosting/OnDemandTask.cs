using Cake.Download.Module;
using Cake.Frosting;

namespace Frosting;

[TaskName("OnDemand")]
public sealed class OnDemandTask : FrostingTask<ScenarioContext>
{
    public override void Run(ScenarioContext context)
    {
        context.DownloadTool(new Uri("download:https://github.com/jqlang/jq/releases/download/jq-{version}/jq-{os}-{arch}{exe}?package=jq&version=1.8.2&os.darwin=macos&checksums=sha256sum.txt&checksums_sha256=dc86824a41c165ece971ff691aff6e08bbfe6e1d1f531688b47ee78c283a85cd"));
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
}
