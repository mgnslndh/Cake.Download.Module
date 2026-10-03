using System.Text.RegularExpressions;
using Cake.Core;

namespace Cake.Download.Module.Directives;

/// <summary>
/// Expands <c>{name}</c> placeholders. Unknown placeholders are errors, so a typo never reaches the server.
/// </summary>
internal static partial class PlaceholderExpander
{
    public static string Expand(string template, IReadOnlyDictionary<string, string> values, string context)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);

        return Placeholder().Replace(template, match =>
        {
            var name = match.Groups["name"].Value;
            if (values.TryGetValue(name, out var value))
            {
                return value;
            }

            var available = string.Join(", ", values.Keys.Select(key => "{" + key + "}"));
            throw new CakeException($"Unknown placeholder '{{{name}}}' in {context}. Available placeholders: {available}.");
        });
    }

    public static bool ContainsAny(string template, IEnumerable<string> names)
    {
        var used = Placeholder().Matches(template).Select(match => match.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);
        return names.Any(used.Contains);
    }

    [GeneratedRegex(@"\{(?<name>[^{}]*)\}")]
    private static partial Regex Placeholder();
}
