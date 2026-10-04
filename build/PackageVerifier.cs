using System.IO.Compression;
using System.Xml.Linq;

namespace Build;

/// <summary>
/// Checks a packed Cake.Download.Module .nupkg: a library and its XML docs per target framework, the icon and README,
/// the <c>cake-module</c> tag, and no dependencies at all, since Cake never installs a <c>#module</c>'s dependencies.
/// </summary>
public static class PackageVerifier
{
    private const string PackageId = "Cake.Download.Module";

    private static readonly string[] TargetFrameworks = ["net8.0", "net9.0", "net10.0"];

    private static readonly char[] TagSeparators = [' ', ','];

    public static IReadOnlyList<string> Verify(string packagePath)
    {
        var problems = new List<string>();

        using var package = ZipFile.OpenRead(packagePath);
        var entries = new HashSet<string>(package.Entries.Select(entry => entry.FullName), StringComparer.OrdinalIgnoreCase);

        foreach (var framework in TargetFrameworks)
        {
            RequireEntry(entries, $"lib/{framework}/{PackageId}.dll", problems);
            RequireEntry(entries, $"lib/{framework}/{PackageId}.xml", problems);
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
        if (!tags.Split(TagSeparators, StringSplitOptions.RemoveEmptyEntries).Contains("cake-module"))
        {
            problems.Add("nuspec tags do not contain 'cake-module'");
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
