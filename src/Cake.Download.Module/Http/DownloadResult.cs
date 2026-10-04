namespace Cake.Download.Module.Http;

internal sealed record DownloadResult(string Path, string Sha256, long Length);
