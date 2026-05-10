using System;
using System.Net.Http;
using Aigest.Cli.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aigest.Cli.Tests;

public class OpenAiChatClientTests
{
    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://attacker.example")]
    [InlineData("data:text/plain,foo")]
    public void Constructor_RejectsNonHttpScheme(string url)
    {
        var config = new AigestConfig("key", url, "model", 1, 1, 1, false);
        Assert.Throws<InvalidOperationException>(() => new OpenAiChatClient(config));
    }

    [Theory]
    [InlineData("https://api.example.com")]
    [InlineData("http://localhost:8080")]
    public void Constructor_AcceptsHttpAndHttps(string url)
    {
        var config = new AigestConfig("key", url, "model", 1, 1, 1, false);
        // Should not throw — just verifies scheme validation doesn't block valid URLs
        var ex = Record.Exception(() => new OpenAiChatClient(config));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://attacker.example")]
    public void DIConstructor_RejectsNonHttpScheme(string url)
    {
        var config = new AigestConfig("key", url, "model", 1, 1, 1, false);
        using var httpClient = new HttpClient();
        var logger = NullLogger<OpenAiChatClient>.Instance;
        Assert.Throws<InvalidOperationException>(() => new OpenAiChatClient(config, httpClient, logger));
    }

    [Theory]
    [InlineData("https://api.example.com")]
    [InlineData("http://localhost:8080")]
    public void DIConstructor_AcceptsHttpAndHttps(string url)
    {
        var config = new AigestConfig("key", url, "model", 1, 1, 1, false);
        using var httpClient = new HttpClient();
        var logger = NullLogger<OpenAiChatClient>.Instance;
        var ex = Record.Exception(() => new OpenAiChatClient(config, httpClient, logger));
        Assert.Null(ex);
    }
}
