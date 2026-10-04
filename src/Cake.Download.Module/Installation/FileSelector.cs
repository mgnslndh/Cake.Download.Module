using Cake.Core;
using Cake.Core.IO;

namespace Cake.Download.Module.Installation;

/// <summary>
/// Chooses the files of an install folder that are registered with Cake's tool locator.
/// </summary>
internal sealed class FileSelector
{
    public const string MarkerFileName = ".cake-download.json";

    private const int ListingLimit = 50;

    private const UnixFileMode ExecuteBits = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

    private readonly IFileSystem _fileSystem;

    public FileSelector(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var mode = File.GetUnixFileMode(path);
        if ((mode & ExecuteBits) != ExecuteBits)
        {
            File.SetUnixFileMode(path, mode | ExecuteBits);
        }
    }

    public IReadOnlyList<string> Match(string installDirectory, IReadOnlyList<string> include, IReadOnlyList<string> exclude) =>
        Candidates(installDirectory, include, exclude).Selected.Select(file => file.Full).ToList();

    public IReadOnlyCollection<IFile> Select(string installDirectory, IReadOnlyList<string> include, IReadOnlyList<string> exclude)
    {
        var (all, selected) = Candidates(installDirectory, include, exclude);
        if (selected.Count == 0)
        {
            var excluded = exclude.Count > 0 ? $" and exclude '{string.Join("', '", exclude)}'" : string.Empty;
            var heading = all.Count > ListingLimit ? $"Contents (first {ListingLimit})" : "Contents";
            var listing = all.Count == 0 ? "(empty)" : string.Join(", ", all.Take(ListingLimit).Select(file => file.Relative));
            throw new CakeException(
                $"No files in {installDirectory} match include '{string.Join("', '", include)}'{excluded}. " +
                $"{heading}: {listing}. Use 'include=' to choose the files to register.");
        }

        foreach (var file in selected)
        {
            MakeExecutable(file.Full);
        }

        return selected.Select(file => _fileSystem.GetFile(new FilePath(file.Full))).ToList();
    }

    private static (List<(string Full, string Relative)> All, List<(string Full, string Relative)> Selected) Candidates(
        string installDirectory,
        IReadOnlyList<string> include,
        IReadOnlyList<string> exclude)
    {
        var ignoreCase = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();
        var includes = include.Select(pattern => new GlobMatcher(pattern, ignoreCase)).ToList();
        var excludes = exclude.Select(pattern => new GlobMatcher(pattern, ignoreCase)).ToList();

        var all = Directory.EnumerateFiles(installDirectory, "*", SearchOption.AllDirectories)
            .Select(full => (Full: full, Relative: System.IO.Path.GetRelativePath(installDirectory, full).Replace('\\', '/')))
            .Where(file => file.Relative != MarkerFileName)
            .OrderBy(file => file.Relative, StringComparer.Ordinal)
            .ToList();
        var selected = all
            .Where(file => includes.Any(matcher => matcher.IsMatch(file.Relative)) && !excludes.Any(matcher => matcher.IsMatch(file.Relative)))
            .ToList();
        return (all, selected);
    }
}
