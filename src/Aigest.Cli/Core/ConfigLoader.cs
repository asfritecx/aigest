using System.Runtime.InteropServices;

namespace Aigest.Cli.Core;

public static class ConfigLoader
{
    public static AigestConfig Load(string? envFilePath = null, bool searchForEnvFile = true)
    {
        if (envFilePath is not null)
        {
            // Explicit path: load only that file (used in tests and direct-path scenarios)
            LoadEnvFile(envFilePath);
        }
        else if (searchForEnvFile)
        {
            // Cascade: user config wins because LoadEnvFile skips already-set vars.
            // CWD is intentionally not searched — arbitrary projects' .env files must not
            // silently override the user's canonical config.
            var userConfig = GetUserConfigEnvPath();
            if (File.Exists(userConfig)) LoadEnvFile(userConfig);

            // Fallback: walk up from AppContext.BaseDirectory so `dotnet run` inside the kit
            // still picks up the repo .env when no user config exists.
            var baseDirResult = WalkForEnvFile(AppContext.BaseDirectory);
            if (baseDirResult is not null) LoadEnvFile(baseDirResult);
        }

        var provider = ParseProvider(GetEnv("AIGEST_PROVIDER"));
        var isLocal = string.Equals(provider, "local", StringComparison.OrdinalIgnoreCase);
        var isAzure = string.Equals(provider, "azure", StringComparison.OrdinalIgnoreCase);

        var apiKey = GetEnv("AIGEST_API_KEY")
            ?? GetEnv("GEMINI_API_KEY")
            ?? GetEnv("DEEPSEEK_API_KEY")
            ?? GetEnv("OPENAI_API_KEY")
            ?? GetEnv("AZURE_OPENAI_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            if (isLocal)
            {
                // Local OpenAI-compatible servers (Ollama, LM Studio) ignore the auth header,
                // but the SDK's ApiKeyCredential requires a non-empty string.
                apiKey = "ollama";
            }
            else
            {
                var userConfigPath = GetUserConfigEnvPath();
                throw new InvalidOperationException(
                    $"Missing AIGEST_API_KEY. Set it via environment variable, a project-local .env, " +
                    $"or at {userConfigPath} (copy .env.example to start)."
                );
            }
        }

        var maxOutputTokens = ParseInt(GetEnv("AIGEST_MAX_TOKENS"), 0);
        if (maxOutputTokens < 0)
            throw new InvalidOperationException(
                $"AIGEST_MAX_TOKENS must be a non-negative integer (got '{GetEnv("AIGEST_MAX_TOKENS")}').");

        if (isAzure)
        {
            var azureBaseUrl = GetEnv("AIGEST_BASE_URL");
            if (string.IsNullOrWhiteSpace(azureBaseUrl))
                throw new InvalidOperationException(
                    "AIGEST_BASE_URL is required when AIGEST_PROVIDER=azure (e.g. https://<resource>.openai.azure.com/openai/v1/).");

            var azureModel = GetEnv("AIGEST_MODEL");
            if (string.IsNullOrWhiteSpace(azureModel))
                throw new InvalidOperationException(
                    "AIGEST_MODEL is required when AIGEST_PROVIDER=azure (use your Azure deployment name).");

            return new AigestConfig(
                ApiKey: apiKey,
                BaseUrl: azureBaseUrl,
                Model: azureModel,
                MaxFileBytes: ParseInt(GetEnv("AIGEST_MAX_FILE_BYTES"), 800_000),
                MaxTotalBytes: ParseInt(GetEnv("AIGEST_MAX_TOTAL_BYTES"), 4_000_000),
                TimeoutSeconds: ParseInt(GetEnv("AIGEST_TIMEOUT_SECONDS"), 120),
                Debug: (GetEnv("AIGEST_DEBUG") ?? "0") == "1",
                ThinkingEffort: ParseThinkingEffort(GetEnv("AIGEST_THINKING_EFFORT")),
                MaxParallelFolders: ParseInt(GetEnv("AIGEST_MAX_PARALLEL_FOLDERS"), 4),
                Provider: provider,
                MaxOutputTokens: maxOutputTokens
            );
        }

        var defaultBaseUrl = isLocal ? "http://localhost:11434/v1" : "https://api.deepseek.com";
        var defaultModel = isLocal ? "llama3.2" : "deepseek-v4-flash";
        var defaultTimeout = isLocal ? 600 : 120;

        return new AigestConfig(
            ApiKey: apiKey,
            BaseUrl: GetEnv("AIGEST_BASE_URL") ?? defaultBaseUrl,
            Model: GetEnv("AIGEST_MODEL") ?? defaultModel,
            MaxFileBytes: ParseInt(GetEnv("AIGEST_MAX_FILE_BYTES"), 800_000),
            MaxTotalBytes: ParseInt(GetEnv("AIGEST_MAX_TOTAL_BYTES"), 4_000_000),
            TimeoutSeconds: ParseInt(GetEnv("AIGEST_TIMEOUT_SECONDS"), defaultTimeout),
            Debug: (GetEnv("AIGEST_DEBUG") ?? "0") == "1",
            ThinkingEffort: ParseThinkingEffort(GetEnv("AIGEST_THINKING_EFFORT")),
            MaxParallelFolders: ParseInt(GetEnv("AIGEST_MAX_PARALLEL_FOLDERS"), 4),
            Provider: provider,
            MaxOutputTokens: maxOutputTokens
        );
    }

    internal static string? FindEnvFile()
    {
        // Tier 1: user-level config dir — canonical source for the installed binary.
        var userConfig = GetUserConfigEnvPath();
        if (File.Exists(userConfig))
            return userConfig;

        // Tier 2: walk up from AppContext.BaseDirectory (supports `dotnet run` inside the kit repo).
        var baseDirResult = WalkForEnvFile(AppContext.BaseDirectory);
        if (baseDirResult is not null)
            return baseDirResult;

        return null;
    }

    private static string? WalkForEnvFile(string startDir)
    {
        var dir = startDir;
        while (!string.IsNullOrEmpty(dir))
        {
            var candidate = Path.Combine(dir, ".env");
            if (File.Exists(candidate))
                return candidate;

            // Stop at project root — do not walk above a .git boundary
            // (.git can be a file in worktrees/submodules, not just a directory)
            var gitMarker = Path.Combine(dir, ".git");
            if (Directory.Exists(gitMarker) || File.Exists(gitMarker))
                break;

            var parent = Directory.GetParent(dir);
            dir = parent?.FullName;
        }
        return null;
    }

    internal static string GetUserConfigEnvPath()
    {
        string configBase;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            configBase = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }
        else
        {
            // XDG_CONFIG_HOME overrides the default on Linux; on macOS use ~/.config for consistency
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (!string.IsNullOrEmpty(xdg))
                configBase = xdg;
            else
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                configBase = Path.Combine(home, ".config");
            }
        }
        return Path.Combine(configBase, "aigest", ".env");
    }

    private static void LoadEnvFile(string path)
    {
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                continue;

            var eq = line.IndexOf('=');
            if (eq < 0)
                continue;

            var key = line[..eq].Trim();
            var value = StripOptionalQuotes(line[(eq + 1)..].Trim());

            // Only set if not already present in environment
            if (Environment.GetEnvironmentVariable(key) is null)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    private static string StripOptionalQuotes(string value)
    {
        if (value.Length >= 2)
        {
            var first = value[0];
            var last = value[^1];
            if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
            {
                return value[1..^1];
            }
        }

        return value;
    }

    private static string? GetEnv(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int ParseInt(string? value, int fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static string? ParseThinkingEffort(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var normalized = raw.Trim().ToLowerInvariant();
        return normalized switch
        {
            "low" or "medium" or "high" or "xhigh" => normalized,
            _ => throw new InvalidOperationException(
                $"AIGEST_THINKING_EFFORT must be one of: low, medium, high, xhigh (got '{raw}').")
        };
    }

    private static string? ParseProvider(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var normalized = raw.Trim().ToLowerInvariant();
        return normalized switch
        {
            "local" or "cloud" or "azure" => normalized,
            _ => throw new InvalidOperationException(
                $"AIGEST_PROVIDER must be one of: local, cloud, azure (got '{raw}').")
        };
    }
}
