using Cake.Core;
using Cake.Download.Module.Directives;

namespace Cake.Download.Module.Tests.Directives;

public sealed class PlaceholderExpanderTests
{
    private static readonly Dictionary<string, string> Values = new()
    {
        ["version"] = "1.8.2",
        ["os"] = "linux",
        ["exe"] = string.Empty,
    };

    [Fact]
    public void Expand_Replaces_Every_Placeholder()
    {
        Assert.Equal(
            "https://example.com/jq-1.8.2/jq-linux",
            PlaceholderExpander.Expand("https://example.com/jq-{version}/jq-{os}{exe}", Values, "the download URL"));
    }

    [Fact]
    public void Expand_Leaves_Text_Without_Placeholders_Untouched()
    {
        Assert.Equal("a%2Bb/c", PlaceholderExpander.Expand("a%2Bb/c", Values, "the download URL"));
    }

    [Fact]
    public void Expand_Rejects_Unknown_Placeholders_And_Lists_The_Available_Ones()
    {
        var exception = Assert.Throws<CakeException>(
            () => PlaceholderExpander.Expand("tool-{platform}", Values, "the download URL"));

        Assert.Equal(
            "Unknown placeholder '{platform}' in the download URL. Available placeholders: {version}, {os}, {exe}.",
            exception.Message);
    }

    [Fact]
    public void ContainsAny_Detects_Named_Placeholders()
    {
        Assert.True(PlaceholderExpander.ContainsAny("tool-{os}.zip", ["os", "arch"]));
        Assert.False(PlaceholderExpander.ContainsAny("tool-{version}.zip", ["os", "arch"]));
    }
}
