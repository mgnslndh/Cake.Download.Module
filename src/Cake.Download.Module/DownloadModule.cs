using Cake.Core.Annotations;
using Cake.Core.Composition;
using Cake.Core.Packaging;
using Cake.Download.Module;

[assembly: CakeModule(typeof(DownloadModule))]

namespace Cake.Download.Module;

/// <summary>
/// The Cake module that adds the <c>download:</c> scheme for <c>#tool</c> and <c>InstallTool</c>.
/// </summary>
public sealed class DownloadModule : ICakeModule
{
    /// <summary>
    /// Registers <see cref="DownloadPackageInstaller"/> as a package installer.
    /// </summary>
    /// <param name="registrar">The container registrar.</param>
    public void Register(ICakeContainerRegistrar registrar)
    {
        ArgumentNullException.ThrowIfNull(registrar);

        registrar.RegisterType<DownloadPackageInstaller>().As<IPackageInstaller>().Singleton();
    }
}
