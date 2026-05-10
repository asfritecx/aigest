using Aigest.Cli.Core;
using Aigest.Cli.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aigest.Cli.Tests;

[Collection("EnvVar")]
public class CliHostTests : IDisposable
{
    private readonly string? _priorApiKey;
    private readonly string? _priorBaseUrl;
    private readonly string? _priorModel;

    public CliHostTests()
    {
        _priorApiKey = Environment.GetEnvironmentVariable("AIGEST_API_KEY");
        _priorBaseUrl = Environment.GetEnvironmentVariable("AIGEST_BASE_URL");
        _priorModel = Environment.GetEnvironmentVariable("AIGEST_MODEL");
        Environment.SetEnvironmentVariable("AIGEST_API_KEY", "test-key-for-di-resolution");
        Environment.SetEnvironmentVariable("AIGEST_BASE_URL", "https://test.example");
        Environment.SetEnvironmentVariable("AIGEST_MODEL", "test-model");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("AIGEST_API_KEY", _priorApiKey);
        Environment.SetEnvironmentVariable("AIGEST_BASE_URL", _priorBaseUrl);
        Environment.SetEnvironmentVariable("AIGEST_MODEL", _priorModel);
    }

    [Fact]
    public void Build_ResolvesIChatClient()
    {
        using var host = CliHost.Build();
        var client = host.Services.GetRequiredService<IChatClient>();
        Assert.IsType<OpenAiChatClient>(client);
    }
}
