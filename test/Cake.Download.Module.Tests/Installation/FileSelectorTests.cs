using Cake.Core;
using Cake.Core.IO;
using Cake.Download.Module.Installation;
using Cake.Download.Module.Tests.Fakes;

namespace Cake.Download.Module.Tests.Installation;

public sealed class FileSelectorTests : IDisposable
{
    private readonly TestDirectory _directory = new();
    private readonly FileSelector _selector = new(new FileSystem());

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void Select_Finds_The_Default_Include_In_A_Nested_Folder()
    {
        Write("gh_2.62.0_linux_amd64/bin/gh", "gh_2.62.0_linux_amd64/LICENSE", ".cake-download.json");

        var files = _selector.Select(_directory.Root, ["**/gh"], []);

        Assert.Equal(_directory.Combine("gh_2.62.0_linux_amd64", "bin", "gh"), Assert.Single(files).Path.FullPath.Replace('/', System.IO.Path.DirectorySeparatorChar));
    }

    [Fact]
    public void Select_Returns_Every_Match_In_Ordinal_Order_And_Applies_Exclude()
    {
        Write("tools/b", "tools/a", "tools/a.txt", "other/c");

        var matches = _selector.Match(_directory.Root, ["tools/*"], ["**/*.txt"]);

        Assert.Equal([_directory.Combine("tools", "a"), _directory.Combine("tools", "b")], matches);
    }

    [Fact]
    public void Select_Never_Returns_The_Marker()
    {
        Write(".cake-download.json");

        Assert.Empty(_selector.Match(_directory.Root, ["**/*"], []));
    }

    [Fact]
    public void Select_Explains_An_Empty_Selection_With_The_Folder_Contents()
    {
        Write("bin/tool.exe", "README.md");

        var exception = Assert.Throws<CakeException>(() => _selector.Select(_directory.Root, ["**/jq"], ["**/*.md"]));

        Assert.Equal(
            $"No files in {_directory.Root} match include '**/jq' and exclude '**/*.md'. Contents: README.md, bin/tool.exe. Use 'include=' to choose the files to register.",
            exception.Message);
    }

    [Fact]
    public void Select_Makes_Selected_Files_Executable_On_Unix()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes only.");
            return;
        }

        Write("bin/tool", "bin/data");

        _selector.Select(_directory.Root, ["bin/tool"], []);

        Assert.True(File.GetUnixFileMode(_directory.Combine("bin", "tool")).HasFlag(UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute));
        Assert.False(File.GetUnixFileMode(_directory.Combine("bin", "data")).HasFlag(UnixFileMode.UserExecute));
    }

    private void Write(params string[] relativePaths)
    {
        foreach (var relativePath in relativePaths)
        {
            var path = _directory.Combine(relativePath.Split('/'));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, relativePath);
        }
    }
}
