using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aigest.Cli.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aigest.Cli.Tests;

public class OpenAiChatClientReasoningEffortTests
{
    // Minimal valid OpenAI SSE response that yields one text chunk then terminates.
    private const string SseResponse =
        "data: {\"id\":\"1\",\"object\":\"chat.completion.chunk\",\"model\":\"m\"," +
        "\"choices\":[{\"delta\":{\"content\":\"hi\"},\"index\":0,\"finish_reason\":null}]}\n\n" +
        "data: [DONE]\n\n";

    [Fact]
    public async Task CompleteStreamingAsync_OmitsReasoningEffort_WhenThinkingEffortIsNull()
    {
        string? capturedBody = null;
        using var handler = new CaptureHandler(SseResponse, body => capturedBody = body);
        using var httpClient = new HttpClient(handler);

        var config = new AigestConfig("key", "https://api.example.com", "model", 1, 1, 120, false);
        var client = new OpenAiChatClient(config, httpClient, NullLogger<OpenAiChatClient>.Instance);

        var messages = new List<ChatMessage> { new() { Role = "user", Content = "hello" } };
        await foreach (var _ in client.CompleteStreamingAsync(messages, 100, CancellationToken.None)) { }

        Assert.NotNull(capturedBody);
        Assert.DoesNotContain("reasoning_effort", capturedBody);
    }

    [Fact]
    public async Task CompleteStreamingAsync_SendsReasoningEffortMedium_WhenThinkingEffortIsSet()
    {
        string? capturedBody = null;
        using var handler = new CaptureHandler(SseResponse, body => capturedBody = body);
        using var httpClient = new HttpClient(handler);

        var config = new AigestConfig("key", "https://api.example.com", "model", 1, 1, 120, false, "medium");
        var client = new OpenAiChatClient(config, httpClient, NullLogger<OpenAiChatClient>.Instance);

        var messages = new List<ChatMessage> { new() { Role = "user", Content = "hello" } };
        await foreach (var _ in client.CompleteStreamingAsync(messages, 100, CancellationToken.None)) { }

        Assert.NotNull(capturedBody);
        Assert.Contains("reasoning_effort", capturedBody);
        Assert.Contains("medium", capturedBody);
    }

    [Fact]
    public async Task CompleteStreamingAsync_SendsReasoningEffortXHigh_WhenThinkingEffortIsXHigh()
    {
        string? capturedBody = null;
        using var handler = new CaptureHandler(SseResponse, body => capturedBody = body);
        using var httpClient = new HttpClient(handler);

        var config = new AigestConfig("key", "https://api.example.com", "model", 1, 1, 120, false, "xhigh");
        var client = new OpenAiChatClient(config, httpClient, NullLogger<OpenAiChatClient>.Instance);

        var messages = new List<ChatMessage> { new() { Role = "user", Content = "hello" } };
        await foreach (var _ in client.CompleteStreamingAsync(messages, 100, CancellationToken.None)) { }

        Assert.NotNull(capturedBody);
        Assert.Contains("reasoning_effort", capturedBody);
        Assert.Contains("xhigh", capturedBody);
    }

    [Fact]
    public async Task CompleteStreamingAsync_OmitsReasoningEffort_InLocalProviderMode()
    {
        string? capturedBody = null;
        using var handler = new CaptureHandler(SseResponse, body => capturedBody = body);
        using var httpClient = new HttpClient(handler);

        // Provider="local" must suppress reasoning_effort even when ThinkingEffort is set —
        // most local OpenAI-compat servers (Ollama, LM Studio) reject or mishandle the field.
        var config = new AigestConfig(
            ApiKey: "ollama",
            BaseUrl: "http://localhost:11434/v1",
            Model: "llama3.2",
            MaxFileBytes: 1,
            MaxTotalBytes: 1,
            TimeoutSeconds: 600,
            Debug: false,
            ThinkingEffort: "medium",
            MaxParallelFolders: 4,
            Provider: "local");
        var client = new OpenAiChatClient(config, httpClient, NullLogger<OpenAiChatClient>.Instance);

        var messages = new List<ChatMessage> { new() { Role = "user", Content = "hello" } };
        await foreach (var _ in client.CompleteStreamingAsync(messages, 100, CancellationToken.None)) { }

        Assert.NotNull(capturedBody);
        Assert.DoesNotContain("reasoning_effort", capturedBody);
    }

    [Fact]
    public async Task CompleteStreamingAsync_ForwardsReasoningEffort_InAzureProviderMode()
    {
        string? capturedBody = null;
        using var handler = new CaptureHandler(SseResponse, body => capturedBody = body);
        using var httpClient = new HttpClient(handler);

        // Provider="azure" must forward reasoning_effort when ThinkingEffort is set.
        var config = new AigestConfig(
            ApiKey: "azure-key",
            BaseUrl: "https://example.openai.azure.com/",
            Model: "gpt-4-turbo",
            MaxFileBytes: 1,
            MaxTotalBytes: 1,
            TimeoutSeconds: 600,
            Debug: false,
            ThinkingEffort: "medium",
            MaxParallelFolders: 4,
            Provider: "azure");
        var client = new OpenAiChatClient(config, httpClient, NullLogger<OpenAiChatClient>.Instance);

        var messages = new List<ChatMessage> { new() { Role = "user", Content = "hello" } };
        await foreach (var _ in client.CompleteStreamingAsync(messages, 100, CancellationToken.None)) { }

        Assert.NotNull(capturedBody);
        Assert.Contains("reasoning_effort", capturedBody);
        Assert.Contains("medium", capturedBody);
    }

    [Fact]
    public async Task CompleteStreamingAsync_ReportsReasoningExhaustion_WhenContentIsEmptyButReasoningTokensExist()
    {
        const string reasoningExhaustedSse =
            "data: {\"id\":\"1\",\"object\":\"chat.completion.chunk\",\"model\":\"m\"," +
            "\"choices\":[{\"delta\":{},\"index\":0,\"finish_reason\":\"length\"}]}\n\n" +
            "data: {\"id\":\"1\",\"object\":\"chat.completion.chunk\",\"model\":\"m\"," +
            "\"choices\":[],\"usage\":{\"prompt_tokens\":50,\"completion_tokens\":1024,\"total_tokens\":1074," +
            "\"completion_tokens_details\":{\"reasoning_tokens\":1024}}}\n\n" +
            "data: [DONE]\n\n";

        using var handler = new CaptureHandler(reasoningExhaustedSse, _ => { });
        using var httpClient = new HttpClient(handler);

        var config = new AigestConfig(
            ApiKey: "key", BaseUrl: "https://example.openai.azure.com/", Model: "gpt-5",
            MaxFileBytes: 1, MaxTotalBytes: 1, TimeoutSeconds: 120, Debug: false,
            ThinkingEffort: "high", MaxParallelFolders: 4, Provider: "azure");
        var client = new OpenAiChatClient(config, httpClient, NullLogger<OpenAiChatClient>.Instance);

        var messages = new List<ChatMessage> { new() { Role = "user", Content = "hi" } };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in client.CompleteStreamingAsync(messages, 1024, CancellationToken.None)) { }
        });

        Assert.Contains("reasoning", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AIGEST_MAX_TOKENS", ex.Message);
        Assert.Contains("high", ex.Message);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly string _responseBody;
        private readonly Action<string> _captureBody;

        public CaptureHandler(string responseBody, Action<string> captureBody)
        {
            _responseBody = responseBody;
            _captureBody = captureBody;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                var body = await request.Content.ReadAsStringAsync(cancellationToken);
                _captureBody(body);
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent(_responseBody, Encoding.UTF8, "text/event-stream");
            return response;
        }
    }
}
