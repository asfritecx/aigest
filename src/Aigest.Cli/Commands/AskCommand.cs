using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aigest.Cli.Core;
using Aigest.Cli.Logging;
using Microsoft.Extensions.Logging;

namespace Aigest.Cli.Commands;

public static class AskCommand
{
    public static async Task<int> RunAsync(
        IReadOnlyList<string> paths,
        string question,
        int maxTokens,
        IReadOnlyList<string>? denyPatterns,
        AigestConfig config,
        IChatClient client,
        ILogger? logger = null,
        CancellationToken cancellationToken = default,
        bool perFolder = false)
    {
        var details = CorpusLoader.LoadFiles(
            paths,
            config.MaxFileBytes,
            config.MaxTotalBytes,
            denyPatterns,
            logger);

        if (details.Files.Count == 0)
        {
            throw new InvalidOperationException(
                "No readable files were included. Check paths, denylist, and size limits.");
        }

        if (logger is null)
            Console.Error.WriteLine($"[info] Included {details.Files.Count} file(s).");

        if (!perFolder)
        {
            var corpus = CorpusLoader.RenderCorpus(details.Files);
            var messages = AskPromptBuilder.BuildDefault(corpus, question);
            await StreamToStdoutAsync(client, messages, maxTokens, cancellationToken);
            return 0;
        }

        var groups = FolderGrouper.Group(details.Files);
        if (groups.Count <= 1)
        {
            if (logger is not null)
                CliLog.PerFolderDispatched(logger, groups.Count, config.MaxParallelFolders);
            else
                Console.Error.WriteLine($"[info] Per-folder mode: only {groups.Count} folder(s); falling back to single call.");

            var corpus = CorpusLoader.RenderCorpus(details.Files);
            var messages = AskPromptBuilder.BuildDefault(corpus, question);
            await StreamToStdoutAsync(client, messages, maxTokens, cancellationToken);
            return 0;
        }

        if (logger is not null)
            CliLog.PerFolderDispatched(logger, groups.Count, config.MaxParallelFolders);
        else
            Console.Error.WriteLine($"[info] Per-folder mode: dispatching {groups.Count} folder(s), concurrency={config.MaxParallelFolders}.");

        return await RunPerFolderAsync(groups, question, maxTokens, config, client, logger, cancellationToken);
    }

    private static async Task<int> RunPerFolderAsync(
        IReadOnlyList<FolderGroup> groups,
        string question,
        int maxTokens,
        AigestConfig config,
        IChatClient client,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var concurrency = Math.Max(1, config.MaxParallelFolders);
        using var gate = new SemaphoreSlim(concurrency, concurrency);

        var tasks = groups
            .Select((group, index) => RunOneFolderAsync(group, index, question, maxTokens, client, gate, logger, cancellationToken))
            .ToList();

        var results = await Task.WhenAll(tasks);

        var anyFailed = false;
        var stripper = new StreamingAnsiStripper();
        for (int i = 0; i < results.Length; i++)
        {
            var (group, body, error) = results[i];
            if (i > 0) Console.WriteLine();
            Console.WriteLine($"## Folder: {group.FolderPath}");
            Console.WriteLine();

            if (error is not null)
            {
                anyFailed = true;
                Console.WriteLine($"_Failed: {error}_");
                continue;
            }

            var stripped = stripper.StripChunk(body);
            if (stripped.Length > 0)
                Console.Write(stripped);
            var tail = stripper.Flush();
            if (tail.Length > 0)
                Console.Write(tail);
            if (body.Length == 0 || body[^1] != '\n')
                Console.WriteLine();
        }

        Console.Out.Flush();
        return anyFailed ? 1 : 0;
    }

    private static async Task<(FolderGroup Group, string Body, string? Error)> RunOneFolderAsync(
        FolderGroup group,
        int index,
        string question,
        int maxTokens,
        IChatClient client,
        SemaphoreSlim gate,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var messages = AskPromptBuilder.BuildPerFolder(group, question);
            var sb = new StringBuilder();
            await foreach (var chunk in client.CompleteStreamingAsync(messages, maxTokens, cancellationToken))
            {
                sb.Append(chunk);
            }

            if (logger is not null)
                CliLog.PerFolderCompleted(logger, group.FolderPath);

            return (group, sb.ToString(), null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (logger is not null)
                CliLog.PerFolderFailed(logger, group.FolderPath, ex.Message);
            return (group, string.Empty, ex.Message);
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task StreamToStdoutAsync(
        IChatClient client,
        IReadOnlyList<ChatMessage> messages,
        int maxTokens,
        CancellationToken cancellationToken)
    {
        var stripper = new StreamingAnsiStripper();
        await foreach (var chunk in client.CompleteStreamingAsync(messages, maxTokens, cancellationToken))
        {
            var stripped = stripper.StripChunk(chunk);
            if (stripped.Length == 0)
                continue;

            Console.Write(stripped);
            Console.Out.Flush();
        }

        var tail = stripper.Flush();
        if (tail.Length > 0)
        {
            Console.Write(tail);
            Console.Out.Flush();
        }

        Console.WriteLine();
    }
}
