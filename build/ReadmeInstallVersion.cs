using System.Text.RegularExpressions;
using Cake.Core;
using NuGet.Versioning;

namespace Build;

/// <summary>
/// Checks that the README's install snippets name the version being released, since the README ships in the
/// package and is its nuget.org page. Only stable tags are checked: during a preview the README can install either
/// the preview or the latest stable version.
/// </summary>
public static partial class ReadmeInstallVersion
{
    /// <summary>
    /// Throws when the README lacks an <c>#module</c> or a <c>#:package</c> install snippet for Cake.Download.Module, or when
    /// any of its Cake.Download.Module install directives does not name a stable tag's version.
    /// </summary>
    /// <param name="readme">The README.md content.</param>
    /// <param name="tag">The release tag, e.g. <c>v1.2.0</c>.</param>
    /// <exception cref="CakeException">The README's install snippets are missing or name another version.</exception>
    public static void Check(string readme, string tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || !tag.StartsWith('v') || !NuGetVersion.TryParseStrict(tag[1..], out var version))
        {
            throw new CakeException($"'{tag}' is not a version tag like v1.2.3.");
        }

        if (version.IsPrerelease)
        {
            return;
        }

        var expected = tag[1..];

        // The directive patterns anchor on line ends, which a Windows checkout ends with \r\n.
        readme = readme.ReplaceLineEndings("\n");
        var modules = ModuleDirective().Matches(readme)
            .Select(match => (Directive: match.Value.Trim(), Version: ModuleVersion().Match(match.Groups["query"].Value)))
            .Select(module => (module.Directive, Version: module.Version.Success ? module.Version.Groups["version"].Value : null))
            .ToList();
        var packages = PackageDirective().Matches(readme)
            .Select(match => (Directive: match.Value.Trim(), Version: match.Groups["version"].Success ? match.Groups["version"].Value : null))
            .ToList();

        var problems = new List<string>();
        if (!modules.Any(module => module.Directive.StartsWith("#module", StringComparison.Ordinal)))
        {
            problems.Add("no '#module nuget:?package=Cake.Download.Module&version=…' snippet");
        }

        if (packages.Count == 0)
        {
            problems.Add("no '#:package Cake.Download.Module@…' snippet");
        }

        problems.AddRange(modules.Concat(packages)
            .Where(directive => directive.Version != expected)
            .Select(directive => directive.Version is null
                ? $"'{directive.Directive}' has no version"
                : $"'{directive.Directive}' installs {directive.Version}")
            .Distinct());

        if (problems.Count > 0)
        {
            throw new CakeException(
                $"README.md must install Cake.Download.Module {expected} before tagging {tag}: {string.Join("; ", problems)}.");
        }
    }

    // "#module nuget:?package=Cake.Download.Module&version=1.2.0" or the same with #addin, with any other query parameters
    [GeneratedRegex(@"^[ \t]*#(?:module|addin)[ \t]+nuget:\?package=Cake\.Download\.Module(?<query>(?:&\S*)?)[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex ModuleDirective();

    [GeneratedRegex(@"&version=(?<version>[^&\s]+)")]
    private static partial Regex ModuleVersion();

    // "#:package Cake.Download.Module@1.2.0" or a versionless "#:package Cake.Download.Module"
    [GeneratedRegex(@"^[ \t]*#:package[ \t]+Cake\.Download\.Module(?:@(?<version>\S+))?[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex PackageDirective();
}
