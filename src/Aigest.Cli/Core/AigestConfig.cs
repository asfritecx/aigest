namespace Aigest.Cli.Core;

public sealed record AigestConfig(
    string ApiKey,
    string BaseUrl,
    string Model,
    int MaxFileBytes,
    int MaxTotalBytes,
    int TimeoutSeconds,
    bool Debug,
    string? ThinkingEffort = null,
    int MaxParallelFolders = 4,
    string? Provider = null,
    int MaxOutputTokens = 0)
{
    public bool IsLocal => string.Equals(Provider, "local", System.StringComparison.OrdinalIgnoreCase);
    public bool IsAzure => string.Equals(Provider, "azure", System.StringComparison.OrdinalIgnoreCase);
}
