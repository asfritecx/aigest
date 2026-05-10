using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aigest.Cli.Core;

namespace Aigest.Cli.Commands;

internal static class CheckCommand
{
    internal static int Run() => RunAsync(CancellationToken.None).GetAwaiter().GetResult();

    internal static async Task<int> RunAsync(CancellationToken ct = default)
    {
        var envFilePath = ConfigLoader.FindEnvFile();
        var userConfigPath = ConfigLoader.GetUserConfigEnvPath();

        Console.WriteLine("aigest environment check");
        Console.WriteLine();

        if (envFilePath is not null)
        {
            var tier = string.Equals(envFilePath, userConfigPath, StringComparison.OrdinalIgnoreCase)
                ? "user config"
                : "base dir";
            Console.WriteLine($"  Config file : {envFilePath}  [{tier}]");
        }
        else
        {
            Console.WriteLine($"  Config file : not found (using environment variables only)");
            Console.WriteLine($"  User config : {userConfigPath}");
        }

        AigestConfig config;
        try
        {
            config = ConfigLoader.Load(searchForEnvFile: true);
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine("  API key     : MISSING");
            Console.WriteLine();
            Console.Error.WriteLine($"✗ {ex.Message}");
            return 1;
        }

        var providerLabel = config.IsLocal
            ? "local (Ollama/LM Studio compatible)"
            : config.IsAzure
                ? "azure (Foundry / OpenAI v1)"
                : "cloud";
        Console.WriteLine($"  Provider    : {providerLabel}");
        Console.WriteLine($"  API key     : {GetKeySourceName(config.ApiKey)}  ({MaskKey(config.ApiKey)})");
        Console.WriteLine($"  Base URL    : {config.BaseUrl}");
        Console.WriteLine($"  Model       : {config.Model}");
        Console.WriteLine($"  Timeout     : {config.TimeoutSeconds}s");
        Console.WriteLine($"  Thinking    : {FormatThinking(config)}");

        if (config.IsLocal || config.IsAzure)
        {
            Console.WriteLine($"  Reachable   : {await ProbeReachableAsync(config.BaseUrl, config.ApiKey, config.IsAzure, ct)}");
        }

        Console.WriteLine();
        Console.WriteLine("✓ Ready.");
        return 0;
    }

    private static string FormatThinking(AigestConfig config)
    {
        if (config.ThinkingEffort is null) return "(disabled)";
        return config.IsLocal
            ? $"{config.ThinkingEffort} (ignored: local mode)"
            : config.ThinkingEffort;
    }

    private static async Task<string> ProbeReachableAsync(string baseUrl, string apiKey, bool isAzure, CancellationToken ct)
    {
        // Use a fresh HttpClient — diagnostic, not subject to the production Polly retry/timeout.
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var url = baseUrl.TrimEnd('/') + "/models";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            // Attach Authorization header for Azure; local providers do not require auth.
            if (isAzure && !string.IsNullOrEmpty(apiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return $"no (HTTP {(int)response.StatusCode} from {url})";

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var count = TryCountModels(body);
            return count is null
                ? $"yes (responded {(int)response.StatusCode})"
                : $"yes ({count} models available)";
        }
        catch (TaskCanceledException)
        {
            return $"no (timeout contacting {url})";
        }
        catch (HttpRequestException ex)
        {
            return $"no ({ex.Message})";
        }
        catch (Exception ex)
        {
            return $"no ({ex.GetType().Name}: {ex.Message})";
        }
    }

    private static int? TryCountModels(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Array)
            {
                return data.GetArrayLength();
            }
        }
        catch (JsonException)
        {
            // fall through
        }
        return null;
    }

    private static string GetKeySourceName(string apiKey)
    {
        foreach (var name in new[] { "AIGEST_API_KEY", "GEMINI_API_KEY", "DEEPSEEK_API_KEY", "OPENAI_API_KEY", "AZURE_OPENAI_API_KEY" })
        {
            if (string.Equals(Environment.GetEnvironmentVariable(name), apiKey, StringComparison.Ordinal))
                return name;
        }
        // Local-mode synthetic credential (when no key env var is set).
        if (string.Equals(apiKey, "ollama", StringComparison.Ordinal))
            return "(local default)";
        return "unknown";
    }

    private static string MaskKey(string key)
    {
        if (key.Length <= 8) return "***";
        return $"{key[..4]}…{key[^4..]}";
    }
}
