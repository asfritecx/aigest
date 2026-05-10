using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Aigest.Cli.Commands;

public static class ExtractChatCommand
{
    private const int MaxTextCharsPerItem = 12000;

    public static int Run(string inputPath, string outputPath)
    {
        var input = Path.GetFullPath(Environment.ExpandEnvironmentVariables(inputPath));
        var output = Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputPath));

        if (!File.Exists(input))
        {
            Console.Error.WriteLine($"Input file not found: {input}");
            return 1;
        }

        var sections = new List<string>();
        var lineNo = 0;

        foreach (var raw in File.ReadLines(input))
        {
            lineNo++;
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line))
                continue;

            JsonElement item;
            try
            {
                item = JsonDocument.Parse(line).RootElement;
            }
            catch (JsonException)
            {
                continue;
            }

            var role =
                (item.TryGetProperty("role", out var r) ? r.GetString() : null)
                ?? (item.TryGetProperty("type", out var t) ? t.GetString() : null)
                ?? (item.TryGetProperty("speaker", out var s) ? s.GetString() : null)
                ?? "unknown";

            var texts = ExtractText(item);
            if (texts.Count == 0)
                continue;

            var clean = string.Join("\n\n", texts.Distinct());
            sections.Add($"## {role} / line {lineNo}\n\n{clean}");
        }

        if (sections.Count == 0)
        {
            Console.Error.WriteLine("No readable chat text found.");
            return 1;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, string.Join("\n\n---\n\n", sections) + Environment.NewLine);

        Console.WriteLine(output);
        return 0;
    }

    private static List<string> ExtractText(JsonElement element)
    {
        var results = new List<string>();
        ExtractTextCore(element, results);
        return results;
    }

    private static void ExtractTextCore(JsonElement element, List<string> results)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var s = element.GetString();
                if (!string.IsNullOrWhiteSpace(s) && s.Length <= MaxTextCharsPerItem)
                    results.Add(s.Trim());
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    ExtractTextCore(item, results);
                break;

            case JsonValueKind.Object:
                foreach (var key in new[] { "text", "content", "message", "prompt", "response" })
                {
                    if (element.TryGetProperty(key, out var prop))
                        ExtractTextCore(prop, results);
                }
                break;
        }
    }
}
