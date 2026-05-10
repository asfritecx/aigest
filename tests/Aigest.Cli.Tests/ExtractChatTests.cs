using System;
using System.IO;
using Aigest.Cli.Commands;
using Xunit;

namespace Aigest.Cli.Tests;

public class ExtractChatTests : IDisposable
{
    private readonly string _tempDir;

    public ExtractChatTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void ExtractChat_ProducesGoldenOutput()
    {
        var input = Path.Combine(_tempDir, "input.jsonl");
        var output = Path.Combine(_tempDir, "output.txt");

        File.WriteAllText(input, """
{"role":"user","content":"Hello"}
{"role":"assistant","content":"Hi there"}
{"role":"user","text":"What is 2+2?"}
{"role":"assistant","message":"It equals 4."}
""");

        var exit = ExtractChatCommand.Run(input, output);

        Assert.Equal(0, exit);
        Assert.True(File.Exists(output));
        var content = File.ReadAllText(output);
        Assert.Contains("## user / line 1", content);
        Assert.Contains("Hello", content);
        Assert.Contains("## assistant / line 2", content);
        Assert.Contains("Hi there", content);
        Assert.Contains("## user / line 3", content);
        Assert.Contains("What is 2+2?", content);
        Assert.Contains("## assistant / line 4", content);
        Assert.Contains("It equals 4.", content);
    }

    [Fact]
    public void ExtractChat_SkipsInvalidJsonLines()
    {
        var input = Path.Combine(_tempDir, "input.jsonl");
        var output = Path.Combine(_tempDir, "output.txt");

        File.WriteAllText(input, """
not json
{"role":"user","content":"valid"}
bad line
""");

        var exit = ExtractChatCommand.Run(input, output);

        Assert.Equal(0, exit);
        var content = File.ReadAllText(output);
        Assert.DoesNotContain("not json", content);
        Assert.Contains("valid", content);
    }

    [Fact]
    public void ExtractChat_ReturnsError_WhenNoReadableText()
    {
        var input = Path.Combine(_tempDir, "input.jsonl");
        var output = Path.Combine(_tempDir, "output.txt");

        File.WriteAllText(input, "{\"unknown\":true}\n");

        var exit = ExtractChatCommand.Run(input, output);

        Assert.Equal(1, exit);
    }

    [Fact]
    public void ExtractChat_ReturnsError_WhenInputMissing()
    {
        var output = Path.Combine(_tempDir, "output.txt");
        var exit = ExtractChatCommand.Run(Path.Combine(_tempDir, "missing.jsonl"), output);
        Assert.Equal(1, exit);
    }
}
