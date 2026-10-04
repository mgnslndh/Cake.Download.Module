using System.Net;
using Cake.Core;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using Cake.Core.Packaging;
using Cake.Download.Module.Archives;
using Cake.Download.Module.Directives;
using Cake.Download.Module.Http;
using Cake.Download.Module.Installation;
using Cake.Download.Module.Integrity;
using Cake.Download.Module.Platforms;
using Path = System.IO.Path;

namespace Cake.Download.Module;

/// <summary>
/// Installs tools from <c>download:</c> package references: downloads a file over HTTPS, verifies its SHA-256,
/// extracts it when it is an archive and returns the files to register with Cake's tool locator.
/// </summary>
public sealed class DownloadPackageInstaller : IPackageInstaller
{
    private static readonly Lazy<HttpMessageHandler> SharedHandler = new(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    });

    private readonly ICakeEnvironment _environment;
    private readonly ICakeLog _log;
    private readonly IPlatformDetector _platformDetector;
    private readonly HttpDownloader _downloader;
    private readonly IntegrityResolver _integrity;
    private readonly FileSelector _fileSelector;
    private readonly TimeProvider _time;

    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadPackageInstaller"/> class.
    /// </summary>
    /// <param name="environment">The Cake environment.</param>
    /// <param name="fileSystem">The file system.</param>
    /// <param name="log">The log.</param>
    public DownloadPackageInstaller(ICakeEnvironment environment, IFileSystem fileSystem, ICakeLog log)
        : this(
            environment,
            fileSystem,
            log,
            new PlatformDetector(GetPlatform(environment)),
            SharedHandler.Value,
            DownloadOptions.Default,
            TimeProvider.System)
    {
    }

    internal DownloadPackageInstaller(
        ICakeEnvironment environment,
        IFileSystem fileSystem,
        ICakeLog log,
        IPlatformDetector platformDetector,
        HttpMessageHandler handler,
        DownloadOptions options,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _platformDetector = platformDetector ?? throw new ArgumentNullException(nameof(platformDetector));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _downloader = new HttpDownloader(handler, log, options);
        _integrity = new IntegrityResolver(_downloader);
        _fileSelector = new FileSelector(fileSystem);
    }

    /// <summary>
    /// Determines whether this installer handles the package: <c>download:</c> references of type tool.
    /// </summary>
    /// <param name="package">The package reference.</param>
    /// <param name="type">The package type.</param>
    /// <returns><see langword="true"/> for <c>download:</c> tools; otherwise <see langword="false"/>.</returns>
    public bool CanInstall(PackageReference package, PackageType type)
    {
        ArgumentNullException.ThrowIfNull(package);

        return type == PackageType.Tool && string.Equals(package.Scheme, DirectiveParser.Scheme, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Installs the tool into <c>&lt;path&gt;/&lt;package&gt;.&lt;version&gt;/</c>, unless an identical install is
    /// already there, and returns the files to register.
    /// </summary>
    /// <param name="package">The package reference.</param>
    /// <param name="type">The package type.</param>
    /// <param name="path">The tools directory.</param>
    /// <returns>The files to register with the tool locator.</returns>
    /// <exception cref="CakeException">The directive is invalid, or downloading, verifying or extracting failed.</exception>
    public IReadOnlyCollection<IFile> Install(PackageReference package, PackageType type, DirectoryPath path) =>
        Install(package, type, path, DirectiveSource.Directive);

    internal IReadOnlyCollection<IFile> Install(PackageReference package, PackageType type, DirectoryPath path, DirectiveSource source)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(path);

        var directive = DirectiveParser.Parse(package, source);
        var plan = DownloadPlanner.Create(directive, _platformDetector.Detect());
        var store = new InstallStore(path.MakeAbsolute(_environment).FullPath, _time);
        var installDirectory = store.GetInstallDirectory(plan);

        if (!store.IsCurrent(plan))
        {
            InstallFresh(plan, store, replaceCurrent: false);
        }
        else if (_fileSelector.Match(installDirectory, plan.Include, plan.Exclude).Count == 0)
        {
            _log.Verbose("{0} {1} ({2}) is installed in {3}, but no files match 'include'; reinstalling.", plan.Package, plan.Version, plan.Platform.Rid, installDirectory);
            InstallFresh(plan, store, replaceCurrent: true);
        }
        else
        {
            _log.Verbose("{0} {1} ({2}) is already installed in {3}.", plan.Package, plan.Version, plan.Platform.Rid, installDirectory);
        }

        var files = _fileSelector.Select(installDirectory, plan.Include, plan.Exclude);
        foreach (var file in files)
        {
            _log.Verbose("Registering {0}.", file.Path.FullPath);
        }

        return files;
    }

    private static ICakePlatform GetPlatform(ICakeEnvironment environment) =>
        environment?.Platform ?? throw new ArgumentNullException(nameof(environment));

    private static string NotFoundMessage(DownloadPlan plan)
    {
        var expanded = string.Join(", ", plan.Placeholders.Where(entry => entry.Key != "version").Select(entry => $"{{{entry.Key}}}='{entry.Value}'"));
        return $"{plan.Url.AbsoluteUri} was not found (HTTP 404). Detected platform {plan.Platform.Rid}; dialect '{plan.Dialect}' expanded {expanded}. " +
            Hints.NotFoundOverrides(plan.Source, plan.Platform.Rid);
    }

    private void InstallFresh(DownloadPlan plan, InstallStore store, bool replaceCurrent)
    {
        _log.Information("Downloading {0} {1} ({2}) from {3}", plan.Package, plan.Version, plan.Platform.Rid, plan.Url.AbsoluteUri);

        using var staging = store.CreateStaging(plan);

        ExpectedHash? expected;
        try
        {
            expected = _integrity.Resolve(plan, staging.DownloadDirectory);
        }
        catch (DownloadHttpException exception)
        {
            throw new CakeException(exception.Message, exception);
        }

        var assetPath = Path.Combine(staging.DownloadDirectory, "asset");

        DownloadResult download;
        try
        {
            download = _downloader.Download(plan.Url, assetPath);
        }
        catch (DownloadHttpException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new CakeException(NotFoundMessage(plan), exception);
        }
        catch (DownloadHttpException exception)
        {
            throw new CakeException(exception.Message, exception);
        }

        IntegrityResolver.Verify(plan, expected, download.Sha256);
        if (plan.Integrity is SkippedIntegrity)
        {
            _log.Warning(
                "Integrity verification is disabled for {0} {1} (sha256=skip). The downloaded file has SHA-256 {2}.",
                plan.Package,
                plan.Version,
                download.Sha256);
        }
        else
        {
            _log.Verbose("Verified SHA-256 {0} of {1}.", download.Sha256, plan.AssetName);
        }

        try
        {
            if (plan.Format == ArchiveFormat.File)
            {
                var target = Path.Combine(staging.ContentDirectory, plan.FileName!);
                var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                var targetDirectory = Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(Path.GetFullPath(target)) ?? string.Empty);
                var contentDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(staging.ContentDirectory));
                if (!string.Equals(targetDirectory, contentDirectory, comparison))
                {
                    throw new CakeException(
                        $"The file name '{plan.FileName}' for {plan.Package} {plan.Version} would place the download outside the install folder.");
                }

                File.Move(assetPath, target);
                FileSelector.MakeExecutable(target);
            }
            else
            {
                ArchiveExtractor.Extract(assetPath, plan.Format, staging.ContentDirectory, _log);
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            throw new CakeException(
                $"Could not extract {plan.Package} {plan.Version} from {plan.Url.AbsoluteUri} as {ArchiveFormats.ToName(plan.Format)}: {exception.Message}",
                exception);
        }

        store.Publish(plan, staging, store.CreateMarker(plan, download.Sha256), replaceCurrent);
        _log.Verbose("Installed {0} {1} into {2}.", plan.Package, plan.Version, store.GetInstallDirectory(plan));
    }
}
