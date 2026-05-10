using Aigest.Cli;
using Aigest.Cli.Core;
using Xunit;

namespace Aigest.Cli.Tests;

public class MaxTokensResolverTests
{
    [Fact]
    public void ResolveMaxTokens_ExplicitValueWins_WhenPositive()
    {
        var config = new AigestConfig(
            ApiKey: "test-key",
            BaseUrl: "https://test.example",
            Model: "test-model",
            MaxFileBytes: 1_000_000,
            MaxTotalBytes: 4_000_000,
            TimeoutSeconds: 60,
            Debug: false,
            MaxOutputTokens: 10000);

        var result = Program.ResolveMaxTokens(5000, config, fallback: 8192);

        Assert.Equal(5000, result);
    }

    [Fact]
    public void ResolveMaxTokens_ConfigValueWins_WhenExplicitIsZero()
    {
        var config = new AigestConfig(
            ApiKey: "test-key",
            BaseUrl: "https://test.example",
            Model: "test-model",
            MaxFileBytes: 1_000_000,
            MaxTotalBytes: 4_000_000,
            TimeoutSeconds: 60,
            Debug: false,
            MaxOutputTokens: 10000);

        var result = Program.ResolveMaxTokens(0, config, fallback: 8192);

        Assert.Equal(10000, result);
    }

    [Fact]
    public void ResolveMaxTokens_FallbackUsed_WhenBothExplicitAndConfigAreZero()
    {
        var config = new AigestConfig(
            ApiKey: "test-key",
            BaseUrl: "https://test.example",
            Model: "test-model",
            MaxFileBytes: 1_000_000,
            MaxTotalBytes: 4_000_000,
            TimeoutSeconds: 60,
            Debug: false,
            MaxOutputTokens: 0);

        var result = Program.ResolveMaxTokens(0, config, fallback: 8192);

        Assert.Equal(8192, result);
    }
}
