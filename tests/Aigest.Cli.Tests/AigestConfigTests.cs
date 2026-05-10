using Aigest.Cli.Core;
using Xunit;

namespace Aigest.Cli.Tests;

public class AigestConfigTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("cloud")]
    [InlineData("CLOUD")]
    [InlineData("foo")]
    public void IsAzure_IsFalse_ForNonAzureProvider(string? provider)
    {
        var config = TestAigestConfig.Create() with { Provider = provider };

        Assert.False(config.IsAzure);
    }

    [Theory]
    [InlineData("azure")]
    [InlineData("AZURE")]
    [InlineData("Azure")]
    public void IsAzure_IsTrue_ForAzureProvider(string provider)
    {
        var config = TestAigestConfig.Create() with { Provider = provider };

        Assert.True(config.IsAzure);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("cloud")]
    [InlineData("CLOUD")]
    [InlineData("azure")]
    [InlineData("foo")]
    public void IsLocal_IsFalse_ForNonLocalProvider(string? provider)
    {
        var config = TestAigestConfig.Create() with { Provider = provider };

        Assert.False(config.IsLocal);
    }

    [Theory]
    [InlineData("local")]
    [InlineData("LOCAL")]
    [InlineData("Local")]
    public void IsLocal_IsTrue_ForLocalProvider(string provider)
    {
        var config = TestAigestConfig.Create() with { Provider = provider };

        Assert.True(config.IsLocal);
    }
}
