using Cake.Common;
using Cake.Common.IO;
using Cake.Core;
using Cake.Core.IO;
using Cake.Frosting;

namespace Frosting;

public sealed class ScenarioContext : FrostingContext
{
    public ScenarioContext(ICakeContext context)
        : base(context)
    {
        Output = context.MakeAbsolute(new DirectoryPath(context.Argument<string>("output")));
    }

    public DirectoryPath Output { get; }
}
