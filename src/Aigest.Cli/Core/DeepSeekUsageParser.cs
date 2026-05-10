using System.Text;
using System.Text.Json;

namespace Aigest.Cli.Core;

internal readonly record struct CacheTokenUsage(int CacheHitTokens, int CacheMissTokens);

internal static class DeepSeekUsageParser
{
    internal static CacheTokenUsage Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            return default;

        try
        {
            var text = Encoding.UTF8.GetString(bytes);
            var usageJson = text.Contains("data:", StringComparison.Ordinal)
                ? GetLastSseUsageJson(text)
                : GetRootUsageJson(text);

            return usageJson is null ? default : ParseUsageObject(usageJson.Value);
        }
        catch (JsonException)
        {
            return default;
        }
        catch (InvalidOperationException)
        {
            return default;
        }
    }

    private static JsonElement? GetLastSseUsageJson(string text)
    {
        JsonElement? result = null;
        var lines = text.Split('\n');
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                continue;

            var data = line[5..].Trim();
            if (data.Length == 0 || data == "[DONE]")
                continue;

            try
            {
                using var doc = JsonDocument.Parse(data);
                if (doc.RootElement.TryGetProperty("usage", out var usage)
                        && usage.ValueKind == JsonValueKind.Object)
                    result = usage.Clone();
            }
            catch (JsonException)
            {
                continue;
            }
        }

        return result;
    }

    private static JsonElement? GetRootUsageJson(string text)
    {
        using var doc = JsonDocument.Parse(text);
        if (!doc.RootElement.TryGetProperty("usage", out var usage)) return null;
        return usage.ValueKind == JsonValueKind.Object ? usage.Clone() : null;
    }

    private static CacheTokenUsage ParseUsageObject(JsonElement usage)
    {
        var hit = GetInt32(usage, "prompt_cache_hit_tokens");
        var miss = GetInt32(usage, "prompt_cache_miss_tokens");

        if (hit == 0
            && miss == 0
            && usage.TryGetProperty("prompt_token_details", out var details))
        {
            hit = GetInt32(details, "cached_tokens");
        }

        return new CacheTokenUsage(hit, miss);
    }

    private static int GetInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return 0;

        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value)
            ? value
            : 0;
    }
}
