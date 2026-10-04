namespace Cake.Download.Module.Directives;

/// <summary>
/// The form the user wrote a download in, so error hints can be phrased in that form.
/// </summary>
internal enum DirectiveSource
{
    /// <summary>A <c>download:</c> directive: <c>#tool</c>, <c>InstallTool</c> or the directive alias.</summary>
    Directive,

    /// <summary>A <c>DownloadToolSettings</c> passed to the settings alias.</summary>
    Settings,
}
