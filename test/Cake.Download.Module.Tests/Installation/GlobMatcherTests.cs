using Cake.Core;
using Cake.Download.Module.Installation;

namespace Cake.Download.Module.Tests.Installation;

public sealed class GlobMatcherTests
{
    [Theory]
    [InlineData("**/tool", "tool", true)]
    [InlineData("**/tool", "a/b/tool", true)]
    [InlineData("**/tool", "a/tool.txt", false)]
    [InlineData("**/tool", "a/xtool", false)]
    [InlineData("bin/*", "bin/x", true)]
    [InlineData("bin/*", "bin/a/x", false)]
    [InlineData("bin/**", "bin/a/x", true)]
    [InlineData("*.exe", "a.exe", true)]
    [InlineData("*.exe", "d/a.exe", false)]
    [InlineData("**/*.exe", "d/e/a.exe", true)]
    [InlineData("tool?", "tool1", true)]
    [InlineData("tool?", "tool12", false)]
    [InlineData("./bin/tool", "bin/tool", true)]
    [InlineData("a+b/(x)", "a+b/(x)", true)]
    [InlineData("jq", "jq", true)]
    public void IsMatch_Matches_Relative_Paths(string pattern, string path, bool expected)
    {
        Assert.Equal(expected, new GlobMatcher(pattern, ignoreCase: false).IsMatch(path));
    }

    [Fact]
    public void IsMatch_Honours_Case_Sensitivity_And_Backslashes()
    {
        Assert.True(new GlobMatcher("**/Tool.EXE", ignoreCase: true).IsMatch("BIN\\tool.exe"));
        Assert.False(new GlobMatcher("**/Tool", ignoreCase: false).IsMatch("bin/tool"));
    }

    [Fact]
    public void Constructor_Rejects_Patterns_Leaving_The_Install_Folder()
    {
        var exception = Assert.Throws<CakeException>(() => new GlobMatcher("../other/tool", ignoreCase: false));

        Assert.Equal("The glob '../other/tool' must stay inside the install folder ('..' is not allowed).", exception.Message);
    }
}
