using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Cake.Core;
using Cake.Download.Module.Directives;

namespace Cake.Download.Module.Archives;

/// <summary>
/// Extracts zip and tar archives verbatim, refusing any entry or link that would land outside the destination and
/// keeping Unix permission bits.
/// </summary>
internal static class ArchiveExtractor
{
    private const int PermissionBits = 0x1FF;

    public static void Extract(string archivePath, ArchiveFormat format, string destination)
    {
        if (format == ArchiveFormat.File)
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "Only archives can be extracted.");
        }

        Directory.CreateDirectory(destination);
        var root = Path.GetFullPath(destination);
        if (format == ArchiveFormat.Zip)
        {
            ExtractZip(archivePath, root);
        }
        else
        {
            ExtractTar(archivePath, root, gzip: format == ArchiveFormat.TarGz);
        }
    }

    private static void ExtractZip(string archivePath, string root)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var target = ResolveInside(root, entry.FullName);
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);

            var mode = (entry.ExternalAttributes >> 16) & PermissionBits;
            if (!OperatingSystem.IsWindows() && mode != 0)
            {
                File.SetUnixFileMode(target, (UnixFileMode)mode);
            }
        }
    }

    private static void ExtractTar(string archivePath, string root, bool gzip)
    {
        var links = new List<(string Name, string Path, string LinkName, string Target, bool Symbolic)>();
        var entryPaths = new List<(string Name, string Path)>();
        using (var file = File.OpenRead(archivePath))
        using (var stream = gzip ? new GZipStream(file, CompressionMode.Decompress) : (Stream)file)
        using (var reader = new TarReader(stream))
        {
            while (reader.GetNextEntry() is { } entry)
            {
                switch (entry.EntryType)
                {
                    case TarEntryType.Directory:
                        var directory = ResolveInside(root, entry.Name);
                        entryPaths.Add((entry.Name, directory));
                        Directory.CreateDirectory(directory);
                        break;
                    case TarEntryType.RegularFile:
                    case TarEntryType.V7RegularFile:
                    case TarEntryType.ContiguousFile:
                        var target = ResolveInside(root, entry.Name);
                        entryPaths.Add((entry.Name, target));
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        entry.ExtractToFile(target, overwrite: true);
                        if (!OperatingSystem.IsWindows())
                        {
                            File.SetUnixFileMode(target, (UnixFileMode)((int)entry.Mode & PermissionBits));
                        }

                        break;
                    case TarEntryType.SymbolicLink:
                        var link = ResolveInside(root, entry.Name);
                        var linkTarget = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(link)!, entry.LinkName));
                        if (!IsInside(root, linkTarget))
                        {
                            throw Unsafe(entry.Name, $"its link target '{entry.LinkName}' is outside the target folder");
                        }

                        entryPaths.Add((entry.Name, link));
                        links.Add((entry.Name, link, entry.LinkName, linkTarget, true));
                        break;
                    case TarEntryType.HardLink:
                        var hardLink = ResolveInside(root, entry.Name);
                        entryPaths.Add((entry.Name, hardLink));
                        links.Add((entry.Name, hardLink, entry.LinkName, ResolveInside(root, entry.LinkName), false));
                        break;
                    default:
                        // PAX/GNU metadata entries are consumed by TarReader; devices and FIFOs are skipped.
                        break;
                }
            }
        }

        var symlinkPaths = links.Where(l => l.Symbolic).Select(l => l.Path).ToList();
        foreach (var (name, path) in entryPaths)
        {
            foreach (var symlink in links.Where(l => l.Symbolic))
            {
                if (!SamePath(symlink.Path, path) && IsInside(symlink.Path, path))
                {
                    throw Unsafe(name, $"its path passes through the link '{symlink.Name}'");
                }
            }
        }

        foreach (var (name, path, linkName, target, symbolic) in links)
        {
            var baseDirectory = symbolic ? Path.GetDirectoryName(path)! : root;
            if (symlinkPaths.Any(symlink => IsInside(symlink, target)) || WalksThrough(baseDirectory, linkName, symlinkPaths))
            {
                throw Unsafe(name, $"its link target '{linkName}' passes through another link");
            }
        }

        foreach (var (name, path, linkName, target, symbolic) in links)
        {
            CreateLink(name, path, linkName, target, symbolic);
        }
    }

    private static bool WalksThrough(string baseDirectory, string linkName, List<string> symlinkPaths)
    {
        // Walk the raw segments one by one, so "link/.." is seen passing through "link" before it is normalized away.
        var current = baseDirectory;
        foreach (var segment in linkName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            current = segment == ".." ? Path.GetDirectoryName(current) ?? current : Path.Combine(current, segment);
            if (symlinkPaths.Any(symlink => SamePath(symlink, current)))
            {
                return true;
            }
        }

        return false;
    }

    private static void CreateLink(string name, string path, string linkName, string target, bool symbolic)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        if (symbolic && !OperatingSystem.IsWindows())
        {
            File.CreateSymbolicLink(path, linkName);
            return;
        }

        // Hard links, and symbolic links on Windows (which need extra privileges), become copies of their target.
        if (File.Exists(target))
        {
            File.Copy(target, path, overwrite: true);
        }
        else if (!symbolic)
        {
            throw Unsafe(name, $"its link target '{linkName}' does not exist in the archive");
        }
    }

    private static string ResolveInside(string root, string entryName)
    {
        var name = entryName.Replace('\\', '/');
        if (name.StartsWith('/') || Path.IsPathRooted(name) || (name.Length >= 2 && name[1] == ':'))
        {
            throw Unsafe(entryName, "absolute paths are not allowed");
        }

        var full = Path.GetFullPath(Path.Combine(root, name));
        if (!IsInside(root, full))
        {
            throw Unsafe(entryName, "it would be extracted outside the target folder");
        }

        return full;
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string Canonical(string path) => path.Normalize(NormalizationForm.FormC);

    private static bool SamePath(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Canonical(left)),
            Path.TrimEndingDirectorySeparator(Canonical(right)),
            PathComparison);

    private static bool IsInside(string root, string fullPath)
    {
        var canonicalRoot = Canonical(root);
        var rootWithSeparator = Path.EndsInDirectorySeparator(canonicalRoot) ? canonicalRoot : canonicalRoot + Path.DirectorySeparatorChar;
        return SamePath(fullPath, root) || Canonical(fullPath).StartsWith(rootWithSeparator, PathComparison);
    }

    private static CakeException Unsafe(string entryName, string reason) =>
        new($"Refusing to extract archive entry '{entryName}': {reason}.");
}
