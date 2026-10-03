using System.Text.RegularExpressions;
using Cake.Core;

namespace Cake.Download.Module.Integrity;

/// <summary>
/// Reads GNU (<c>hash  name</c>, <c>hash *name</c>) and BSD (<c>SHA256 (name) = hash</c>) checksum lines and ignores
/// everything else.
/// </summary>
internal static partial class ChecksumsFileParser
{
    public static IReadOnlyDictionary<string, string> Parse(string text, string source)
    {
        ArgumentNullException.ThrowIfNull(text);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var match = Gnu().Match(line);
            if (!match.Success)
            {
                match = Bsd().Match(line);
            }

            if (!match.Success)
            {
                continue;
            }

            var name = match.Groups["name"].Value;
            if (name.StartsWith("./", StringComparison.Ordinal))
            {
                name = name[2..];
            }

            var hash = match.Groups["hash"].Value.ToLowerInvariant();
            if (result.TryGetValue(name, out var existing) && existing != hash)
            {
                throw new CakeException($"The checksums file {source} lists two different SHA-256 hashes for '{name}'.");
            }

            result[name] = hash;
        }

        return result;
    }

    [GeneratedRegex(@"^(?<hash>[0-9a-fA-F]{64})\s+\*?(?<name>\S.*)$")]
    private static partial Regex Gnu();

    [GeneratedRegex(@"^SHA256 ?\((?<name>.+)\) ?= ?(?<hash>[0-9a-fA-F]{64})$")]
    private static partial Regex Bsd();
}
