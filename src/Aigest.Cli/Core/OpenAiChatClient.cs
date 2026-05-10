using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Aigest.Cli.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI;
using OpenAI.Chat;

namespace Aigest.Cli.Core;

public sealed class OpenAiChatClient : IChatClient, IDisposable
{
    private readonly ChatClient _client;
    private readonly AigestConfig _config;
    private readonly ILogger<OpenAiChatClient> _logger;

    /// <summary>
    /// Original single-parameter constructor. Used by tests and legacy callers.
    /// </summary>
    public OpenAiChatClient(AigestConfig config)
    {
        var uri = ValidateScheme(config.BaseUrl);

        var options = new OpenAIClientOptions
        {
            Endpoint = uri,
            NetworkTimeout = TimeSpan.FromSeconds(config.TimeoutSeconds),
        };
        options.AddPolicy(new ResponseCapturePolicy(), PipelinePosition.PerCall);

        _client = new ChatClient(
            config.Model,
            new ApiKeyCredential(config.ApiKey),
            options);

        _config = config;
        _logger = NullLogger<OpenAiChatClient>.Instance;
    }

    /// <summary>
    /// DI-friendly constructor that accepts an injected HttpClient (for Polly resilience) and an ILogger.
    /// Timeout is enforced via HttpClient.Timeout, which CliHost sets from AigestConfig.TimeoutSeconds.
    /// NetworkTimeout on OpenAIClientOptions has no effect when a custom transport is supplied.
    /// </summary>
    public OpenAiChatClient(AigestConfig config, HttpClient httpClient, ILogger<OpenAiChatClient> logger)
    {
        var uri = ValidateScheme(config.BaseUrl);

        var options = new OpenAIClientOptions { Endpoint = uri };
        options.Transport = new HttpClientPipelineTransport(httpClient);
        options.AddPolicy(new ResponseCapturePolicy(), PipelinePosition.PerCall);

        _client = new ChatClient(
            config.Model,
            new ApiKeyCredential(config.ApiKey),
            options);

        _config = config;
        _logger = logger;
    }

    public async IAsyncEnumerable<string> CompleteStreamingAsync(
        IReadOnlyList<ChatMessage> messages,
        int maxTokens,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Per-call capture isolates parallel CompleteStreamingAsync invocations
        // (used by --per-folder mode) so they don't share a single MemoryStream
        // buffer through ResponseCapturePolicy.
        var capture = new ResponseCapture();
        var previousCapture = ResponseCapturePolicy.Current.Value;
        ResponseCapturePolicy.Current.Value = capture;
        try
        {
            var chatMessages = new List<OpenAI.Chat.ChatMessage>();
            foreach (var m in messages)
            {
                chatMessages.Add(m.Role.ToLowerInvariant() switch
                {
                    "system" => new SystemChatMessage(m.Content),
                    "user" => new UserChatMessage(m.Content),
                    "assistant" => new AssistantChatMessage(m.Content),
                    _ => new UserChatMessage(m.Content),
                });
            }

            var options = new ChatCompletionOptions
            {
                MaxOutputTokenCount = maxTokens,
            };

            if (_config.ThinkingEffort is { } effort && !_config.IsLocal)
            {
#pragma warning disable OPENAI001
                // String constructor used instead of typed statics — forward-compat with xhigh and future levels.
                options.ReasoningEffortLevel = new ChatReasoningEffortLevel(effort);
#pragma warning restore OPENAI001
            }

            var emittedText = false;
            ChatTokenUsage? usage = null;
            string? model = null;

            var updates = _client.CompleteChatStreamingAsync(
                chatMessages,
                options,
                cancellationToken);

            await foreach (var update in updates.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                model = update.Model ?? model;
                usage = update.Usage ?? usage;
                foreach (var contentPart in update.ContentUpdate)
                {
                    if (string.IsNullOrEmpty(contentPart.Text))
                        continue;

                    emittedText = true;
                    yield return contentPart.Text;
                }
            }

            if (!emittedText)
            {
                var reasoningTokens = usage?.OutputTokenDetails?.ReasoningTokenCount ?? 0;
                if (reasoningTokens > 0)
                {
                    throw new InvalidOperationException(
                        $"Model returned no visible content: spent all {maxTokens} max-output tokens on hidden reasoning " +
                        $"({reasoningTokens} reasoning tokens). Raise AIGEST_MAX_TOKENS (or pass --max-tokens) " +
                        $"or lower AIGEST_THINKING_EFFORT (currently '{_config.ThinkingEffort ?? "unset"}').");
                }
                throw new InvalidOperationException(
                    "LLM returned an empty response. Try a higher --max-tokens / AIGEST_MAX_TOKENS value.");
            }

            var cacheUsage = DeepSeekUsageParser.Parse(capture.ToArray());
            if (cacheUsage.CacheHitTokens == 0 && usage?.InputTokenDetails?.CachedTokenCount is int cachedTokens)
                cacheUsage = cacheUsage with { CacheHitTokens = cachedTokens };

            CliLog.LlmCallComplete(
                _logger,
                model: model ?? _config.Model,
                promptTokens: usage?.InputTokenCount ?? 0,
                completionTokens: usage?.OutputTokenCount ?? 0,
                cacheHitTokens: cacheUsage.CacheHitTokens,
                cacheMissTokens: cacheUsage.CacheMissTokens);
        }
        finally
        {
            ResponseCapturePolicy.Current.Value = previousCapture;
        }
    }

    public void Dispose()
    {
        // NOTE: When using the DI constructor, the injected HttpClient is managed by IHttpClientFactory.
        // The ChatClient/transport may attempt to dispose it; for a CLI (single-shot), this is harmless
        // since the process exits immediately after. Do not call Dispose() on long-lived DI registrations.
        (_client as IDisposable)?.Dispose();
    }

    private static Uri ValidateScheme(string baseUrl)
    {
        var uri = new Uri(baseUrl);
        if (uri.Scheme is not ("https" or "http"))
            throw new InvalidOperationException(
                $"AIGEST_BASE_URL scheme '{uri.Scheme}' is not allowed. Use https or http.");
        return uri;
    }
}
