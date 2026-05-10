using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aigest.Cli.Commands;
using Aigest.Cli.Core;
using Xunit;

namespace Aigest.Cli.Tests;

public class AskCommandTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _originalApiKey;

    public AskCommandTests()
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
    public async Task SystemPrompt_ContainsAnalysisContract()
    {
        var file = Path.Combine(_tempDir, "sample.cs");
        await File.WriteAllTextAsync(file, "public class Foo {}");

        var fake = new FakeChatClient { Response = "analysis" };
        var exit = await AskCommand.RunAsync(
            [file],
            "What is this?",
            512,
            null,
            TestAigestConfig.Create(),
            fake);

        Assert.Equal(0, exit);

        var systemMessage = fake.CapturedMessages.First(m => m.Role == "system");
        Assert.Contains("untrusted source data", systemMessage.Content);
        Assert.Contains("line-numbered corpus", systemMessage.Content);
        Assert.Contains("Cite file paths and line numbers or line ranges", systemMessage.Content);
        Assert.Contains("Not found in provided files", systemMessage.Content);
        Assert.Contains("Separate confirmed facts from assumptions", systemMessage.Content);
        Assert.Contains("Additional files needed", systemMessage.Content);

        var corpusMessage = fake.CapturedMessages.First(m => m.Role == "user" && m.Content.Contains("<corpus>"));
        Assert.Contains("source data only", corpusMessage.Content);
    }

    [Fact]
    public async Task StreamsChunksToStdout_AndStripsSplitAnsiSequences()
    {
        var file = Path.Combine(_tempDir, "sample.cs");
        await File.WriteAllTextAsync(file, "public class Foo {}");

        var fake = new FakeChatClient
        {
            ResponseChunks = ["hel\u001b[", "31mlo", " wor", "ld"],
        };

        var originalOut = Console.Out;
        var stdout = new StringWriter();
        Console.SetOut(stdout);
        try
        {
            var exit = await AskCommand.RunAsync(
                [file],
                "What is this?",
                512,
                null,
                TestAigestConfig.Create(),
                fake);

            Assert.Equal(0, exit);
            Assert.Equal($"hello world{Environment.NewLine}", stdout.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task PerFolder_DispatchesOneCallPerSubfolder_WithScopeDeclaration()
    {
        var commands = Path.Combine(_tempDir, "Commands");
        var core = Path.Combine(_tempDir, "Core");
        Directory.CreateDirectory(commands);
        Directory.CreateDirectory(core);
        await File.WriteAllTextAsync(Path.Combine(commands, "Ask.cs"), "// a");
        await File.WriteAllTextAsync(Path.Combine(core, "Loader.cs"), "// l");

        var fake = new FakeChatClient
        {
            ResponseFor = msgs =>
            {
                var corpus = msgs.First(m => m.Role == "user").Content;
                return corpus.Contains("Commands") ? ["Commands analysis"] : ["Core analysis"];
            },
        };

        var originalOut = Console.Out;
        var stdout = new StringWriter();
        Console.SetOut(stdout);
        try
        {
            var exit = await AskCommand.RunAsync(
                [_tempDir],
                "What does each folder do?",
                512,
                null,
                TestAigestConfig.Create(),
                fake,
                logger: null,
                cancellationToken: CancellationToken.None,
                perFolder: true);

            Assert.Equal(0, exit);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Equal(2, fake.CallCount);

        var captures = fake.Captures.ToArray();
        foreach (var capture in captures)
        {
            var systemMessage = capture.Messages.First(m => m.Role == "system");
            Assert.Contains("Folder-scope rules (per-folder mode)", systemMessage.Content);
            Assert.Contains("declared folder scope", systemMessage.Content);
            Assert.Contains("Additional files needed", systemMessage.Content);
            Assert.Contains("folder-level briefing", systemMessage.Content);

            var corpusMessage = capture.Messages.First(m => m.Role == "user" && m.Content.Contains("<scope"));
            Assert.Contains("<scope folder=", corpusMessage.Content);
            Assert.Contains("</scope>", corpusMessage.Content);
        }

        var output = stdout.ToString();
        Assert.Contains("## Folder:", output);
        Assert.Contains("Commands analysis", output);
        Assert.Contains("Core analysis", output);
    }

    [Fact]
    public async Task PerFolder_RespectsMaxParallelFolders()
    {
        for (int i = 0; i < 5; i++)
        {
            var d = Path.Combine(_tempDir, $"Folder{i}");
            Directory.CreateDirectory(d);
            await File.WriteAllTextAsync(Path.Combine(d, "f.cs"), $"// {i}");
        }

        using var startedAll = new SemaphoreSlim(0, 5);
        using var allowFinish = new ManualResetEventSlim(false);

        var fake = new FakeChatClient
        {
            OnCallStart = async _ =>
            {
                startedAll.Release();
                await Task.Run(() => allowFinish.Wait());
            },
        };

        var config = TestAigestConfig.Create() with { MaxParallelFolders = 2 };

        var originalOut = Console.Out;
        Console.SetOut(new StringWriter());
        try
        {
            var runTask = AskCommand.RunAsync(
                [_tempDir],
                "?",
                256,
                null,
                config,
                fake,
                logger: null,
                cancellationToken: CancellationToken.None,
                perFolder: true);

            await startedAll.WaitAsync(TimeSpan.FromSeconds(5));
            await startedAll.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(100);

            Assert.True(fake.MaxObservedConcurrency <= 2,
                $"Concurrency cap violated: observed {fake.MaxObservedConcurrency}");

            allowFinish.Set();
            await runTask;
        }
        finally
        {
            allowFinish.Set();
            Console.SetOut(originalOut);
        }

        Assert.Equal(5, fake.CallCount);
        Assert.True(fake.MaxObservedConcurrency >= 2,
            "At least two folders should have run concurrently");
    }

    [Fact]
    public async Task PerFolder_OneFolderFails_OthersStillReportAndExitNonZero()
    {
        var goodDir = Path.Combine(_tempDir, "GoodFolder");
        var badDir = Path.Combine(_tempDir, "BadFolder");
        Directory.CreateDirectory(goodDir);
        Directory.CreateDirectory(badDir);
        await File.WriteAllTextAsync(Path.Combine(goodDir, "g.cs"), "// good");
        await File.WriteAllTextAsync(Path.Combine(badDir, "b.cs"), "// bad");

        var fake = new FakeChatClient
        {
            FailWhen = msgs =>
            {
                var corpus = msgs.First(m => m.Role == "user").Content;
                return corpus.Contains("BadFolder")
                    ? new InvalidOperationException("simulated upstream failure")
                    : null;
            },
            ResponseFor = msgs => ["good output"],
        };

        var originalOut = Console.Out;
        var stdout = new StringWriter();
        Console.SetOut(stdout);
        int exit;
        try
        {
            exit = await AskCommand.RunAsync(
                [_tempDir],
                "?",
                256,
                null,
                TestAigestConfig.Create(),
                fake,
                logger: null,
                cancellationToken: CancellationToken.None,
                perFolder: true);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Equal(1, exit);
        var output = stdout.ToString();
        Assert.Contains("good output", output);
        Assert.Contains("Failed: simulated upstream failure", output);
    }
}
