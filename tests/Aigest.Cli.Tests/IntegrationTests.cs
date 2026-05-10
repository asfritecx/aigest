using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Aigest.Cli.Commands;
using Aigest.Cli.Core;
using Xunit;

namespace Aigest.Cli.Tests;

public class IntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _originalApiKey;

    public IntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _originalApiKey = Environment.GetEnvironmentVariable("AIGEST_API_KEY") ?? "";
        Environment.SetEnvironmentVariable("AIGEST_API_KEY", "test-key");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
        Environment.SetEnvironmentVariable("AIGEST_API_KEY", _originalApiKey);
    }

    [Fact]
    public async Task Ask_SendsCorrectMessages()
    {
        var file = Path.Combine(_tempDir, "code.cs");
        await File.WriteAllTextAsync(file, "class A {}");

        var fake = new FakeChatClient { Response = "Analysis result" };
        var exit = await AskCommand.RunAsync(
            [file],
            "What does this do?",
            1234,
            null,
            TestAigestConfig.Create(),
            fake);

        Assert.Equal(0, exit);
        Assert.Equal(1234, fake.CapturedMaxTokens);
        Assert.Equal(3, fake.CapturedMessages.Count);
        Assert.Equal("system", fake.CapturedMessages[0].Role);
        Assert.Equal("user", fake.CapturedMessages[1].Role);
        Assert.Contains("<corpus>", fake.CapturedMessages[1].Content);
        Assert.Contains("class A {}", fake.CapturedMessages[1].Content);
        Assert.Contains("</corpus>", fake.CapturedMessages[1].Content);
        Assert.Equal("user", fake.CapturedMessages[2].Role);
        Assert.Equal("What does this do?", fake.CapturedMessages[2].Content);
    }

    [Fact]
    public async Task Write_RefusesOverwrite_UnlessFlagSet()
    {
        var context = Path.Combine(_tempDir, "context.cs");
        var target = Path.Combine(_tempDir, "output.cs");
        await File.WriteAllTextAsync(context, "class A {}");
        await File.WriteAllTextAsync(target, "existing");

        var fake = new FakeChatClient { Response = "generated" };
        var exit = await WriteCommand.RunAsync(
            "generate tests",
            [context],
            target,
            overwrite: false,
            allowOutsideCwd: true,
            1000,
            null,
            TestAigestConfig.Create(),
            fake);

        Assert.Equal(1, exit);

        // Now with overwrite
        exit = await WriteCommand.RunAsync(
            "generate tests",
            [context],
            target,
            overwrite: true,
            allowOutsideCwd: true,
            1000,
            null,
            TestAigestConfig.Create(),
            fake);

        Assert.Equal(0, exit);
        Assert.Equal("generated\n", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task Write_SendsSpecAndContext()
    {
        var context = Path.Combine(_tempDir, "context.cs");
        var target = Path.Combine(_tempDir, "output.cs");
        await File.WriteAllTextAsync(context, "class A {}");

        var fake = new FakeChatClient { Response = "generated content" };
        var exit = await WriteCommand.RunAsync(
            "generate xunit tests",
            [context],
            target,
            overwrite: false,
            allowOutsideCwd: true,
            2000,
            null,
            TestAigestConfig.Create(),
            fake);

        Assert.Equal(0, exit);
        Assert.Equal(2000, fake.CapturedMaxTokens);
        Assert.Equal(3, fake.CapturedMessages.Count);
        Assert.Contains("<context>", fake.CapturedMessages[1].Content);
        Assert.Contains("class A {}", fake.CapturedMessages[1].Content);
        Assert.Contains("<spec>", fake.CapturedMessages[2].Content);
        Assert.Contains("generate xunit tests", fake.CapturedMessages[2].Content);
    }

    [Fact]
    public async Task Write_CreatesParentDirectories()
    {
        var context = Path.Combine(_tempDir, "context.cs");
        var target = Path.Combine(_tempDir, "nested", "deep", "output.cs");
        await File.WriteAllTextAsync(context, "class A {}");

        var fake = new FakeChatClient { Response = "content" };
        var exit = await WriteCommand.RunAsync(
            "generate",
            [context],
            target,
            overwrite: false,
            allowOutsideCwd: true,
            100,
            null,
            TestAigestConfig.Create(),
            fake);

        Assert.Equal(0, exit);
        Assert.True(File.Exists(target));
    }

}
