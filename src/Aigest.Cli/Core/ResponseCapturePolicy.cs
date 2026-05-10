using System.ClientModel.Primitives;

namespace Aigest.Cli.Core;

internal sealed class ResponseCapturePolicy : PipelinePolicy
{
    // Per-async-flow capture target. Lets a single policy instance (shared across
    // parallel CompleteStreamingAsync calls) tee each call's response into its own
    // ResponseCapture without cross-contamination or thread-safety bugs in
    // MemoryStream. Set/restored by OpenAiChatClient.CompleteStreamingAsync.
    internal static readonly AsyncLocal<ResponseCapture?> Current = new();

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        ProcessNext(message, pipeline, currentIndex);
        WrapContentStream(message);
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
        WrapContentStream(message);
    }

    internal static void WrapContentStream(PipelineMessage message)
    {
        var capture = Current.Value;
        if (capture is null) return;

        var response = message.Response;
        if (response?.ContentStream is null)
            return;

        response.ContentStream = new TeeReadStream(response.ContentStream, capture);
    }
}

internal sealed class ResponseCapture
{
    private const int MaxBytes = 1_000_000;
    private readonly MemoryStream _buffer = new();

    internal void Append(ReadOnlySpan<byte> bytes)
    {
        if (_buffer.Length >= MaxBytes)
            return;

        var remaining = MaxBytes - (int)_buffer.Length;
        _buffer.Write(bytes[..Math.Min(bytes.Length, remaining)]);
    }

    internal void Clear() => _buffer.SetLength(0);

    internal byte[] ToArray() => _buffer.ToArray();
}

internal sealed class TeeReadStream : Stream
{
    private readonly Stream _inner;
    private readonly ResponseCapture _capture;

    internal TeeReadStream(Stream inner, ResponseCapture capture)
    {
        _inner = inner;
        _capture = capture;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => _inner.Flush();

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);
        if (read > 0)
            _capture.Append(buffer.AsSpan(offset, read));
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        var read = _inner.Read(buffer);
        if (read > 0)
            _capture.Append(buffer[..read]);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (read > 0)
            _capture.Append(buffer.Span[..read]);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();
        base.Dispose(disposing);
    }
}
