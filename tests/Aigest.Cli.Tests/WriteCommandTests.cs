using System;
using System.IO;
using System.Threading.Tasks;
using Aigest.Cli.Commands;
using Xunit;

namespace Aigest.Cli.Tests;

// NOTE: CLI argument-parsing gap
// The `case "--allow-outside-cwd":` branch in Program.cs RunWrite() is not directly tested
// here because Program.Main constructs a real OpenAiChatClient (via ConfigLoader.Load()) and
// provides no injection seam for FakeChatClient. A typo in that case arm would silently leave
// allowOutsideCwd stuck at false without any test catching it.
//
// Current coverage:
//   - WriteCommand.RunAsync is exercised with allowOutsideCwd: true and allowOutsideCwd: false
//     in the three tests below (and in IntegrationTests.cs).
//
// To close this gap in the future, consider making RunWrite() internal and accepting an optional
// IChatClient parameter so tests can inject FakeChatClient through Program.Main.
public class WriteCommandTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _originalApiKey;
    private string? _fileToCleanup;

    public WriteCommandTests()
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
        if (_fileToCleanup is not null && File.Exists(_fileToCleanup))
            File.Delete(_fileToCleanup);
        Environment.SetEnvironmentVariable("AIGEST_API_KEY", _originalApiKey);
    }

    [Fact]
    public async Task RejectsTargetOutsideCwd_WhenFlagNotSet()
    {
        var context = Path.Combine(_tempDir, "context.cs");
        await File.WriteAllTextAsync(context, "class A {}");

        var targetPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.cs");

        var fake = new FakeChatClient { Response = "generated content" };
        var exit = await WriteCommand.RunAsync(
            "generate a class",
            [context],
            targetPath,
            overwrite: false,
            allowOutsideCwd: false,
            1000,
            null,
            TestAigestConfig.Create(),
            fake);

        Assert.Equal(1, exit);
        Assert.False(File.Exists(targetPath));
    }

    [Fact]
    public async Task AllowsTargetOutsideCwd_WhenFlagSet()
    {
        var context = Path.Combine(_tempDir, "context.cs");
        await File.WriteAllTextAsync(context, "class A {}");

        var targetPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.cs");
        _fileToCleanup = targetPath;

        var fake = new FakeChatClient { Response = "generated content" };
        var exit = await WriteCommand.RunAsync(
            "generate a class",
            [context],
            targetPath,
            overwrite: false,
            allowOutsideCwd: true,
            1000,
            null,
            TestAigestConfig.Create(),
            fake);

        Assert.Equal(0, exit);
        Assert.True(File.Exists(targetPath));
    }

    [Fact]
    public async Task AllowsTargetInsideCwd_WithoutFlag()
    {
        var context = Path.Combine(_tempDir, "context.cs");
        await File.WriteAllTextAsync(context, "class A {}");

        var targetPath = Path.Combine(Directory.GetCurrentDirectory(), $"{Guid.NewGuid()}.cs");
        _fileToCleanup = targetPath;

        var fake = new FakeChatClient { Response = "generated content" };
        var exit = await WriteCommand.RunAsync(
            "generate a class",
            [context],
            targetPath,
            overwrite: false,
            allowOutsideCwd: false,
            1000,
            null,
            TestAigestConfig.Create(),
            fake);

        Assert.Equal(0, exit);
        Assert.True(File.Exists(targetPath));
    }

    [Fact]
    public async Task SendsContextBeforeSpec_WithContextBlock()
    {
        var context = Path.Combine(_tempDir, "context.cs");
        await File.WriteAllTextAsync(context, "class A {}");

        var targetPath = Path.Combine(_tempDir, "generated.cs");
        var fake = new FakeChatClient { Response = "generated content" };
        var exit = await WriteCommand.RunAsync(
            "generate a class",
            [context],
            targetPath,
            overwrite: false,
            allowOutsideCwd: true,
            1000,
            null,
            TestAigestConfig.Create(),
            fake);

        Assert.Equal(0, exit);
        Assert.Equal(3, fake.CapturedMessages.Count);
        Assert.Contains("<context>", fake.CapturedMessages[1].Content);
        Assert.Contains("class A {}", fake.CapturedMessages[1].Content);
        Assert.DoesNotContain("<spec>", fake.CapturedMessages[1].Content);
        Assert.Contains("<spec>", fake.CapturedMessages[2].Content);
        Assert.Contains("generate a class", fake.CapturedMessages[2].Content);
        Assert.DoesNotContain("<context>", fake.CapturedMessages[2].Content);
    }

    [Fact]
    public async Task SystemPrompt_ContainsGenerationContract()
    {
        var context = Path.Combine(_tempDir, "context.cs");
        await File.WriteAllTextAsync(context, "class A {}");

        var targetPath = Path.Combine(_tempDir, "generated.cs");
        var fake = new FakeChatClient { Response = "generated content" };
        var exit = await WriteCommand.RunAsync(
            "generate a class",
            [context],
            targetPath,
            overwrite: false,
            allowOutsideCwd: true,
            1000,
            null,
            TestAigestConfig.Create(),
            fake);

        Assert.Equal(0, exit);

        var systemMessage = fake.CapturedMessages[0];
        Assert.Equal("system", systemMessage.Role);
        Assert.Contains("Output only the final file content", systemMessage.Content);
        Assert.Contains("Do not invent APIs", systemMessage.Content);
        Assert.Contains("TODO comment", systemMessage.Content);
        Assert.Contains("untrusted input", systemMessage.Content);
        Assert.Contains("Do not wrap the answer in Markdown fences", systemMessage.Content);
    }
}
