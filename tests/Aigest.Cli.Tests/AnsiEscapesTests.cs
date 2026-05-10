using Aigest.Cli.Core;

namespace Aigest.Cli.Tests;

public class AnsiEscapesTests
{
    [Fact]
    public void StreamingStripper_StripsSplitCsiSequence()
    {
        var stripper = new StreamingAnsiStripper();

        Assert.Equal("a", stripper.StripChunk("a\u001b["));
        Assert.Equal("b", stripper.StripChunk("31mb"));
        Assert.Equal(string.Empty, stripper.Flush());
    }

    [Fact]
    public void StreamingStripper_StripsSplitOscSequence()
    {
        var stripper = new StreamingAnsiStripper();

        Assert.Equal("a", stripper.StripChunk("a\u001b]52;c;"));
        Assert.Equal("b", stripper.StripChunk("payload\u0007b"));
        Assert.Equal(string.Empty, stripper.Flush());
    }

    [Fact]
    public void Strip_RemovesCompleteSequences()
    {
        Assert.Equal("red normal", AnsiEscapes.Strip("\u001b[31mred\u001b[0m normal"));
        Assert.Equal("safe", AnsiEscapes.Strip("\u001b]52;c;payload\u0007safe"));
    }

    [Fact]
    public void StreamingStripper_DropsMalformedFinalEscape()
    {
        var stripper = new StreamingAnsiStripper();

        Assert.Equal("safe", stripper.StripChunk("safe\u001b[31"));
        Assert.Equal(string.Empty, stripper.Flush());
    }

    [Fact]
    public void StreamingStripper_PreservesNormalText()
    {
        var stripper = new StreamingAnsiStripper();

        Assert.Equal("plain text", stripper.StripChunk("plain text"));
        Assert.Equal(string.Empty, stripper.Flush());
    }
}
