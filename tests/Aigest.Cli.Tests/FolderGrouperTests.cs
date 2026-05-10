using System;
using System.IO;
using System.Linq;
using Aigest.Cli.Core;
using Xunit;

namespace Aigest.Cli.Tests;

public class FolderGrouperTests : IDisposable
{
    private readonly string _tempDir;

    public FolderGrouperTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Group_EmptyList_ReturnsEmpty()
    {
        var groups = FolderGrouper.Group([]);
        Assert.Empty(groups);
    }

    [Fact]
    public void Group_SiblingFolders_ProducesOneGroupPerSubfolder()
    {
        var commandsDir = Path.Combine(_tempDir, "Commands");
        var coreDir = Path.Combine(_tempDir, "Core");
        Directory.CreateDirectory(commandsDir);
        Directory.CreateDirectory(coreDir);
        File.WriteAllText(Path.Combine(commandsDir, "Ask.cs"), "// a");
        File.WriteAllText(Path.Combine(commandsDir, "Write.cs"), "// w");
        File.WriteAllText(Path.Combine(coreDir, "Loader.cs"), "// l");

        var details = CorpusLoader.LoadFiles([_tempDir], 1_000_000, 1_000_000);
        var groups = FolderGrouper.Group(details.Files);

        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, g => g.FolderPath.EndsWith("Commands"));
        Assert.Contains(groups, g => g.FolderPath.EndsWith("Core"));

        var commandsGroup = groups.First(g => g.FolderPath.EndsWith("Commands"));
        Assert.Equal(2, commandsGroup.Files.Count);
        Assert.Contains("Ask.cs", commandsGroup.DirectoryTree);
        Assert.Contains("Write.cs", commandsGroup.DirectoryTree);
    }

    [Fact]
    public void Group_AllFilesUnderSameLeaf_ReturnsSingleGroup()
    {
        var dir = Path.Combine(_tempDir, "OnlyFolder");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.cs"), "// 1");
        File.WriteAllText(Path.Combine(dir, "b.cs"), "// 2");

        var details = CorpusLoader.LoadFiles([dir], 1_000_000, 1_000_000);
        var groups = FolderGrouper.Group(details.Files);

        Assert.Single(groups);
        Assert.Equal(2, groups[0].Files.Count);
    }

    [Fact]
    public void Group_PerFolderCorpusContainsScopedTreeAndFileBlocks()
    {
        var sub = Path.Combine(_tempDir, "Pkg");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "x.cs"), "line1\nline2");

        var details = CorpusLoader.LoadFiles([sub], 1_000_000, 1_000_000);
        var groups = FolderGrouper.Group(details.Files);

        Assert.Single(groups);
        var g = groups[0];
        Assert.Contains("<tree", g.Corpus);
        Assert.Contains("<file path=", g.Corpus);
        Assert.Contains("1: line1", g.Corpus);
        Assert.Contains("2: line2", g.Corpus);
    }
}
