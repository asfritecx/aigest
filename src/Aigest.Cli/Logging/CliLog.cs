using Microsoft.Extensions.Logging;

namespace Aigest.Cli.Logging;

internal static partial class CliLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Included {FileCount} file(s).")]
    internal static partial void IncludedFiles(ILogger logger, int fileCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "LLM call complete. Model={Model} PromptTokens={PromptTokens} CompletionTokens={CompletionTokens} CacheHit={CacheHitTokens} CacheMiss={CacheMissTokens}")]
    internal static partial void LlmCallComplete(ILogger logger, string model, int promptTokens, int completionTokens, int cacheHitTokens, int cacheMissTokens);

    [LoggerMessage(Level = LogLevel.Information, Message = "Per-folder mode: dispatching {FolderCount} folder(s) with concurrency={MaxParallel}.")]
    internal static partial void PerFolderDispatched(ILogger logger, int folderCount, int maxParallel);

    [LoggerMessage(Level = LogLevel.Information, Message = "Per-folder mode: completed folder '{FolderPath}'.")]
    internal static partial void PerFolderCompleted(ILogger logger, string folderPath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Per-folder mode: folder '{FolderPath}' failed: {Reason}")]
    internal static partial void PerFolderFailed(ILogger logger, string folderPath, string reason);
}
