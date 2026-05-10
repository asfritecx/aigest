using Aigest.Cli.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Polly;
using Polly.Retry;

namespace Aigest.Cli.Hosting;

internal static class CliHost
{
    internal static IHost Build()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(opts =>
        {
            opts.SingleLine = true;
            opts.IncludeScopes = false;
        });
        // Route ALL log output to stderr so piped stdout (LLM responses) is not polluted
        builder.Services.Configure<ConsoleLoggerOptions>(opts =>
        {
            opts.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        builder.Services.AddSingleton(_ => ConfigLoader.Load());

        builder.Services.AddHttpClient("openai")
            .ConfigureHttpClient((sp, client) =>
            {
                var cfg = sp.GetRequiredService<AigestConfig>();
                client.Timeout = TimeSpan.FromSeconds(cfg.TimeoutSeconds);
            })
            .AddResilienceHandler("llm", (pipeline, ctx) =>
            {
                // Only retry on HTTP 429 (rate limit) — never retry other errors to avoid duplicate charges
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromSeconds(2),
                    ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                        .HandleResult(r => r.StatusCode == System.Net.HttpStatusCode.TooManyRequests),
                });
                var cfg = ctx.ServiceProvider.GetRequiredService<AigestConfig>();
                pipeline.AddTimeout(TimeSpan.FromSeconds(cfg.TimeoutSeconds / 2.0));
                // HttpClient.Timeout covers the full retry sequence (all attempts + backoff); AddTimeout is per attempt.
            });

        builder.Services.AddTransient<IChatClient>(sp =>
        {
            var config = sp.GetRequiredService<AigestConfig>();
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var logger = sp.GetRequiredService<ILogger<OpenAiChatClient>>();
            var httpClient = httpClientFactory.CreateClient("openai");
            return new OpenAiChatClient(config, httpClient, logger);
        });

        return builder.Build();
    }
}
