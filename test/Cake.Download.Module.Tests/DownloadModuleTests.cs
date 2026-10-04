using System.Reflection;
using Cake.Core.Annotations;
using Cake.Core.Packaging;
using Cake.Download.Module.Tests.Fakes;

namespace Cake.Download.Module.Tests;

public sealed class DownloadModuleTests
{
    [Fact]
    public void Assembly_Declares_The_Module()
    {
        var attribute = typeof(DownloadModule).Assembly.GetCustomAttribute<CakeModuleAttribute>();

        Assert.Equal(typeof(DownloadModule), attribute?.ModuleType);
    }

    [Fact]
    public void Register_Adds_The_Installer_As_A_Singleton_Package_Installer()
    {
        var registrar = new RecordingRegistrar();

        new DownloadModule().Register(registrar);

        var registration = Assert.Single(registrar.Registrations);
        Assert.Equal(typeof(DownloadPackageInstaller), registration.Implementation);
        Assert.Equal([typeof(IPackageInstaller)], registration.Services);
        Assert.True(registration.IsSingleton);
    }
}
