using System.IO;

namespace Aigest.Cli.Core;

public static class FileFilter
{
    private static readonly string[] DefaultDenyPatterns =
    [
        ".env",
        ".env.*",
        "**/.env",
        "**/.env.*",
        "**/secrets.json",
        "**/*secret*",
        "**/*password*",
        "**/*credential*",
        "**/*private*",
        // SSH private keys (extensionless — no extension, so extension-based deny won't catch them)
        "**/id_rsa",
        "**/id_ed25519",
        "**/id_ecdsa",
        "**/id_dsa",
        // Credential stores
        "**/.netrc",
        "**/.npmrc",
        "**/.pypirc",
        // Token files (narrow patterns only — avoid false positives on e.g. TokenService.cs)
        "**/*.token",
        "**/access_token",
        "**/refresh_token",
        "**/.token",
        // PKCS#12 bundles
        "**/*.p12",
        // Cloud/k8s credentials
        "**/.aws/credentials",
        "**/.aws/config",
        "**/kubeconfig",
        "**/.kube/config",
        // All appsettings environment variants
        "**/appsettings.*.json",
        "**/*.pfx",
        "**/*.pem",
        "**/*.key",
        "**/*.cer",
        "**/*.crt",
        "**/*.der",
        "**/*.kdbx",
        "**/*.sqlite",
        "**/*.sqlite3",
        "**/*.db",
        "**/*.bak",
        "**/*.bacpac",
        "**/*.dacpac",
        "**/*.zip",
        "**/*.7z",
        "**/*.rar",
        "**/*.tar",
        "**/*.gz",
        "**/*.png",
        "**/*.jpg",
        "**/*.jpeg",
        "**/*.gif",
        "**/*.webp",
        "**/*.pdf",
        "**/*.doc",
        "**/*.docx",
        "**/*.xls",
        "**/*.xlsx",
    ];

    private static readonly HashSet<string> DefaultAllowExtensions =
    [
        ".cs", ".fs", ".vb", ".js", ".jsx", ".ts", ".tsx", ".py", ".ps1",
        ".sh", ".bash", ".zsh", ".go", ".rs", ".java", ".kt", ".kts",
        ".cpp", ".c", ".h", ".hpp", ".sql", ".json", ".jsonc", ".xml",
        ".yaml", ".yml", ".toml", ".ini", ".config", ".md", ".txt",
        ".html", ".css", ".scss", ".csproj", ".sln", ".props", ".targets",
        ".bicep", ".tf", ".dockerfile",
    ];

    public static bool IsDenied(string path, IEnumerable<string>? extraPatterns = null)
    {
        var name = Path.GetFileName(path).ToLowerInvariant();
        var posixPath = path.Replace('\\', '/').ToLowerInvariant();

        foreach (var pattern in DefaultDenyPatterns)
        {
            if (Matches(posixPath, name, pattern))
                return true;
        }

        if (extraPatterns is not null)
        {
            foreach (var pattern in extraPatterns)
            {
                if (Matches(posixPath, name, pattern.ToLowerInvariant()))  // normalize so uppercase patterns work against lowercased input
                    return true;
            }
        }

        return false;
    }

    public static bool IsProbablyText(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (DefaultAllowExtensions.Contains(ext))
            return true;

        var name = Path.GetFileName(path).ToLowerInvariant();
        return name is "dockerfile" or "makefile" or "readme" or "license";
    }

    private static bool Matches(string posixPath, string name, string pattern)
    {
        if (pattern.Contains('/'))
        {
            // Simple glob matching for patterns with /
            if (GlobMatch(posixPath, pattern))
                return true;
        }
        else
        {
            if (GlobMatch(name, pattern))
                return true;
        }

        return false;
    }

    private static bool GlobMatch(string input, string pattern)
    {
        // Support **/ prefix and * / ? wildcards
        if (pattern.StartsWith("**/"))
        {
            var suffix = pattern[3..];
            // Match at any directory depth
            return EndsWithGlobMatch(input, suffix) ||
                   (input.Contains('/' + suffix.TrimStart('*', '?')) &&
                    GlobMatch(input, suffix)); // recursive fallback
        }

        return SimpleGlobMatch(input, pattern);
    }

    private static bool EndsWithGlobMatch(string input, string pattern)
    {
        // Quick check for simple suffix after **/
        if (!pattern.Contains('*') && !pattern.Contains('?'))
        {
            return input == pattern || input.EndsWith('/' + pattern);
        }
        return SimpleGlobMatch(input, pattern) ||
               (input.Contains('/') && SimpleGlobMatch(input[(input.LastIndexOf('/') + 1)..], pattern));
    }

    private static bool SimpleGlobMatch(string input, string pattern)
    {
        // Basic fnmatch-style glob matching
        int i = 0, j = 0;
        int star = -1, match = 0;

        while (i < input.Length)
        {
            if (j < pattern.Length && (pattern[j] == input[i] || pattern[j] == '?'))
            {
                i++;
                j++;
            }
            else if (j < pattern.Length && pattern[j] == '*')
            {
                star = j++;
                match = i;
            }
            else if (star >= 0)
            {
                j = star + 1;
                i = ++match;
            }
            else
            {
                return false;
            }
        }

        while (j < pattern.Length && pattern[j] == '*')
            j++;

        return j == pattern.Length;
    }
}
