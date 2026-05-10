using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Aigest.Cli.Core;
using Xunit;

namespace Aigest.Cli.Tests;

public class ResponseCapturePolicyTests
{
    [Fact]
    public async Task Capture_IsolatesParallelFlows_NoCrossContamination()
    {
        // Mimics what OpenAiChatClient.CompleteStreamingAsync does per call: set an
        // AsyncLocal capture, stream through a TeeReadStream wired to that capture,
        // then read capture.ToArray(). With a single shared MemoryStream (the old
        // design) the parallel writes would interleave or throw; with per-call
        // captures, each flow sees only its own bytes.
        async Task<byte[]> RunOneAsync(byte fillByte)
        {
            var capture = new ResponseCapture();
            var previous = ResponseCapturePolicy.Current.Value;
            ResponseCapturePolicy.Current.Value = capture;
            try
            {
                var payload = new byte[8192];
                Array.Fill(payload, fillByte);
                using var inner = new MemoryStream(payload);
                using var tee = new TeeReadStream(inner, ResponseCapturePolicy.Current.Value!);

                var buffer = new byte[1024];
                int total = 0;
                while (true)
                {
                    int read = await tee.ReadAsync(buffer.AsMemory());
                    if (read == 0) break;
                    total += read;
                    // Yield to amplify interleaving with sibling tasks.
                    await Task.Yield();
                }

                Assert.Equal(payload.Length, total);
                return capture.ToArray();
            }
            finally
            {
                ResponseCapturePolicy.Current.Value = previous;
            }
        }

        var tasks = Enumerable.Range(1, 32)
            .Select(i => Task.Run(() => RunOneAsync((byte)i)))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        for (int i = 0; i < results.Length; i++)
        {
            var captured = results[i];
            var expectedByte = (byte)(i + 1);
            Assert.Equal(8192, captured.Length);
            Assert.True(
                captured.All(b => b == expectedByte),
                $"task {i + 1}: capture contained a foreign byte (expected all {expectedByte})");
        }
    }

    [Fact]
    public async Task Current_RestoresPreviousValue_AfterCallFlow()
    {
        Assert.Null(ResponseCapturePolicy.Current.Value);

        var outer = new ResponseCapture();
        ResponseCapturePolicy.Current.Value = outer;
        try
        {
            await Task.Run(() =>
            {
                Assert.Same(outer, ResponseCapturePolicy.Current.Value);

                var inner = new ResponseCapture();
                var previous = ResponseCapturePolicy.Current.Value;
                ResponseCapturePolicy.Current.Value = inner;
                try
                {
                    Assert.Same(inner, ResponseCapturePolicy.Current.Value);
                }
                finally
                {
                    ResponseCapturePolicy.Current.Value = previous;
                }

                Assert.Same(outer, ResponseCapturePolicy.Current.Value);
            });
        }
        finally
        {
            ResponseCapturePolicy.Current.Value = null;
        }
    }

    [Fact]
    public async Task TeeReadStream_AppendsExactlyOnce_PerByteRead()
    {
        var capture = new ResponseCapture();
        var payload = "hello world"u8.ToArray();
        using var inner = new MemoryStream(payload);
        using var tee = new TeeReadStream(inner, capture);

        var buffer = new byte[64];
        int read = await tee.ReadAsync(buffer.AsMemory());

        Assert.Equal(payload.Length, read);
        Assert.Equal(payload, capture.ToArray());
    }
}
