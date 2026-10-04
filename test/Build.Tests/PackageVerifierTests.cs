using System.IO.Compression;

namespace Build.Tests;

public sealed class PackageVerifierTests : IDisposable
{
    private const string Tags = "cake,cake-module,cake-addin,cake-build,download,tool";

    private static readonly string[] Libraries =
    [
        "lib/net8.0/Cake.Download.Module.dll", "lib/net8.0/Cake.Download.Module.xml",
        "lib/net9.0/Cake.Download.Module.dll", "lib/net9.0/Cake.Download.Module.xml",
        "lib/net10.0/Cake.Download.Module.dll", "lib/net10.0/Cake.Download.Module.xml",
    ];

    private readonly string _directory = Directory.CreateTempSubdirectory("PackageVerifierTests").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Verify_Accepts_A_Complete_Package_With_Empty_Dependency_Groups()
    {
        var package = CreatePackage(
            Libraries.Concat(["icon.png", "README.md"]),
            Nuspec(Tags, """<dependencies><group targetFramework="net8.0" /></dependencies>"""));

        Assert.Empty(PackageVerifier.Verify(package));
    }

    [Fact]
    public void Verify_Reports_Missing_Files()
    {
        var package = CreatePackage(Libraries.Where(path => path != "lib/net9.0/Cake.Download.Module.xml"), Nuspec(Tags));

        Assert.Equal(
            ["missing lib/net9.0/Cake.Download.Module.xml", "missing icon.png", "missing README.md"],
            PackageVerifier.Verify(package));
    }

    [Fact]
    public void Verify_Reports_A_Missing_Cake_Module_Tag()
    {
        var package = CreatePackage(Libraries.Concat(["icon.png", "README.md"]), Nuspec("cake,cake-addin"));

        Assert.Equal(["nuspec tags do not contain 'cake-module'"], PackageVerifier.Verify(package));
    }

    [Fact]
    public void Verify_Reports_A_Missing_Cake_Addin_Tag()
    {
        var package = CreatePackage(Libraries.Concat(["icon.png", "README.md"]), Nuspec("cake,cake-module"));

        Assert.Equal(["nuspec tags do not contain 'cake-addin'"], PackageVerifier.Verify(package));
    }

    [Fact]
    public void Verify_Reports_Any_Dependency()
    {
        var package = CreatePackage(
            Libraries.Concat(["icon.png", "README.md"]),
            Nuspec(Tags, """<dependencies><group targetFramework="net8.0"><dependency id="System.Text.Json" version="8.0.0" /><dependency id="Cake.Core" version="6.0.0" /></group></dependencies>"""));

        Assert.Equal(
            ["nuspec declares dependencies (Cake.Core, System.Text.Json); Cake does not install the dependencies of a #module package, so it must have none"],
            PackageVerifier.Verify(package));
    }

    [Fact]
    public void Verify_Reports_A_Missing_Nuspec()
    {
        var package = CreatePackage(Libraries.Concat(["icon.png", "README.md"]), nuspec: null);

        Assert.Equal(["missing .nuspec"], PackageVerifier.Verify(package));
    }

    private static string Nuspec(string tags, string dependencies = "") => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
          <metadata>
            <id>Cake.Download.Module</id>
            <version>1.0.0</version>
            <tags>{tags}</tags>
            {dependencies}
          </metadata>
        </package>
        """;

    private string CreatePackage(IEnumerable<string> files, string? nuspec)
    {
        var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.nupkg");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var file in files)
        {
            archive.CreateEntry(file);
        }

        if (nuspec is not null)
        {
            using var writer = new StreamWriter(archive.CreateEntry("Cake.Download.Module.nuspec").Open());
            writer.Write(nuspec);
        }

        return path;
    }
}
