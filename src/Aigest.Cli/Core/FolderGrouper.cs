namespace Aigest.Cli.Core;

public sealed record FolderGroup(
    string FolderPath,
    IReadOnlyList<LoadedFile> Files,
    string DirectoryTree,
    string Corpus);

public static class FolderGrouper
{
    public static IReadOnlyList<FolderGroup> Group(IReadOnlyList<LoadedFile> files)
    {
        if (files.Count == 0) return [];

        var commonBase = CorpusLoader.ComputeCommonBase(files.Select(f => f.Path));
        if (commonBase is null)
        {
            // Heterogeneous roots — treat the entire set as one group rooted at "".
            var dir = DirectoryTreeRenderer.Render(files.Select(f => f.Path));
            var corpus = CorpusLoader.RenderCorpus(files);
            return [new FolderGroup("", files, dir, corpus)];
        }

        return files
            .GroupBy(f => GetBucket(f.Path, commonBase))
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => Build(g.Key, [.. g]))
            .ToList();
    }

    private static string GetBucket(string filePath, string commonBase)
    {
        var rel = filePath[commonBase.Length..].TrimStart(Path.DirectorySeparatorChar, '/');
        var firstSep = rel.IndexOfAny([Path.DirectorySeparatorChar, '/']);
        if (firstSep < 0)
        {
            // File is directly inside commonBase — bucket is commonBase itself.
            return commonBase;
        }
        var firstSegment = rel[..firstSep];
        return Path.Combine(commonBase, firstSegment);
    }

    private static FolderGroup Build(string folderPath, IReadOnlyList<LoadedFile> files)
    {
        var tree = DirectoryTreeRenderer.Render(files.Select(f => f.Path), folderPath);
        var corpus = CorpusLoader.RenderCorpus(files, folderPath);
        return new FolderGroup(folderPath, files, tree, corpus);
    }
}
