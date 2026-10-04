using System.Text;
using Cake.Core;

namespace Cake.Download.Module.Archives;

/// <summary>
/// Resolves and compares archive entry paths against the extraction root.
/// </summary>
internal static class ArchivePaths
{
    public static string ResolveInside(string root, string entryName)
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

    public static bool SamePath(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Canonical(left)),
            Path.TrimEndingDirectorySeparator(Canonical(right)),
            PathComparison);

    public static bool IsInside(string root, string fullPath)
    {
        var canonicalRoot = Canonical(root);
        var rootWithSeparator = Path.EndsInDirectorySeparator(canonicalRoot) ? canonicalRoot : canonicalRoot + Path.DirectorySeparatorChar;
        return SamePath(fullPath, root) || Canonical(fullPath).StartsWith(rootWithSeparator, PathComparison);
    }

    public static CakeException Unsafe(string entryName, string reason) =>
        new($"Refusing to extract archive entry '{entryName}': {reason}.");

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string Canonical(string path) => path.Normalize(NormalizationForm.FormC);
}
