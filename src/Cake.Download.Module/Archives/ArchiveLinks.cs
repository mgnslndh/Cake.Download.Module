using Cake.Core.Diagnostics;
using static Cake.Download.Module.Archives.ArchivePaths;

namespace Cake.Download.Module.Archives;

/// <summary>
/// Collects the links of an archive while it is extracted, then checks that no entry or link passes through a symbolic
/// link and creates the links: symbolic links on Unix, copies of their target for hard links and on Windows.
/// </summary>
internal sealed class ArchiveLinks(string root)
{
    private readonly List<(string Name, string Path)> _entries = [];
    private readonly List<Link> _links = [];

    public void AddEntry(string name, string path) => _entries.Add((name, path));

    public void AddSymbolicLink(string name, string linkName)
    {
        var path = ResolveInside(root, name);
        var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, linkName));
        if (!IsInside(root, target) || LeavesRoot(Path.GetDirectoryName(path)!, linkName))
        {
            throw Unsafe(name, $"its link target '{linkName}' is outside the target folder");
        }

        AddEntry(name, path);
        _links.Add(new Link(name, path, linkName, target, Symbolic: true));
    }

    public void AddHardLink(string name, string linkName)
    {
        var path = ResolveInside(root, name);
        AddEntry(name, path);
        _links.Add(new Link(name, path, linkName, ResolveInside(root, linkName), Symbolic: false));
    }

    public void Create(ICakeLog log)
    {
        var symlinks = _links.Where(link => link.Symbolic).ToList();
        foreach (var (name, path) in _entries)
        {
            foreach (var symlink in symlinks)
            {
                if (!SamePath(symlink.Path, path) && IsInside(symlink.Path, path))
                {
                    throw Unsafe(name, $"its path passes through the link '{symlink.Name}'");
                }
            }
        }

        var symlinkPaths = symlinks.Select(link => link.Path).ToList();
        foreach (var (name, path, linkName, target, symbolic) in _links)
        {
            // A symbolic link may point at another symbolic link (libfoo.so -> libfoo.so.1), but no link may pass through
            // one. Every link target is checked to stay inside the root, so following a chain of links stays inside too.
            var baseDirectory = symbolic ? Path.GetDirectoryName(path)! : root;
            var throughLink = symlinkPaths.Any(symlink => IsInside(symlink, target) && !(symbolic && SamePath(symlink, target)));
            if (throughLink || WalksThrough(baseDirectory, linkName, symlinkPaths, allowLastSegment: symbolic))
            {
                throw Unsafe(name, $"its link target '{linkName}' passes through another link");
            }
        }

        CreateLinks(log);
    }

    private bool LeavesRoot(string baseDirectory, string linkName)
    {
        // The root is renamed when the install is published, so a target that climbs out of the root and back in by
        // its folder name ("../../content/x") would resolve somewhere else afterwards. Every step must stay inside.
        var current = baseDirectory;
        foreach (var segment in linkName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            current = segment == ".." ? Path.GetDirectoryName(current) ?? current : Path.Combine(current, segment);
            if (!IsInside(root, current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool WalksThrough(string baseDirectory, string linkName, List<string> symlinkPaths, bool allowLastSegment)
    {
        // Walk the raw segments one by one, so "link/.." is seen passing through "link" before it is normalized away.
        var segments = linkName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).Where(segment => segment != ".").ToList();
        var current = baseDirectory;
        for (var index = 0; index < segments.Count; index++)
        {
            current = segments[index] == ".." ? Path.GetDirectoryName(current) ?? current : Path.Combine(current, segments[index]);
            var isLast = index == segments.Count - 1;
            if (!(isLast && allowLastSegment) && symlinkPaths.Any(symlink => SamePath(symlink, current)))
            {
                return true;
            }
        }

        return false;
    }

    private void CreateLinks(ICakeLog log)
    {
        var pending = new List<Link>();
        foreach (var link in _links)
        {
            if (link.Symbolic && !OperatingSystem.IsWindows())
            {
                PrepareLinkPath(link.Path);
                File.CreateSymbolicLink(link.Path, link.LinkName);
            }
            else
            {
                pending.Add(link);
            }
        }

        // Hard links, and symbolic links on Windows (which need extra privileges), become copies of their target. A link
        // is copied once its target neither is nor contains a link that is still waiting to be copied.
        while (pending.Count > 0)
        {
            var ready = pending.Where(link => !pending.Any(other => IsInside(link.Target, other.Path))).ToList();
            if (ready.Count == 0)
            {
                throw Unsafe(pending[0].Name, $"its link target '{pending[0].LinkName}' is part of a link cycle");
            }

            foreach (var (name, path, linkName, target, symbolic) in ready)
            {
                PrepareLinkPath(path);
                if (File.Exists(target))
                {
                    File.Copy(target, path, overwrite: true);
                }
                else if (symbolic && Directory.Exists(target))
                {
                    CopyDirectory(target, path);
                }
                else if (symbolic)
                {
                    log.Verbose("Skipping archive entry '{0}': its link target '{1}' does not exist in the archive.", name, linkName);
                }
                else
                {
                    throw Unsafe(name, $"its link target '{linkName}' does not exist in the archive");
                }
            }

            pending.RemoveAll(ready.Contains);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static void PrepareLinkPath(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed record Link(string Name, string Path, string LinkName, string Target, bool Symbolic);
}
