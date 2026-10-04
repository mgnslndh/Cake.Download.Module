using System.Text.Json;

namespace Build.RunnerTests;

public sealed record ToolReportEntry(string Name, string Path, string Sha256, string Version);

public static class ToolReport
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<ToolReportEntry> Read(string path) =>
        JsonSerializer.Deserialize<List<ToolReportEntry>>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException($"'{path}' is not a tool report.");
}
