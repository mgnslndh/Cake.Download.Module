namespace Build.RunnerTests;

/// <summary>
/// Captures every <c>.cake-download.json</c> in a tools folder, to prove a second run reinstalled nothing.
/// </summary>
public static class MarkerSnapshot
{
    private const string MarkerFileName = ".cake-download.json";

    public static IReadOnlyDictionary<string, string> Take(string toolsDirectory)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(toolsDirectory))
        {
            return result;
        }

        foreach (var folder in Directory.EnumerateDirectories(toolsDirectory))
        {
            var marker = Path.Combine(folder, MarkerFileName);
            if (File.Exists(marker))
            {
                result[Path.GetFileName(folder)] = File.ReadAllText(marker);
            }
        }

        return result;
    }

    public static IReadOnlyList<string> Compare(IReadOnlyDictionary<string, string> first, IReadOnlyDictionary<string, string> second)
    {
        if (first.Count == 0)
        {
            return ["no .cake-download.json markers were written"];
        }

        var failures = new List<string>();
        foreach (var (folder, content) in first.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (!second.TryGetValue(folder, out var after))
            {
                failures.Add($"the second run removed {folder}");
            }
            else if (after != content)
            {
                failures.Add($"the second run reinstalled {folder}");
            }
        }

        return failures;
    }
}
