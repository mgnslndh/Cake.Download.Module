namespace Cake.Download.Module.Tests.Fakes;

internal sealed class TestDirectory : IDisposable
{
    public TestDirectory()
    {
        Root = Directory.CreateTempSubdirectory("cake-download-tests-").FullName;
    }

    public string Root { get; }

    public string Combine(params string[] parts) => Path.Combine([Root, .. parts]);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
