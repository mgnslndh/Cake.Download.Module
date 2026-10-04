using Cake.Core;
using Cake.Core.Configuration;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using Cake.Core.Tooling;

namespace Cake.Download.Module.Tests.Fakes;

internal sealed class TestCakeContext(ICakeEnvironment environment, ICakeLog log, ICakeConfiguration configuration) : ICakeContext
{
    public RecordingToolLocator Tools { get; } = new();

    public IFileSystem FileSystem { get; } = new FileSystem();

    public ICakeEnvironment Environment => environment;

    public IGlobber Globber => throw new NotSupportedException();

    public ICakeLog Log => log;

    public ICakeArguments Arguments => throw new NotSupportedException();

    public IProcessRunner ProcessRunner => throw new NotSupportedException();

    public IRegistry Registry => throw new NotSupportedException();

    public ICakeDataResolver Data => throw new NotSupportedException();

    public ICakeConfiguration Configuration => configuration;

    IToolLocator ICakeContext.Tools => Tools;
}
