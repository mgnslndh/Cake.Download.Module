using Cake.Core;
using Cake.Download.Module.Integrity;

namespace Cake.Download.Module.Tests.Integrity;

public sealed class ChecksumsFileParserTests
{
    private const string A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string B = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void Parse_Reads_Gnu_Text_And_Binary_Lines()
    {
        var entries = ChecksumsFileParser.Parse($"{A}  jq-linux-amd64\n{B} *jq-windows-amd64.exe\n", "sums");

        Assert.Equal(A, entries["jq-linux-amd64"]);
        Assert.Equal(B, entries["jq-windows-amd64.exe"]);
    }

    [Fact]
    public void Parse_Reads_Bsd_Lines()
    {
        Assert.Equal(A, ChecksumsFileParser.Parse($"SHA256 (tool.tar.gz) = {A}", "sums")["tool.tar.gz"]);
    }

    [Fact]
    public void Parse_Tolerates_Crlf_Blank_Lines_Other_Content_Upper_Case_And_Dot_Slash()
    {
        var entries = ChecksumsFileParser.Parse($"# checksums\r\n\r\n{A.ToUpperInvariant()}  ./tool.zip\r\nnot a checksum line\r\n", "sums");

        Assert.Equal(A, Assert.Single(entries).Value);
        Assert.Equal("tool.zip", entries.Keys.Single());
    }

    [Fact]
    public void Parse_Accepts_Identical_Duplicates_And_Rejects_Conflicting_Ones()
    {
        Assert.Single(ChecksumsFileParser.Parse($"{A}  tool\n{A}  tool\n", "sums"));

        var exception = Assert.Throws<CakeException>(() => ChecksumsFileParser.Parse($"{A}  tool\n{B}  tool\n", "https://example.com/sums"));
        Assert.Equal("The checksums file https://example.com/sums lists two different SHA-256 hashes for 'tool'.", exception.Message);
    }
}
