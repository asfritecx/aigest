using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Aigest.Cli.Commands;
using Xunit;

namespace Aigest.Cli.Tests;

[Collection("EnvVar")]
public class CheckCommandTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _originalApiKey;
    private readonly string? _originalGeminiKey;
    private readonly string? _originalDeepSeekKey;
    private readonly string? _originalOpenAiKey;
    private readonly string? _originalAzureOpenAiKey;
    private readonly string? _originalXdg;
    private readonly string? _originalThinkingEffort;
    private readonly string? _originalProvider;
    private readonly string? _originalBaseUrl;
    private readonly string? _originalModel;

    public CheckCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);

        _originalApiKey = Environment.GetEnvironmentVariable("AIGEST_API_KEY") ?? "";
        _originalGeminiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        _originalDeepSeekKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        _originalOpenAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        _originalAzureOpenAiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
        _originalXdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        _originalThinkingEffort = Environment.GetEnvironmentVariable("AIGEST_THINKING_EFFORT");
        _originalProvider = Environment.GetEnvironmentVariable("AIGEST_PROVIDER");
        _originalBaseUrl = Environment.GetEnvironmentVariable("AIGEST_BASE_URL");
        _originalModel = Environment.GetEnvironmentVariable("AIGEST_MODEL");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);

        Environment.SetEnvironmentVariable("AIGEST_API_KEY", _originalApiKey);
        Environment.SetEnvironmentVariable("GEMINI_API_KEY", _originalGeminiKey);
        Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", _originalDeepSeekKey);
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", _originalOpenAiKey);
        Environment.SetEnvironmentVariable("AZURE_OPENAI_API_KEY", _originalAzureOpenAiKey);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _originalXdg);
        Environment.SetEnvironmentVariable("AIGEST_THINKING_EFFORT", _originalThinkingEffort);
        Environment.SetEnvironmentVariable("AIGEST_PROVIDER", _originalProvider);
        Environment.SetEnvironmentVariable("AIGEST_BASE_URL", _originalBaseUrl);
        Environment.SetEnvironmentVariable("AIGEST_MODEL", _originalModel);
    }

    [Fact]
    public void Run_ReturnsZero_WhenApiKeyInEnvironment()
    {
        Environment.SetEnvironmentVariable("AIGEST_API_KEY", "test-api-key-for-check");

        var (stdout, _, exitCode) = CaptureRun();

        Assert.Equal(0, exitCode);
        Assert.Contains("✓ Ready.", stdout);
    }

    [Fact]
    public void Run_MasksApiKey_InOutput()
    {
        Environment.SetEnvironmentVariable("AIGEST_API_KEY", "sk-abcdefghijklmnop");

        var (stdout, _, _) = CaptureRun();

        Assert.Contains("sk-a", stdout);
        Assert.Contains("mnop", stdout);
        Assert.DoesNotContain("sk-abcdefghijklmnop", stdout);
    }

    [Fact]
    public void Run_ShowsKeySourceName_InOutput()
    {
        Environment.SetEnvironmentVariable("AIGEST_API_KEY", "worker-key-unique-xyz9876");

        var (stdout, _, exitCode) = CaptureRun();

        Assert.Equal(0, exitCode);
        Assert.Contains("AIGEST_API_KEY", stdout);
    }

    [Fact]
    public void Run_IgnoresProjectEnvInCwd_WhenUserConfigExists()
    {
        // The CWD walk was removed: a project .env in the working directory must NOT
        // be picked up. User config wins.
        var cwdDir = Path.Combine(_tempDir, "other-project");
        Directory.CreateDirectory(cwdDir);
        File.WriteAllText(
            Path.Combine(cwdDir, ".env"),
            "AIGEST_API_KEY=ProjectShouldBeIgnored1234\nAIGEST_BASE_URL=https://wrong.example.com\n");
        Directory.CreateDirectory(Path.Combine(cwdDir, ".git"));

        var userConfigDir = Path.Combine(_tempDir, "xdg-config", "aigest");
        Directory.CreateDirectory(userConfigDir);
        File.WriteAllText(
            Path.Combine(userConfigDir, ".env"),
            "AIGEST_API_KEY=UserConfigShouldBeUsed5678\n");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(_tempDir, "xdg-config"));

        Environment.SetEnvironmentVariable("AIGEST_API_KEY", null);
        Environment.SetEnvironmentVariable("GEMINI_API_KEY", null);
        Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", null);
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
        Environment.SetEnvironmentVariable("AIGEST_BASE_URL", null);

        var originalDir = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(cwdDir);
        try
        {
            var (stdout, _, exitCode) = CaptureRun();

            Assert.Equal(0, exitCode);
            Assert.Contains("✓ Ready.", stdout);
            Assert.Contains("5678", stdout);             // user-config key suffix (masked)
            Assert.DoesNotContain("Proj", stdout);       // project key prefix would appear if it leaked
            Assert.DoesNotContain("wrong.example.com", stdout);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }
    }

    [Fact]
    public void Run_ShowsUserConfigPath_WhenUserConfigEnvExists()
    {
        var userConfigDir = Path.Combine(_tempDir, "xdg-config", "aigest");
        Directory.CreateDirectory(userConfigDir);
        File.WriteAllText(
            Path.Combine(userConfigDir, ".env"),
            "AIGEST_API_KEY=path-test-key-1234567890\n");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(_tempDir, "xdg-config"));

        Environment.SetEnvironmentVariable("AIGEST_API_KEY", null);
        Environment.SetEnvironmentVariable("GEMINI_API_KEY", null);
        Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", null);
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);

        var (stdout, _, exitCode) = CaptureRun();

        Assert.Equal(0, exitCode);
        Assert.Contains(".env", stdout);
        Assert.Contains("[user config]", stdout);
    }

    [Fact]
    public void Run_ShowsThinkingDisabled_WhenEnvVarUnset()
    {
        Environment.SetEnvironmentVariable("AIGEST_API_KEY", "test-thinking-key");
        // Set to empty (not null) so LoadEnvFile sees it as "already set" and skips it.
        // Otherwise the BaseDirectory walk could load a kit-level .env and inject a value.
        Environment.SetEnvironmentVariable("AIGEST_THINKING_EFFORT", "");
        // Isolate user config so AIGEST_THINKING_EFFORT from ~/.config/aigest/.env doesn't leak in.
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _tempDir);

        var (stdout, _, exitCode) = CaptureRun();

        Assert.Equal(0, exitCode);
        Assert.Contains("Thinking    : (disabled)", stdout);
    }

    [Fact]
    public void Run_ShowsThinkingEffort_WhenEnvVarSet()
    {
        Environment.SetEnvironmentVariable("AIGEST_API_KEY", "test-thinking-key");
        Environment.SetEnvironmentVariable("AIGEST_THINKING_EFFORT", "medium");

        var (stdout, _, exitCode) = CaptureRun();

        Assert.Equal(0, exitCode);
        Assert.Contains("Thinking    : medium", stdout);
    }

    [Fact]
    public void Run_LocalProvider_PrintsLocalProviderLine_AndNoMissingKeyError()
    {
        Environment.SetEnvironmentVariable("AIGEST_API_KEY", null);
        Environment.SetEnvironmentVariable("GEMINI_API_KEY", null);
        Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", null);
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _tempDir); // isolate user config
        Environment.SetEnvironmentVariable("AIGEST_PROVIDER", "local");
        // Closed port → probe returns "no (...)" quickly, but exit code stays 0.
        Environment.SetEnvironmentVariable("AIGEST_BASE_URL", "http://127.0.0.1:1");

        var (stdout, _, exitCode) = CaptureRun();

        Assert.Equal(0, exitCode);
        Assert.Contains("Provider    : local", stdout);
        Assert.Contains("✓ Ready.", stdout);
        Assert.Contains("Reachable   :", stdout);
    }

    [Fact]
    public void Run_LocalProvider_FlagsThinkingEffortAsIgnored()
    {
        Environment.SetEnvironmentVariable("AIGEST_API_KEY", null);
        Environment.SetEnvironmentVariable("GEMINI_API_KEY", null);
        Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", null);
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _tempDir);
        Environment.SetEnvironmentVariable("AIGEST_PROVIDER", "local");
        Environment.SetEnvironmentVariable("AIGEST_THINKING_EFFORT", "medium");
        Environment.SetEnvironmentVariable("AIGEST_BASE_URL", "http://127.0.0.1:1");

        var (stdout, _, exitCode) = CaptureRun();

        Assert.Equal(0, exitCode);
        Assert.Contains("Thinking    : medium (ignored: local mode)", stdout);
    }

    [Fact]
    public void Run_AzureProvider_PrintsAzureProviderLine()
    {
        Environment.SetEnvironmentVariable("AIGEST_PROVIDER", "azure");
        Environment.SetEnvironmentVariable("AIGEST_API_KEY", "test");
        Environment.SetEnvironmentVariable("AIGEST_BASE_URL", "http://127.0.0.1:1");
        Environment.SetEnvironmentVariable("AIGEST_MODEL", "gpt-5");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _tempDir);
        // Set to "" (not null) so LoadEnvFile skips them — prevents the repo .env from
        // injecting values and letting a different key win the key-chain resolution.
        Environment.SetEnvironmentVariable("GEMINI_API_KEY", "");
        Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", "");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "");
        Environment.SetEnvironmentVariable("AIGEST_THINKING_EFFORT", "");

        var (stdout, _, exitCode) = CaptureRun();

        Assert.Equal(0, exitCode);
        Assert.Contains("Provider    : azure (Foundry / OpenAI v1)", stdout);
        Assert.Contains("Reachable   :", stdout);
        Assert.Contains("✓ Ready.", stdout);
    }

    [Fact]
    public void Run_AzureProvider_DoesNotFlagThinkingEffortAsIgnored()
    {
        Environment.SetEnvironmentVariable("AIGEST_PROVIDER", "azure");
        Environment.SetEnvironmentVariable("AIGEST_API_KEY", "test");
        Environment.SetEnvironmentVariable("AIGEST_BASE_URL", "http://127.0.0.1:1");
        Environment.SetEnvironmentVariable("AIGEST_MODEL", "gpt-5");
        Environment.SetEnvironmentVariable("AIGEST_THINKING_EFFORT", "medium");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _tempDir);
        // Set to "" (not null) so LoadEnvFile skips them — prevents the repo .env from
        // injecting values and letting a different key win the key-chain resolution.
        Environment.SetEnvironmentVariable("GEMINI_API_KEY", "");
        Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", "");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "");

        var (stdout, _, exitCode) = CaptureRun();

        Assert.Equal(0, exitCode);
        Assert.Contains("Thinking    : medium", stdout);
        Assert.DoesNotContain("(ignored: local mode)", stdout);
    }

    [Fact]
    public void Run_AzureProvider_RecognisesAzureOpenAiKeyEnvVar()
    {
        Environment.SetEnvironmentVariable("AIGEST_PROVIDER", "azure");
        // Set to "" (not null) so LoadEnvFile skips them — prevents the repo .env from
        // injecting values and letting a different key win the key-chain resolution.
        Environment.SetEnvironmentVariable("AIGEST_API_KEY", "");
        Environment.SetEnvironmentVariable("GEMINI_API_KEY", "");
        Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", "");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "");
        Environment.SetEnvironmentVariable("AZURE_OPENAI_API_KEY", "test-azure-key");
        Environment.SetEnvironmentVariable("AIGEST_BASE_URL", "http://127.0.0.1:1");
        Environment.SetEnvironmentVariable("AIGEST_MODEL", "gpt-5");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _tempDir);

        var (stdout, _, exitCode) = CaptureRun();

        Assert.Equal(0, exitCode);
        Assert.Contains("AZURE_OPENAI_API_KEY", stdout);
    }

    [Fact]
    public async Task Run_AzureProvider_ProbeSendsBearerAuthHeader()
    {
        // Spin up an in-process HTTP listener to capture the Authorization header.
        var listener = new HttpListener();
        var port = GetFreePort();
        var baseUrl = $"http://127.0.0.1:{port}";
        listener.Prefixes.Add($"{baseUrl}/");

        // Bind synchronously BEFORE kicking off the consumer task. Under a saturated
        // threadpool (late in the suite) Task.Run could otherwise be delayed long enough
        // that CheckCommand's HTTP probe fires before listener.Start() runs — the probe
        // would fail fast, then GetContext() would block forever waiting for a request
        // that already came and went.
        listener.Start();

        string? capturedAuthHeader = null;
        var listenerTask = Task.Run(async () =>
        {
            try
            {
                var context = await listener.GetContextAsync();
                capturedAuthHeader = context.Request.Headers["Authorization"];

                var responseBody = JsonSerializer.Serialize(new { data = new[] { new { id = "test-model" } } });
                var responseBytes = System.Text.Encoding.UTF8.GetBytes(responseBody);
                context.Response.ContentLength64 = responseBytes.Length;
                context.Response.OutputStream.Write(responseBytes, 0, responseBytes.Length);
                context.Response.OutputStream.Close();
            }
            catch (HttpListenerException) { /* listener stopped from outer finally */ }
            catch (ObjectDisposedException) { /* listener closed from outer finally */ }
        });

        try
        {
            const string testKey = "test-azure-key-12345";
            Environment.SetEnvironmentVariable("AIGEST_PROVIDER", "azure");
            Environment.SetEnvironmentVariable("AIGEST_API_KEY", testKey);
            Environment.SetEnvironmentVariable("AIGEST_BASE_URL", baseUrl);
            Environment.SetEnvironmentVariable("AIGEST_MODEL", "test-model");
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _tempDir);
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", "");
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", "");
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", "");
            Environment.SetEnvironmentVariable("AIGEST_THINKING_EFFORT", "");

            var (stdout, _, exitCode) = CaptureRun();

            // Bounded wait — fail fast instead of hanging if the probe never reached the listener.
            await listenerTask.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(0, exitCode);
            Assert.Contains($"Bearer {testKey}", capturedAuthHeader ?? "");
            Assert.Contains("Reachable   : yes (1 models available)", stdout);
        }
        finally
        {
            // Stop first so a pending GetContextAsync unblocks with HttpListenerException.
            try { listener.Stop(); } catch { }
            try { listener.Close(); } catch { }
        }
    }

    private static int GetFreePort()
    {
        // Find a free port by binding to 0 and reading the assigned port.
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static (string stdout, string stderr, int exitCode) CaptureRun()
    {
        var origOut = Console.Out;
        var origErr = Console.Error;

        using var outWriter = new StringWriter();
        using var errWriter = new StringWriter();
        Console.SetOut(outWriter);
        Console.SetError(errWriter);
        int exit;
        try
        {
            exit = CheckCommand.Run();
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }

        return (outWriter.ToString(), errWriter.ToString(), exit);
    }
}
