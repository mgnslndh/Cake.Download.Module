using System.IO.Compression;
using System.Xml.Linq;

namespace Build;

/// <summary>
/// Checks a packed Cake.Download.Module .nupkg: the <c>net8.0</c> library and its XML docs and no other target framework
/// (Cake loads every <c>lib/</c> folder of a <c>#module</c> package), the icon and README, the <c>cake-module</c> and
/// <c>cake-addin</c> tags, and no dependencies at all, since Cake never installs a <c>#module</c>'s dependencies.
/// </summary>
public static class PackageVerifier
{
    private const string PackageId = "Cake.Download.Module";

    private const string TargetFramework = "net8.0";

    private static readonly string[] RequiredTags = ["cake-module", "cake-addin"];

    private static readonly char[] TagSeparators = [' ', ','];

    public static IReadOnlyList<string> Verify(string packagePath)
    {
        var problems = new List<string>();

        using var package = ZipFile.OpenRead(packagePath);
        var entries = new HashSet<string>(package.Entries.Select(entry => entry.FullName), StringComparer.OrdinalIgnoreCase);

        RequireEntry(entries, $"lib/{TargetFramework}/{PackageId}.dll", problems);
        RequireEntry(entries, $"lib/{TargetFramework}/{PackageId}.xml", problems);

        var otherFrameworks = entries
            .Where(entry => entry.StartsWith("lib/", StringComparison.OrdinalIgnoreCase) && entry.IndexOf('/', 4) > 4)
            .Select(entry => entry[..(entry.IndexOf('/', 4) + 1)])
            .Where(folder => !string.Equals(folder, $"lib/{TargetFramework}/", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal);
        foreach (var folder in otherFrameworks)
        {
            problems.Add($"unexpected {folder}; a module must target {TargetFramework} only, because Cake loads every lib/ folder of a #module package");
        }

        RequireEntry(entries, "icon.png", problems);
        RequireEntry(entries, "README.md", problems);

        var nuspecEntry = package.Entries.SingleOrDefault(
            entry => !entry.FullName.Contains('/') && entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        if (nuspecEntry is null)
        {
            problems.Add("missing .nuspec");
            return problems;
        }

        using var stream = nuspecEntry.Open();
        var nuspec = XDocument.Load(stream);
        var ns = nuspec.Root!.Name.Namespace;

        var tags = (string?)nuspec.Root.Element(ns + "metadata")?.Element(ns + "tags") ?? string.Empty;
        var tagList = tags.Split(TagSeparators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var required in RequiredTags)
        {
            if (!tagList.Contains(required))
            {
                problems.Add($"nuspec tags do not contain '{required}'");
            }
        }

        var dependencies = nuspec.Descendants(ns + "dependency")
            .Select(dependency => (string?)dependency.Attribute("id") ?? "?")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (dependencies.Count > 0)
        {
            problems.Add($"nuspec declares dependencies ({string.Join(", ", dependencies)}); Cake does not install the dependencies of a #module package, so it must have none");
        }

        return problems;
    }

    private static void RequireEntry(HashSet<string> entries, string path, List<string> problems)
    {
        if (!entries.Contains(path))
        {
            problems.Add($"missing {path}");
        }
    }
}
