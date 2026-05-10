using System.Text.RegularExpressions;

namespace Aigest.Cli.Core;

internal static class AnsiEscapes
{
    // Covers Fe sequences (ESC + single byte), CSI (ESC [ ... final), and OSC (ESC ] ... BEL/ST).
    // Strips echoback vectors and cursor-control sequences that could corrupt a terminal or hijack
    // clipboard state when LLM output is printed to stdout or written to disk.
    private static readonly Regex Pattern = new(
        @"\x1B(?:\[[0-9;]*[ -/]*[@-~]|\].*?(?:\x07|\x1B\\)|[@-Z\\-_])",
        RegexOptions.Compiled | RegexOptions.Singleline);

    internal static string Strip(string input) =>
        Pattern.Replace(input, string.Empty);
}
