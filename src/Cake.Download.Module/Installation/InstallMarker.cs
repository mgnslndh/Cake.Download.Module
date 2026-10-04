using Cake.Download.Module.Directives;

namespace Cake.Download.Module.Installation;

/// <summary>
/// The provenance written to <c>.cake-download.json</c>; an install is current when it matches the plan.
/// </summary>
internal sealed record InstallMarker(
    int Schema,
    string Package,
    string Version,
    string Rid,
    string Url,
    string Format,
    string? FileName,
    string Integrity,
    string Sha256,
    DateTimeOffset InstalledAt)
{
    public const int CurrentSchema = 1;

    public static InstallMarker For(DownloadPlan plan, string sha256, DateTimeOffset installedAt) => new(
        CurrentSchema,
        plan.Package,
        plan.Version,
        plan.Platform.Rid,
        plan.Url.AbsoluteUri,
        ArchiveFormats.ToName(plan.Format),
        plan.FileName,
        plan.Integrity.Fingerprint,
        sha256,
        installedAt);

    public bool Matches(DownloadPlan plan) =>
        Schema == CurrentSchema
        && Rid == plan.Platform.Rid
        && Url == plan.Url.AbsoluteUri
        && Format == ArchiveFormats.ToName(plan.Format)
        && FileName == plan.FileName
        && Integrity == plan.Integrity.Fingerprint;
}
