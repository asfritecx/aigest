using System.Text;

namespace Aigest.Cli.Core;

public static class DirectoryTreeRenderer
{
    public static string Render(IEnumerable<string> filePaths, string? basePath = null)
    {
        var paths = filePaths.ToList();
        var attr = basePath is not null
            ? $" base='{System.Security.SecurityElement.Escape(basePath)}'"
            : string.Empty;

        if (paths.Count == 0)
            return $"<tree{attr}></tree>";

        var relPaths = paths
            .Select(p => Relativize(p, basePath))
            .Select(NormalizeSeparators)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        var sb = new StringBuilder();
        sb.Append("<tree").Append(attr).Append(">\n");

        string[] previousSegments = [];
        foreach (var rel in relPaths)
        {
            var segments = rel.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                if (i < previousSegments.Length && segments[i] == previousSegments[i])
                    continue;

                var indent = new string(' ', i * 2);
                if (i < segments.Length - 1)
                    sb.Append(indent).Append(segments[i]).Append("/\n");
                else
                    sb.Append(indent).Append(segments[i]).Append('\n');
            }
            previousSegments = segments;
        }

        sb.Append("</tree>");
        return sb.ToString();
    }

    private static string Relativize(string path, string? basePath)
    {
        if (basePath is null) return path;
        if (!path.StartsWith(basePath, StringComparison.Ordinal))
            return path;

        var rel = path[basePath.Length..].TrimStart(Path.DirectorySeparatorChar, '/');
        return rel.Length == 0 ? Path.GetFileName(path) : rel;
    }

    private static string NormalizeSeparators(string path) =>
        path.Replace('\\', '/');
}
