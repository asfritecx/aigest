using Aigest.Cli.Logging;
using Microsoft.Extensions.Logging;

namespace Aigest.Cli.Core;

public sealed record CorpusResult(string Corpus, IReadOnlyList<string> Included, IReadOnlyList<string> Warnings);

public sealed record LoadedFile(string Path, string LineNumberedContent);

public sealed record CorpusLoadDetails(
    IReadOnlyList<LoadedFile> Files,
    IReadOnlyList<string> Warnings);

public static class CorpusLoader
{
    public static CorpusLoadDetails LoadFiles(
        IEnumerable<string> pathPatterns,
        int maxFileBytes,
        int maxTotalBytes,
        IEnumerable<string>? extraDenyPatterns = null,
        ILogger? logger = null)
    {
        var paths = ExpandPaths(pathPatterns, logger);
        var files = new List<LoadedFile>();
        var warnings = new List<string>();
        var total = 0L;

        foreach (var path in paths)
        {
            if (FileFilter.IsDenied(path, extraDenyPatterns))
            {
                warnings.Add($"Denied by safety rules: {path}");
                continue;
            }

            if (!FileFilter.IsProbablyText(path))
            {
                warnings.Add($"Skipped non-allowlisted file type: {path}");
                continue;
            }

            // Coarse pre-check — avoids loading clearly-oversized files into memory
            var roughSize = new FileInfo(path).Length;
            if (roughSize > maxFileBytes)
            {
                warnings.Add($"Skipped oversized file ({roughSize} bytes): {path}");
                continue;
            }

            string content;
            try
            {
                content = File.ReadAllText(path);
            }
            catch (IOException ex)
            {
                warnings.Add($"Skipped unreadable text encoding: {path} ({ex.Message})");
                continue;
            }

            var size = System.Text.Encoding.UTF8.GetByteCount(content);
            if (size > maxFileBytes)
            {
                warnings.Add($"Skipped oversized file ({size} bytes): {path}");
                continue;
            }

            if (total + size > maxTotalBytes)
            {
                warnings.Add($"Skipped because total corpus limit would be exceeded ({maxTotalBytes} bytes): {path}");
                continue;
            }

            var numbered = LineNumber(content);
            total += size;
            files.Add(new LoadedFile(path, numbered));
        }

        if (logger is not null && files.Count > 0)
            CliLog.IncludedFiles(logger, files.Count);

        return new CorpusLoadDetails(files, warnings);
    }

    public static CorpusResult Load(
        IEnumerable<string> pathPatterns,
        int maxFileBytes,
        int maxTotalBytes,
        IEnumerable<string>? extraDenyPatterns = null,
        ILogger? logger = null)
    {
        var details = LoadFiles(pathPatterns, maxFileBytes, maxTotalBytes, extraDenyPatterns, logger);

        if (details.Files.Count == 0)
        {
            throw new InvalidOperationException("No readable files were included. Check paths, denylist, and size limits.");
        }

        var corpus = RenderCorpus(details.Files);
        return new CorpusResult(
            corpus,
            details.Files.Select(f => f.Path).ToList(),
            details.Warnings);
    }

    public static string RenderCorpus(IReadOnlyList<LoadedFile> files, string? basePath = null)
    {
        var resolvedBase = basePath ?? ComputeCommonBase(files.Select(f => f.Path));
        var tree = DirectoryTreeRenderer.Render(files.Select(f => f.Path), resolvedBase);
        var blocks = files.Select(RenderFileBlock).ToList();
        return tree + "\n\n" + string.Join("\n\n", blocks);
    }

    public static string RenderFileBlock(LoadedFile file)
    {
        var escapedPath = System.Security.SecurityElement.Escape(file.Path);
        return $"<file path='{escapedPath}' line_numbered='true'>\n{file.LineNumberedContent}\n</file>";
    }

    public static string? ComputeCommonBase(IEnumerable<string> paths)
    {
        var pathList = paths.ToList();
        if (pathList.Count == 0) return null;
        if (pathList.Count == 1) return Path.GetDirectoryName(pathList[0]);

        var split = pathList.Select(p => p.Split(Path.DirectorySeparatorChar)).ToList();
        var minLen = split.Min(s => s.Length);
        int common = 0;
        for (int i = 0; i < minLen; i++)
        {
            var first = split[0][i];
            if (split.All(s => string.Equals(s[i], first, StringComparison.Ordinal)))
                common++;
            else
                break;
        }
        if (common == 0) return null;
        // Don't include the trailing filename segment as part of the base
        if (common == minLen) common--;
        if (common <= 0) return null;
        return string.Join(Path.DirectorySeparatorChar, split[0].Take(common));
    }

    private static string LineNumber(string content) =>
        string.Join(
            "\n",
            content.Split(['\n', '\r'], StringSplitOptions.None)
                   .Select((line, idx) => $"{idx + 1}: {line}")
        );

    private static List<string> ExpandPaths(IEnumerable<string> patterns, ILogger? logger = null)
    {
        var results = new List<string>();
        foreach (var pattern in patterns)
        {
            var expanded = ExpandGlob(pattern);
            if (expanded.Count == 0)
            {
                expanded = [pattern];
            }

            foreach (var item in expanded)
            {
                var resolved = Path.GetFullPath(Environment.ExpandEnvironmentVariables(item));
                if (Directory.Exists(resolved))
                {
                    foreach (var file in Directory.EnumerateFiles(resolved, "*", SearchOption.AllDirectories))
                    {
                        results.Add(file);
                    }
                }
                else if (File.Exists(resolved))
                {
                    results.Add(resolved);
                }
            }
        }

        return results.Distinct().ToList();
    }

    private static List<string> ExpandGlob(string pattern)
    {
        var results = new List<string>();

        // Handle recursive glob **
        if (pattern.Contains("**"))
        {
            var baseDir = ".";
            var searchPattern = pattern;

            var doubleStarIdx = pattern.IndexOf("**/", StringComparison.Ordinal);
            if (doubleStarIdx > 0)
            {
                baseDir = pattern[..doubleStarIdx];
                searchPattern = pattern[(doubleStarIdx + 3)..];
            }
            else if (pattern.StartsWith("**/"))
            {
                searchPattern = pattern[3..];
            }

            var fullBase = Path.GetFullPath(Environment.ExpandEnvironmentVariables(baseDir));
            if (!Directory.Exists(fullBase))
                return results;

            foreach (var file in Directory.EnumerateFiles(fullBase, searchPattern, SearchOption.AllDirectories))
            {
                results.Add(file);
            }

            return results;
        }

        // Standard glob with optional directory prefix
        var dir = Path.GetDirectoryName(pattern);
        var filePattern = Path.GetFileName(pattern);

        if (string.IsNullOrEmpty(dir))
        {
            dir = ".";
        }

        var fullDir = Path.GetFullPath(Environment.ExpandEnvironmentVariables(dir));
        if (!Directory.Exists(fullDir))
            return results;

        foreach (var file in Directory.EnumerateFiles(fullDir, filePattern, SearchOption.TopDirectoryOnly))
        {
            results.Add(file);
        }

        return results;
    }
}
