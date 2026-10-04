namespace Cake.Download.Module;

/// <summary>
/// The format of the downloaded file (the directive's <c>format</c> parameter). Detected from the URL when not set.
/// </summary>
public enum DownloadFormat
{
    /// <summary>A raw file, installed as is.</summary>
    File,

    /// <summary>A zip archive.</summary>
    Zip,

    /// <summary>An uncompressed tar archive.</summary>
    Tar,

    /// <summary>A gzip-compressed tar archive (<c>.tar.gz</c> or <c>.tgz</c>).</summary>
    TarGz,
}
