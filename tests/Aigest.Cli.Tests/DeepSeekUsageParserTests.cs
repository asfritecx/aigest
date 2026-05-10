using System.Text;
using Aigest.Cli.Core;

namespace Aigest.Cli.Tests;

public class DeepSeekUsageParserTests
{
    [Fact]
    public void Parse_ReadsDeepSeekSseUsage()
    {
        var bytes = Encoding.UTF8.GetBytes("""
data: {"choices":[{"delta":{"content":"hello"}}]}

data: {"usage":{"prompt_cache_hit_tokens":12,"prompt_cache_miss_tokens":34}}

data: [DONE]

""");

        var usage = DeepSeekUsageParser.Parse(bytes);

        Assert.Equal(12, usage.CacheHitTokens);
        Assert.Equal(34, usage.CacheMissTokens);
    }

    [Fact]
    public void Parse_ReadsOpenAiCachedTokensAsHitOnly()
    {
        var bytes = Encoding.UTF8.GetBytes("""
{"usage":{"prompt_tokens":100,"prompt_token_details":{"cached_tokens":45}}}
""");

        var usage = DeepSeekUsageParser.Parse(bytes);

        Assert.Equal(45, usage.CacheHitTokens);
        Assert.Equal(0, usage.CacheMissTokens);
    }

    [Fact]
    public void Parse_DefaultsToZero_WhenCacheFieldsAbsent()
    {
        var bytes = Encoding.UTF8.GetBytes("""
{"usage":{"prompt_tokens":100,"completion_tokens":10}}
""");

        var usage = DeepSeekUsageParser.Parse(bytes);

        Assert.Equal(0, usage.CacheHitTokens);
        Assert.Equal(0, usage.CacheMissTokens);
    }

    [Fact]
    public void Parse_DoesNotThrow_WhenIntermediateSseChunksHaveNullUsage()
    {
        // OpenAI spec: "usage" is null on all chunks except the last one.
        var bytes = Encoding.UTF8.GetBytes("""
data: {"choices":[{"delta":{"content":"hello"}}],"usage":null}

data: {"choices":[{"delta":{"content":" world"}}],"usage":null}

data: {"choices":[],"usage":{"prompt_cache_hit_tokens":10,"prompt_cache_miss_tokens":20}}

data: [DONE]

""");

        var usage = DeepSeekUsageParser.Parse(bytes);

        Assert.Equal(10, usage.CacheHitTokens);
        Assert.Equal(20, usage.CacheMissTokens);
    }

    [Fact]
    public void Parse_ReturnsDefault_WhenRootUsageIsNull()
    {
        var bytes = Encoding.UTF8.GetBytes("""{"usage":null}""");

        var usage = DeepSeekUsageParser.Parse(bytes);

        Assert.Equal(0, usage.CacheHitTokens);
        Assert.Equal(0, usage.CacheMissTokens);
    }
}
