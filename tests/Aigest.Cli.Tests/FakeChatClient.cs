using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Aigest.Cli.Core;

namespace Aigest.Cli.Tests;

public sealed class FakeChatClient : IChatClient
{
    public sealed record Capture(IReadOnlyList<ChatMessage> Messages, int MaxTokens);

    private readonly ConcurrentQueue<Capture> _captures = new();
    private readonly object _lock = new();
    private int _activeCalls;
    private int _maxObservedConcurrency;

    public List<ChatMessage> CapturedMessages { get; } = [];
    public int CapturedMaxTokens { get; set; }
    public string Response { get; set; } = "fake response";
    public IReadOnlyList<string>? ResponseChunks { get; set; }

    /// Per-call response selector. If set, takes precedence over ResponseChunks/Response.
    public Func<IReadOnlyList<ChatMessage>, IReadOnlyList<string>?>? ResponseFor { get; set; }

    /// Per-call hook awaited before any chunk is yielded — useful for gating to observe concurrency.
    public Func<IReadOnlyList<ChatMessage>, Task>? OnCallStart { get; set; }

    /// Per-call hook awaited after all chunks are yielded.
    public Func<IReadOnlyList<ChatMessage>, Task>? OnCallEnd { get; set; }

    /// If set, the first call whose snapshot makes this predicate true will throw.
    public Func<IReadOnlyList<ChatMessage>, Exception?>? FailWhen { get; set; }

    public IReadOnlyCollection<Capture> Captures => _captures.ToArray();
    public int CallCount => _captures.Count;
    public int MaxObservedConcurrency => Volatile.Read(ref _maxObservedConcurrency);

    public async IAsyncEnumerable<string> CompleteStreamingAsync(
        IReadOnlyList<ChatMessage> messages,
        int maxTokens,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var snapshot = messages.ToList();
        _captures.Enqueue(new Capture(snapshot, maxTokens));

        lock (_lock)
        {
            CapturedMessages.AddRange(snapshot);
            CapturedMaxTokens = maxTokens;

            _activeCalls++;
            if (_activeCalls > _maxObservedConcurrency)
                _maxObservedConcurrency = _activeCalls;
        }

        try
        {
            if (FailWhen?.Invoke(snapshot) is { } injected)
                throw injected;

            if (OnCallStart is not null)
                await OnCallStart(snapshot);

            var chunks = ResponseFor?.Invoke(snapshot) ?? ResponseChunks ?? [Response];
            foreach (var chunk in chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return chunk;
            }

            if (OnCallEnd is not null)
                await OnCallEnd(snapshot);
        }
        finally
        {
            lock (_lock) { _activeCalls--; }
        }
    }
}
