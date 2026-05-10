using Aigest.Cli.Core;

namespace Aigest.Cli.Tests;

internal static class TestAigestConfig
{
    internal static AigestConfig Create() =>
        new(
            ApiKey: "test-key",
            BaseUrl: "https://test.example",
            Model: "test-model",
            MaxFileBytes: 1_000_000,
            MaxTotalBytes: 4_000_000,
            TimeoutSeconds: 60,
            Debug: false);
}
