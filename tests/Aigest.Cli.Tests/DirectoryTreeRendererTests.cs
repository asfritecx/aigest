using System.IO;
using Aigest.Cli.Core;
using Xunit;

namespace Aigest.Cli.Tests;

public class DirectoryTreeRendererTests
{
    [Fact]
    public void Render_EmptyList_EmitsEmptyTree()
    {
        var output = DirectoryTreeRenderer.Render([]);
        Assert.Equal("<tree></tree>", output);
    }

    [Fact]
    public void Render_SingleFile_NoBase_ListsAbsolutePath()
    {
        var output = DirectoryTreeRenderer.Render(["/a/b/c.cs"]);
        Assert.Contains("<tree>", output);
        Assert.Contains("c.cs", output);
        Assert.Contains("</tree>", output);
    }

    [Fact]
    public void Render_WithBase_StripsBaseAndIndentsByDepth()
    {
        var sep = Path.DirectorySeparatorChar;
        var basePath = $"src";
        var files = new[]
        {
            $"src{sep}Commands{sep}AskCommand.cs",
            $"src{sep}Commands{sep}WriteCommand.cs",
            $"src{sep}Core{sep}CorpusLoader.cs",
        };

        var output = DirectoryTreeRenderer.Render(files, basePath);

        Assert.Contains("base='src'", output);
        Assert.Contains("Commands/", output);
        Assert.Contains("  AskCommand.cs", output);
        Assert.Contains("  WriteCommand.cs", output);
        Assert.Contains("Core/", output);
        Assert.Contains("  CorpusLoader.cs", output);
    }

    [Fact]
    public void Render_DoesNotRepeatSharedDirectoryHeaders()
    {
        var sep = Path.DirectorySeparatorChar;
        var basePath = "root";
        var files = new[]
        {
            $"root{sep}a{sep}1.cs",
            $"root{sep}a{sep}2.cs",
            $"root{sep}a{sep}3.cs",
        };

        var output = DirectoryTreeRenderer.Render(files, basePath);

        // Only one "a/" header should appear.
        var directoryLineCount = output.Split('\n')
            .Count(l => l.TrimStart() == "a/");
        Assert.Equal(1, directoryLineCount);
    }

    [Fact]
    public void Render_HandlesMixedRootAndNestedFiles()
    {
        var sep = Path.DirectorySeparatorChar;
        var basePath = "r";
        var files = new[]
        {
            $"r{sep}a.cs",
            $"r{sep}sub{sep}b.cs",
        };

        var output = DirectoryTreeRenderer.Render(files, basePath);

        Assert.Contains("a.cs", output);
        Assert.Contains("sub/", output);
        Assert.Contains("  b.cs", output);
    }
}
