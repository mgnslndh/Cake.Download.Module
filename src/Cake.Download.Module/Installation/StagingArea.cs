namespace Cake.Download.Module.Installation;

internal sealed class StagingArea : IDisposable
{
    public StagingArea(string root)
    {
        Root = root;
        DownloadDirectory = Path.Combine(root, "download");
        ContentDirectory = Path.Combine(root, "content");
        Directory.CreateDirectory(DownloadDirectory);
        Directory.CreateDirectory(ContentDirectory);
    }

    public string Root { get; }

    public string DownloadDirectory { get; }

    public string ContentDirectory { get; }

    public void Dispose() => InstallStore.TryDeleteDirectory(Root);
}
