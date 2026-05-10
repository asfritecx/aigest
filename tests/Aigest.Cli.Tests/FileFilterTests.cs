using Aigest.Cli.Core;
using Xunit;

namespace Aigest.Cli.Tests;

public class FileFilterTests
{
    [Theory]
    [InlineData(".env")]
    [InlineData(".env.local")]
    [InlineData("secrets.json")]
    [InlineData("my-secret.txt")]
    [InlineData("passwords.txt")]
    [InlineData("credentials.yml")]
    [InlineData("private.key")]
    [InlineData("app.pfx")]
    [InlineData("cert.pem")]
    [InlineData("data.sqlite")]
    [InlineData("backup.bak")]
    [InlineData("archive.zip")]
    [InlineData("image.png")]
    [InlineData("doc.pdf")]
    [InlineData("appsettings.Production.json")]
    [InlineData("sub/dir/.env")]
    [InlineData("sub/dir/secrets.json")]
    // Fix 1 — case-insensitive denylist
    [InlineData("Secrets.json")]
    [InlineData(".ENV")]
    [InlineData("appsettings.production.json")]
    // Fix 3 — extended denylist
    [InlineData("appsettings.Staging.json")]
    [InlineData("appsettings.Dev.json")]
    [InlineData("id_rsa")]
    [InlineData("sub/dir/id_ed25519")]
    [InlineData(".netrc")]
    [InlineData(".npmrc")]
    [InlineData("access_token")]
    [InlineData("cert.p12")]
    [InlineData(".aws/credentials")]
    [InlineData("kubeconfig")]
    public void IsDenied_MatchesDefaultPatterns(string path)
    {
        Assert.True(FileFilter.IsDenied(path));
    }

    [Theory]
    [InlineData("Program.cs")]
    [InlineData("README.md")]
    [InlineData("config.yaml")]
    [InlineData("src/app.js")]
    [InlineData("appsettings.json")]
    [InlineData("TokenService.cs")]
    [InlineData("JwtTokenHandler.cs")]
    public void IsDenied_DoesNotMatchSafeFiles(string path)
    {
        Assert.False(FileFilter.IsDenied(path));
    }

    [Fact]
    public void IsDenied_RespectsExtraPatterns()
    {
        Assert.False(FileFilter.IsDenied("custom.tmp"));
        Assert.True(FileFilter.IsDenied("custom.tmp", ["*.tmp"]));
    }

    [Fact]
    public void IsDenied_ExtraPatterns_AreCaseInsensitive()
    {
        Assert.True(FileFilter.IsDenied("MY_SECRETS.txt", ["*secrets*"]));
        Assert.True(FileFilter.IsDenied("MY_SECRETS.txt", ["*SECRETS*"]));
    }

    [Theory]
    [InlineData("Program.cs")]
    [InlineData("app.py")]
    [InlineData("config.json")]
    [InlineData("Dockerfile")]
    [InlineData("Makefile")]
    [InlineData("README")]
    [InlineData("LICENSE")]
    public void IsProbablyText_AllowsTextFiles(string path)
    {
        Assert.True(FileFilter.IsProbablyText(path));
    }

    [Theory]
    [InlineData("image.png")]
    [InlineData("binary.exe")]
    [InlineData("data.bin")]
    public void IsProbablyText_RejectsBinaryFiles(string path)
    {
        Assert.False(FileFilter.IsProbablyText(path));
    }
}
