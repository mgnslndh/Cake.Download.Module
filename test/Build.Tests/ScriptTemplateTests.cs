using Build.RunnerTests;
using Cake.Core;

namespace Build.Tests;

public sealed class ScriptTemplateTests
{
    private const string Pipeline = "// --- pipeline ---\nTask(\"Default\");\n";

    [Fact]
    public void GetDirectives_Returns_The_Sorted_Download_Uris()
    {
        var text = "#tool \"download:https://b.example/x?package=b\"\n#tool \"download:https://a.example/x?package=a\"\n";

        Assert.Equal(["download:https://a.example/x?package=a", "download:https://b.example/x?package=b"], ScriptTemplate.GetDirectives(text));
    }

    [Fact]
    public void EnsureConsistent_Accepts_Matching_Templates()
    {
        ScriptTemplate.EnsureConsistent(
            "#tool \"download:https://a.example/x?package=a\"\n" + Pipeline,
            "InstallTool(\"download:https://a.example/x?package=a\");\r\n" + Pipeline.Replace("\n", "\r\n"),
            ".InstallTool(new Uri(\"download:https://a.example/x?package=a\"))");
    }

    [Fact]
    public void EnsureConsistent_Rejects_Different_Pipelines()
    {
        var exception = Assert.Throws<CakeException>(() => ScriptTemplate.EnsureConsistent(
            "#tool \"download:https://a.example/x?package=a\"\n" + Pipeline,
            "InstallTool(\"download:https://a.example/x?package=a\");\n" + Pipeline + "// extra\n",
            ".InstallTool(new Uri(\"download:https://a.example/x?package=a\"))"));

        Assert.StartsWith("The pipelines in test/runners/script/build.cake and test/runners/sdk/cake.cs differ.", exception.Message);
    }

    [Fact]
    public void EnsureConsistent_Rejects_Different_Directives()
    {
        var exception = Assert.Throws<CakeException>(() => ScriptTemplate.EnsureConsistent(
            "#tool \"download:https://a.example/x?package=a\"\n" + Pipeline,
            "InstallTool(\"download:https://a.example/x?package=a\");\n" + Pipeline,
            ".InstallTool(new Uri(\"download:https://a.example/y?package=a\"))"));

        Assert.StartsWith("The download: directives in test/runners/frosting/Program.cs differ from test/runners/script/build.cake.", exception.Message);
    }
}
