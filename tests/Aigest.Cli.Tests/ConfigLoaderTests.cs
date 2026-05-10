using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aigest.Cli.Core;
using Xunit;

namespace Aigest.Cli.Tests;

[Collection("EnvVar")]
public class ConfigLoaderTests : IDisposable
{
    private readonly Dictionary<string, string?> _originalValues;

    public ConfigLoaderTests()
    {
        _originalValues = EnvNames.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        // Also snapshot vars that affect user-config-dir discovery; restored on dispose but not cleared by ClearAigestEnv
        foreach (var name in new[] { "HOME", "USERPROFILE", "APPDATA", "XDG_CONFIG_HOME" })
            _originalValues[name] = Environment.GetEnvironmentVariable(name);
    }

    public void Dispose()
    {
        foreach (var (name, value) in _originalValues)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    private static readonly string[] EnvNames =
    [
        "AIGEST_API_KEY",
        "GEMINI_API_KEY",
        "DEEPSEEK_API_KEY",
        "OPENAI_API_KEY",
        "AZURE_OPENAI_API_KEY",
        "AIGEST_BASE_URL",
        "AIGEST_MODEL",
        "AIGEST_MAX_FILE_BYTES",
        "AIGEST_MAX_TOTAL_BYTES",
        "AIGEST_TIMEOUT_SECONDS",
        "AIGEST_DEBUG",
        "AIGEST_THINKING_EFFORT",
        "AIGEST_PROVIDER",
        "AIGEST_MAX_TOKENS",
    ];

    private static void ClearAigestEnv()
    {
        foreach (var name in EnvNames)
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    private static void SetEnv(string name, string? value)
    {
        Environment.SetEnvironmentVariable(name, value);
    }

    [Fact]
    public void LoadConfig_UsesDefaults_WhenEnvVarsMissing()
    {
        ClearAigestEnv();
        SetEnv("AIGEST_API_KEY", "test-key");

        var config = ConfigLoader.Load(searchForEnvFile: false);

        Assert.Equal("test-key", config.ApiKey);
        Assert.Equal("https://api.deepseek.com", config.BaseUrl);
        Assert.Equal("deepseek-v4-flash", config.Model);
        Assert.Equal(800_000, config.MaxFileBytes);
        Assert.Equal(4_000_000, config.MaxTotalBytes);
        Assert.Equal(120, config.TimeoutSeconds);
        Assert.False(config.Debug);
        Assert.Null(config.ThinkingEffort);
    }

    [Fact]
    public void LoadConfig_FallsBackApiKey_Gemini()
    {
        ClearAigestEnv();
        SetEnv("GEMINI_API_KEY", "gemini-key");

        var config = ConfigLoader.Load(searchForEnvFile: false);

        Assert.Equal("gemini-key", config.ApiKey);
    }

    [Fact]
    public void LoadConfig_FallsBackApiKey_DeepSeek()
    {
        ClearAigestEnv();
        SetEnv("DEEPSEEK_API_KEY", "deepseek-key");

        var config = ConfigLoader.Load(searchForEnvFile: false);

        Assert.Equal("deepseek-key", config.ApiKey);
    }

    [Fact]
    public void LoadConfig_FallsBackApiKey_OpenAI()
    {
        ClearAigestEnv();
        SetEnv("OPENAI_API_KEY", "openai-key");

        var config = ConfigLoader.Load(searchForEnvFile: false);

        Assert.Equal("openai-key", config.ApiKey);
    }

    [Fact]
    public void LoadConfig_ParsesCustomValues()
    {
        ClearAigestEnv();
        Environment.SetEnvironmentVariable("AIGEST_API_KEY", "k");
        Environment.SetEnvironmentVariable("AIGEST_BASE_URL", "https://custom.example");
        Environment.SetEnvironmentVariable("AIGEST_MODEL", "custom-model");
        Environment.SetEnvironmentVariable("AIGEST_MAX_FILE_BYTES", "1000");
        Environment.SetEnvironmentVariable("AIGEST_MAX_TOTAL_BYTES", "5000");
        Environment.SetEnvironmentVariable("AIGEST_TIMEOUT_SECONDS", "30");
        Environment.SetEnvironmentVariable("AIGEST_DEBUG", "1");
        Environment.SetEnvironmentVariable("AIGEST_THINKING_EFFORT", "high");

        var config = ConfigLoader.Load(searchForEnvFile: false);

        Assert.Equal("https://custom.example", config.BaseUrl);
        Assert.Equal("custom-model", config.Model);
        Assert.Equal(1000, config.MaxFileBytes);
        Assert.Equal(5000, config.MaxTotalBytes);
        Assert.Equal(30, config.TimeoutSeconds);
        Assert.True(config.Debug);
        Assert.Equal("high", config.ThinkingEffort);
    }

    [Fact]
    public void LoadConfig_ThinkingEffort_NormalizesCase()
    {
        ClearAigestEnv();
        SetEnv("AIGEST_API_KEY", "test-key");
        SetEnv("AIGEST_THINKING_EFFORT", "  HIGH  ");

        var config = ConfigLoader.Load(searchForEnvFile: false);

        Assert.Equal("high", config.ThinkingEffort);
    }

    [Fact]
    public void LoadConfig_ThinkingEffort_AcceptsXHigh()
    {
        ClearAigestEnv();
        SetEnv("AIGEST_API_KEY", "test-key");
        SetEnv("AIGEST_THINKING_EFFORT", "xhigh");

        var config = ConfigLoader.Load(searchForEnvFile: false);

        Assert.Equal("xhigh", config.ThinkingEffort);
    }

    [Fact]
    public void LoadConfig_ThinkingEffort_RejectsInvalidValue()
    {
        ClearAigestEnv();
        SetEnv("AIGEST_API_KEY", "test-key");
        SetEnv("AIGEST_THINKING_EFFORT", "hihg");

        var ex = Assert.Throws<InvalidOperationException>(
            () => ConfigLoader.Load(searchForEnvFile: false));

        Assert.Contains("low, medium, high, xhigh", ex.Message);
        Assert.Contains("hihg", ex.Message);
    }

    [Fact]
    public void LoadConfig_ReadsDotEnvFile()
    {
        ClearAigestEnv();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var envFile = Path.Combine(tempDir, ".env");
        File.WriteAllText(
            envFile,
            "AIGEST_API_KEY=from-file\nAIGEST_BASE_URL=\"https://quoted.example\"\nAIGEST_MODEL='file-model'\n");

        try
        {
            var config = ConfigLoader.Load(envFile);

            Assert.Equal("from-file", config.ApiKey);
            Assert.Equal("https://quoted.example", config.BaseUrl);
            Assert.Equal("file-model", config.Model);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void FindEnvFile_FallsBackToUserConfigDir_WhenNoLocalEnvFound()
    {
        ClearAigestEnv();
        var tempXdg = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var userEnvDir = Path.Combine(tempXdg, "aigest");
        Directory.CreateDirectory(userEnvDir);
        File.WriteAllText(Path.Combine(userEnvDir, ".env"), "AIGEST_API_KEY=user-config-key\n");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", tempXdg);

        var cwdWithNoEnv = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(cwdWithNoEnv);

        try
        {
            var originalDir = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(cwdWithNoEnv);
            try
            {
                var config = ConfigLoader.Load(searchForEnvFile: true);
                Assert.Equal("user-config-key", config.ApiKey);
            }
            finally
            {
                Directory.SetCurrentDirectory(originalDir);
            }
        }
        finally
        {
            Directory.Delete(tempXdg, recursive: true);
            Directory.Delete(cwdWithNoEnv, recursive: true);
        }
    }

    [Fact]
    public void LoadConfig_LocalProvider_AllowsBlankApiKey()
    {
        ClearAigestEnv();
        SetEnv("AIGEST_PROVIDER", "local");

        var config = ConfigLoader.Load(searchForEnvFile: false);

        Assert.Equal("ollama", config.ApiKey);
        Assert.True(config.IsLocal);
        Assert.Equal("local", config.Provider);
    }

    [Fact]
    public void LoadConfig_LocalProvider_DefaultsBaseUrlToOllama()
    {
        ClearAigestEnv();
        SetEnv("AIGEST_PROVIDER", "local");

        var config = ConfigLoader.Load(searchForEnvFile: false);

        Assert.Equal("http://localhost:11434/v1", config.BaseUrl);
    }

    [Fact]
    public void LoadConfig_LocalProvider_DefaultsModelToLlama32()
    {
        ClearAigestEnv();
        SetEnv("AIGEST_PROVIDER", "local");

        var config = ConfigLoader.Load(searchForEnvFile: false);

        Assert.Equal("llama3.2", config.Model);
    }

    [Fact]
    public void LoadConfig_LocalProvider_DefaultsTimeoutTo600()
    {
        ClearAigestEnv();
        SetEnv("AIGEST_PROVIDER", "local");

        var config = ConfigLoader.Load(searchForEnvFile: false);

        Assert.Equal(600, config.TimeoutSeconds);
    }

    [Fact]
    public void LoadConfig_LocalProvider_RespectsExplicitOverrides()
    {
        ClearAigestEnv();
        SetEnv("AIGEST_PROVIDER", "local");
        SetEnv("AIGEST_BASE_URL", "http://localhost:1234/v1");
        SetEnv("AIGEST_MODEL", "qwen2.5-coder");
        SetEnv("AIGEST_TIMEOUT_SECONDS", "300");

        var config = ConfigLoader.Load(searchForEnvFile: false);

        Assert.Equal("http://localhost:1234/v1", config.BaseUrl);
        Assert.Equal("qwen2.5-coder", config.Model);
        Assert.Equal(300, config.TimeoutSeconds);
    }

    [Fact]
    public void LoadConfig_CloudProvider_StillRequiresApiKey()
    {
        ClearAigestEnv();
        // No AIGEST_PROVIDER (defaults to cloud), no API key in any fallback var.

        Assert.Throws<InvalidOperationException>(
            () => ConfigLoader.Load(searchForEnvFile: false));
    }

    [Fact]
    public void LoadConfig_Provider_RejectsInvalidValue()
    {
        ClearAigestEnv();
        SetEnv("AIGEST_API_KEY", "k");
        SetEnv("AIGEST_PROVIDER", "remote");

        var ex = Assert.Throws<InvalidOperationException>(
            () => ConfigLoader.Load(searchForEnvFile: false));

        Assert.Contains("local, cloud, azure", ex.Message);
        Assert.Contains("remote", ex.Message);
    }

    [Fact]
    public void LoadConfig_Provider_AcceptsCloudExplicit()
    {
        ClearAigestEnv();
        SetEnv("AIGEST_API_KEY", "k");
        SetEnv("AIGEST_PROVIDER", "CLOUD");

        var config = ConfigLoader.Load(searchForEnvFile: false);

        Assert.Equal("cloud", config.Provider);
        Assert.False(config.IsLocal);
    }

    [Fact]
    public void LoadConfig_AzureProvider_AcceptsAzureValue()
    {
        ClearAigestEnv();
        SetEnv("AIGEST_PROVIDER", "azure");
        SetEnv("AIGEST_API_KEY", "key");
        SetEnv("AIGEST_BASE_URL", "https://acme.openai.azure.com/openai/v1/");
        SetEnv("AIGEST_MODEL", "gpt-5");

        var config = ConfigLoader.Load(searchForEnvFile: false);

        Assert.Equal("azure", config.Provider);
        Assert.True(config.IsAzure);
        Assert.False(config.IsLocal);
        Assert.Equal("https://acme.openai.azure.com/openai/v1/", config.BaseUrl);
        Assert.Equal("gpt-5", config.Model);
    }

    [Fact]
    public void LoadConfig_AzureProvider_AcceptsUppercaseValue()
    {
        ClearAigestEnv();
        SetEnv("AIGEST_PROVIDER", "AZURE");
        SetEnv("AIGEST_API_KEY", "key");
        SetEnv("AIGEST_BASE_URL", "https://acme.openai.azure.com/openai/v1/");
        SetEnv("AIGEST_MODEL", "gpt-5");

        var config = ConfigLoader.Load(searchForEnvFile: false);

        Assert.Equal("azure", config.Provider);
        Assert.True(config.IsAzure);
        Assert.False(config.IsLocal);
    }

    [Fact]
    public void LoadConfig_AzureProvider_RequiresBaseUrl()
    {
        ClearAigestEnv();
        SetEnv("AIGEST_PROVIDER", "azure");
        SetEnv("AIGEST_API_KEY", "key");
        // No AIGEST_BASE_URL
        SetEnv("AIGEST_MODEL", "gpt-5");

        var ex = Assert.Throws<InvalidOperationException>(
            () => ConfigLoader.Load(searchForEnvFile: false));

        Assert.Contains("AIGEST_BASE_URL is required", ex.Message);
        Assert.Contains("azure", ex.Message);
    }

    [Fact]
    public void LoadConfig_AzureProvider_RequiresModel()
    {
        ClearAigestEnv();
        SetEnv("AIGEST_PROVIDER", "azure");
        SetEnv("AIGEST_API_KEY", "key");
        SetEnv("AIGEST_BASE_URL", "https://acme.openai.azure.com/openai/v1/");
        // No AIGEST_MODEL

        var ex = Assert.Throws<InvalidOperationException>(
            () => ConfigLoader.Load(searchForEnvFile: false));

        Assert.Contains("AIGEST_MODEL is required", ex.Message);
        Assert.Contains("azure", ex.Message);
    }

    [Fact]
    public void LoadConfig_AzureProvider_RequiresApiKey()
    {
        ClearAigestEnv();
        SetEnv("AIGEST_PROVIDER", "azure");
        SetEnv("AIGEST_BASE_URL", "https://acme.openai.azure.com/openai/v1/");
        SetEnv("AIGEST_MODEL", "gpt-5");
        // No API key in any env var (including AZURE_OPENAI_API_KEY)

        Assert.Throws<InvalidOperationException>(
            () => ConfigLoader.Load(searchForEnvFile: false));
    }

    [Fact]
    public void LoadConfig_AzureProvider_FallsBackToAzureOpenAiKey()
    {
        ClearAigestEnv();
        SetEnv("AIGEST_PROVIDER", "azure");
        SetEnv("AZURE_OPENAI_API_KEY", "azure-key");
        SetEnv("AIGEST_BASE_URL", "https://acme.openai.azure.com/openai/v1/");
        SetEnv("AIGEST_MODEL", "gpt-5");

        var config = ConfigLoader.Load(searchForEnvFile: false);

        Assert.Equal("azure-key", config.ApiKey);
    }

    [Fact]
    public void LoadConfig_AzureProvider_DefaultsTimeoutTo120()
    {
        ClearAigestEnv();
        SetEnv("AIGEST_PROVIDER", "azure");
        SetEnv("AIGEST_API_KEY", "key");
        SetEnv("AIGEST_BASE_URL", "https://acme.openai.azure.com/openai/v1/");
        SetEnv("AIGEST_MODEL", "gpt-5");
        // No AIGEST_TIMEOUT_SECONDS

        var config = ConfigLoader.Load(searchForEnvFile: false);

        Assert.Equal(120, config.TimeoutSeconds);
    }

    [Fact]
    public void Load_UserConfigWins_OverProjectEnvInCwd()
    {
        // The CWD walk was removed: a project .env in the working directory must NOT
        // override the user config. User config is the canonical source.
        ClearAigestEnv();
        var tempXdg = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var userEnvDir = Path.Combine(tempXdg, "aigest");
        Directory.CreateDirectory(userEnvDir);
        File.WriteAllText(Path.Combine(userEnvDir, ".env"), "AIGEST_API_KEY=user-config-key\n");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", tempXdg);

        var projectDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(projectDir);
        File.WriteAllText(Path.Combine(projectDir, ".env"), "AIGEST_API_KEY=project-key\n");

        try
        {
            var originalDir = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(projectDir);
            try
            {
                var config = ConfigLoader.Load(searchForEnvFile: true);
                Assert.Equal("user-config-key", config.ApiKey);
            }
            finally
            {
                Directory.SetCurrentDirectory(originalDir);
            }
        }
        finally
        {
            Directory.Delete(tempXdg, recursive: true);
            Directory.Delete(projectDir, recursive: true);
        }
    }

    [Fact]
    public void Load_ParsesAigestMaxTokens_WhenSet()
    {
        ClearAigestEnv();
        SetEnv("AIGEST_API_KEY", "test-key");
        SetEnv("AIGEST_MAX_TOKENS", "24000");

        var config = ConfigLoader.Load(searchForEnvFile: false);

        Assert.Equal(24000, config.MaxOutputTokens);
    }

    [Fact]
    public void Load_DefaultsMaxOutputTokensToZero_WhenUnset()
    {
        ClearAigestEnv();
        SetEnv("AIGEST_API_KEY", "test-key");
        // AIGEST_MAX_TOKENS is not set (ClearAigestEnv already cleared it)

        var config = ConfigLoader.Load(searchForEnvFile: false);

        Assert.Equal(0, config.MaxOutputTokens);
    }

    [Fact]
    public void Load_RejectsNegativeAigestMaxTokens()
    {
        ClearAigestEnv();
        SetEnv("AIGEST_API_KEY", "test-key");
        SetEnv("AIGEST_MAX_TOKENS", "-1");

        var ex = Assert.Throws<InvalidOperationException>(
            () => ConfigLoader.Load(searchForEnvFile: false));

        Assert.Contains("AIGEST_MAX_TOKENS", ex.Message);
    }
}
