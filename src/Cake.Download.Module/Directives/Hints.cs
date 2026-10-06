namespace Cake.Download.Module.Directives;

/// <summary>
/// The paste-ready parts of error messages, in the form the user wrote: directive parameters or
/// <c>DownloadToolSettings</c> methods.
/// </summary>
internal static class Hints
{
    public static string NoIntegrity(DirectiveSource source, string package) => source == DirectiveSource.Settings
        ? $"The DownloadToolSettings for '{package}' have no integrity check."
        : $"The download directive for '{package}' has no integrity check.";

    public static string AddPin(DirectiveSource source, string parameter, string sha256) => source == DirectiveSource.Settings
        ? $"Add {PinCall(parameter, sha256)} to the DownloadToolSettings, or .WithoutVerification() to install without verification (not recommended)."
        : $"Add '&{parameter}={sha256}' to the directive, or '&sha256=skip' to install without verification (not recommended).";

    public static string PinChecksums(DirectiveSource source, string package, string checksumsReference, string sha256) => source == DirectiveSource.Settings
        ? $"Use .WithChecksums(\"{checksumsReference}\", \"{sha256}\") in the DownloadToolSettings for '{package}'."
        : $"Add '&checksums_sha256={sha256}' to the directive for '{package}'.";

    public static string NotFoundOverrides(DirectiveSource source, string rid) => source == DirectiveSource.Settings
        ? "If the asset is named differently on this platform, add an override such as .WithOs(…), .WithArch(…), .WithArchive(…) " +
          $"or .WithUrl(\"{rid}\", …) to the DownloadToolSettings."
        : "If the asset is named differently on this platform, add an override such as 'os.<value>=', 'arch.<value>=', 'archive.<value>=' " +
          $"or 'url.{rid}=' to the directive.";

    private static string PinCall(string parameter, string sha256) =>
        parameter.StartsWith("sha256.", StringComparison.Ordinal)
            ? $".WithSha256(\"{parameter["sha256.".Length..]}\", \"{sha256}\")"
            : $".WithSha256(\"{sha256}\")";
}
