using System;
using System.IO;
using System.Linq;
using Aigest.Cli.Core;
using Xunit;

namespace Aigest.Cli.Tests;

public class CorpusLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public CorpusLoaderTests()
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
    public void Load_IncludesTextFiles_WithLineNumbers()
    {
        File.WriteAllText(Path.Combine(_tempDir, "a.txt"), "line1\nline2");

        var result = CorpusLoader.Load([_tempDir], 1_000_000, 1_000_000);

        Assert.Single(result.Included);
        Assert.Contains("1: line1", result.Corpus);
        Assert.Contains("2: line2", result.Corpus);
        Assert.Contains("<file path=", result.Corpus);
        Assert.Contains("line_numbered='true'", result.Corpus);
    }

    [Fact]
    public void Load_EmitsGlobalTreeBlockBeforeFileBlocks()
    {
        File.WriteAllText(Path.Combine(_tempDir, "a.txt"), "x");
        File.WriteAllText(Path.Combine(_tempDir, "b.txt"), "y");

        var result = CorpusLoader.Load([_tempDir], 1_000_000, 1_000_000);

        var treeIdx = result.Corpus.IndexOf("<tree", StringComparison.Ordinal);
        var fileIdx = result.Corpus.IndexOf("<file path=", StringComparison.Ordinal);
        Assert.True(treeIdx >= 0, "Corpus should contain a <tree> block");
        Assert.True(fileIdx > treeIdx, "Tree must precede the <file> blocks");
        Assert.Contains("a.txt", result.Corpus);
        Assert.Contains("b.txt", result.Corpus);
    }

    [Fact]
    public void Load_RespectsMaxFileBytes()
    {
        File.WriteAllText(Path.Combine(_tempDir, "big.txt"), new string('x', 200));
        File.WriteAllText(Path.Combine(_tempDir, "small.txt"), "ok");

        var result = CorpusLoader.Load([_tempDir], 100, 1_000_000);

        Assert.Single(result.Included);
        Assert.Contains("small.txt", result.Included[0]);
        Assert.Single(result.Warnings);
        Assert.Contains("oversized", result.Warnings[0]);
    }

    [Fact]
    public void Load_RespectsMaxTotalBytes()
    {
        File.WriteAllText(Path.Combine(_tempDir, "a.txt"), new string('x', 200));
        File.WriteAllText(Path.Combine(_tempDir, "b.txt"), new string('x', 200));

        var result = CorpusLoader.Load([_tempDir], 1_000_000, 300);

        Assert.Single(result.Included);
        Assert.Single(result.Warnings);
        Assert.Contains("total corpus limit", result.Warnings[0]);
    }

    [Fact]
    public void Load_SkipsDeniedFiles()
    {
        File.WriteAllText(Path.Combine(_tempDir, ".env"), "SECRET=1");
        File.WriteAllText(Path.Combine(_tempDir, "safe.txt"), "ok");

        var result = CorpusLoader.Load([_tempDir], 1_000_000, 1_000_000);

        Assert.Single(result.Included);
        Assert.Contains("safe.txt", result.Included[0]);
        Assert.Single(result.Warnings);
        Assert.Contains("Denied", result.Warnings[0]);
    }

    [Fact]
    public void Load_SkipsNonTextFiles()
    {
        File.WriteAllBytes(Path.Combine(_tempDir, "data.bin"), [0x00, 0x01]);
        File.WriteAllText(Path.Combine(_tempDir, "safe.txt"), "ok");

        var result = CorpusLoader.Load([_tempDir], 1_000_000, 1_000_000);

        Assert.Single(result.Included);
        Assert.Contains("safe.txt", result.Included[0]);
        Assert.Single(result.Warnings);
        Assert.Contains("non-allowlisted", result.Warnings[0]);
    }

    [Fact]
    public void Load_ExpandsGlobPatterns()
    {
        File.WriteAllText(Path.Combine(_tempDir, "a.cs"), "class A {}");
        File.WriteAllText(Path.Combine(_tempDir, "b.txt"), "text");

        var result = CorpusLoader.Load([Path.Combine(_tempDir, "*.cs")], 1_000_000, 1_000_000);

        Assert.Single(result.Included);
        Assert.Contains("a.cs", result.Included[0]);
    }

    [Fact]
    public void Load_HandlesNestedDirectories()
    {
        var sub = Path.Combine(_tempDir, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "nested.txt"), "deep");

        var result = CorpusLoader.Load([_tempDir], 1_000_000, 1_000_000);

        Assert.Single(result.Included);
        Assert.Contains("nested.txt", result.Included[0]);
    }

    [Fact]
    public void Load_DeduplicatesPaths()
    {
        File.WriteAllText(Path.Combine(_tempDir, "a.txt"), "dup");

        var result = CorpusLoader.Load(
            [Path.Combine(_tempDir, "a.txt"), Path.Combine(_tempDir, "a.txt")],
            1_000_000,
            1_000_000);

        Assert.Single(result.Included);
    }

    [Fact]
    public void Load_SilentlySkipsMissingPaths()
    {
        var originalError = Console.Error;
        var stderr = new StringWriter();
        Console.SetError(stderr);
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                CorpusLoader.Load([Path.Combine(_tempDir, "missing.cs")], 1_000_000, 1_000_000));

            Assert.Contains("No readable files", ex.Message);
            Assert.Equal(string.Empty, stderr.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }


    [Fact]
    public void Load_XmlEscapesFilePathInCorpus()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        // Single-quote in filename — valid on Linux/macOS
        var filePath = Path.Combine(dir, "it's-a-test.cs");
        File.WriteAllText(filePath, "// test");
        try
        {
            var result = CorpusLoader.Load([filePath], 100_000, 1_000_000);
            Assert.Contains("&apos;", result.Corpus);
            // The attribute value should not contain a raw single quote
            Assert.DoesNotContain($"path='{filePath}'", result.Corpus);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_SkipsFileExceedingMaxFileBytesByUtf8Count()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, "large.cs");
        // Write 10 chars that are 3 bytes each in UTF-8 (30 bytes total)
        var content = new string('€', 10); // € is U+20AC, 3 bytes in UTF-8
        File.WriteAllText(filePath, content, System.Text.Encoding.Unicode);
        try
        {
            // maxFileBytes = 25 — below the 30 UTF-8 bytes
            var result = Assert.Throws<InvalidOperationException>(() =>
                CorpusLoader.Load([filePath], maxFileBytes: 25, maxTotalBytes: 1_000_000));
            // File was skipped — throws because no files included
            Assert.Contains("No readable files", result.Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
