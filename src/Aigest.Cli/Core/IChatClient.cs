using System.Collections.Generic;
using System.Threading;

namespace Aigest.Cli.Core;

public sealed class ChatMessage
{
    public required string Role { get; init; }
    public required string Content { get; init; }
}

public interface IChatClient
{
    IAsyncEnumerable<string> CompleteStreamingAsync(
        IReadOnlyList<ChatMessage> messages,
        int maxTokens,
        CancellationToken cancellationToken = default);
}
