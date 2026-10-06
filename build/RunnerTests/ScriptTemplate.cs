using System.Text.RegularExpressions;
using Cake.Core;
using Cake.Core.IO;

namespace Build.RunnerTests;

/// <summary>
/// Renders runner templates (real, compilable files with a dummy version) and guards them against drifting apart.
/// </summary>
public static partial class ScriptTemplate
{
    public const string PipelineMarker = "// --- pipeline ---";

    /// <param name="lineReplacements">Regex pattern (matched per line) → replacement line.</param>
    public static void Render(FilePath source, FilePath destination, IReadOnlyDictionary<string, string> lineReplacements)
    {
        var text = File.ReadAllText(source.FullPath);
        foreach (var (pattern, replacement) in lineReplacements)
        {
            var regex = new Regex(pattern, RegexOptions.Multiline);
            if (!regex.IsMatch(text))
            {
                throw new CakeException($"'{source.FullPath}' has no line matching '{pattern}'.");
            }

            text = regex.Replace(text, replacement);
        }

        Directory.CreateDirectory(destination.GetDirectory().FullPath);
        File.WriteAllText(destination.FullPath, text);
    }

    public static string GetPipeline(string text)
    {
        var normalized = text.Replace("\r\n", "\n");
        var index = normalized.IndexOf(PipelineMarker, StringComparison.Ordinal);
        return index < 0 ? throw new CakeException($"The template has no '{PipelineMarker}' line.") : normalized[index..];
    }

    public static IReadOnlyList<string> GetDirectives(string text) =>
        Directive().Matches(text).Select(match => match.Groups["uri"].Value).Order(StringComparer.Ordinal).ToList();

    public static void EnsureConsistent(string script, string sdk, string frosting)
    {
        if (!string.Equals(GetPipeline(script), GetPipeline(sdk), StringComparison.Ordinal))
        {
            throw new CakeException(
                "The pipelines in test/runners/script/build.cake and test/runners/sdk/cake.cs differ. " +
                $"Everything after '{PipelineMarker}' must be identical.");
        }

        var expected = GetDirectives(script);
        foreach (var (name, text) in new[] { ("test/runners/sdk/cake.cs", sdk), ("test/runners/frosting", frosting) })
        {
            if (!expected.SequenceEqual(GetDirectives(text)))
            {
                throw new CakeException($"The download: directives in {name} differ from test/runners/script/build.cake. Keep all three runners on the same directives.");
            }
        }
    }

    [GeneratedRegex("\"(?<uri>download:[^\"]+)\"")]
    private static partial Regex Directive();
}
