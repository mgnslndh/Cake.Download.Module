using Cake.Download.Module;
using Cake.Frosting;

namespace Frosting;

[TaskName("OnDemand")]
public sealed class OnDemandTask : FrostingTask<ScenarioContext>
{
    public override void Run(ScenarioContext context)
    {
        context.DownloadTool("download:https://github.com/jqlang/jq/releases/download/jq-{version}/jq-{os}-{arch}{exe}?package=jq&version=1.8.2&os.darwin=macos&checksums=sha256sum.txt&checksums_sha256=dc86824a41c165ece971ff691aff6e08bbfe6e1d1f531688b47ee78c283a85cd");
    }
}
