using System.Text;
using System.Text.RegularExpressions;
using Cake.Core;

namespace Cake.Download.Module.Installation;

/// <summary>
/// Matches '/'-separated relative paths against a glob: <c>**</c> spans folders, <c>*</c> and <c>?</c> stay inside one.
/// </summary>
internal sealed class GlobMatcher
{
    private readonly Regex _regex;

    public GlobMatcher(string pattern, bool ignoreCase)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        var normalized = pattern.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        normalized = normalized.TrimStart('/');
        if (normalized.Split('/').Contains(".."))
        {
            throw new CakeException($"The glob '{pattern}' must stay inside the install folder ('..' is not allowed).");
        }

        Pattern = pattern;
        var options = RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
        _regex = new Regex("^" + ToRegex(normalized) + @"\z", options);
    }

    public string Pattern { get; }

    public bool IsMatch(string relativePath) => _regex.IsMatch(relativePath.Replace('\\', '/'));

    private static string ToRegex(string glob)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            if (c == '*' && i + 1 < glob.Length && glob[i + 1] == '*')
            {
                var atSegmentStart = i == 0 || glob[i - 1] == '/';
                var followedBySlash = i + 2 < glob.Length && glob[i + 2] == '/';
                if (atSegmentStart && followedBySlash)
                {
                    builder.Append("(?:.*/)?");
                    i += 2;
                }
                else
                {
                    builder.Append(".*");
                    i += 1;
                }
            }
            else if (c == '*')
            {
                builder.Append("[^/]*");
            }
            else if (c == '?')
            {
                builder.Append("[^/]");
            }
            else
            {
                builder.Append(Regex.Escape(c.ToString()));
            }
        }

        return builder.ToString();
    }
}
