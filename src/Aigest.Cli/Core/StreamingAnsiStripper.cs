using System.Text;

namespace Aigest.Cli.Core;

internal sealed class StreamingAnsiStripper
{
    private const int MaxPending = 256;
    private string _pending = string.Empty;

    internal string StripChunk(string chunk)
    {
        if (string.IsNullOrEmpty(chunk))
            return string.Empty;

        var input = _pending + chunk;
        _pending = string.Empty;

        var output = new StringBuilder(input.Length);
        var i = 0;
        while (i < input.Length)
        {
            if (input[i] != '\u001b')
            {
                output.Append(input[i++]);
                continue;
            }

            if (i == input.Length - 1)
            {
                HoldPending(input[i..]);
                break;
            }

            var marker = input[i + 1];
            if (marker == '[')
            {
                var end = FindCsiEnd(input, i + 2);
                if (end < 0)
                {
                    HoldPending(input[i..]);
                    break;
                }
                i = end + 1;
                continue;
            }

            if (marker == ']')
            {
                var end = FindOscEnd(input, i + 2);
                if (end < 0)
                {
                    HoldPending(input[i..]);
                    break;
                }
                i = end;
                continue;
            }

            if (marker is >= '@' and <= '_')
            {
                i += 2;
                continue;
            }

            output.Append(input[i++]);
        }

        return output.ToString();
    }

    internal string Flush()
    {
        if (_pending.Length == 0)
            return string.Empty;

        var pending = _pending;
        _pending = string.Empty;
        if (pending.StartsWith('\u001b'))
            return string.Empty;

        return AnsiEscapes.Strip(pending);
    }

    private void HoldPending(string value)
    {
        if (value.Length > MaxPending)
        {
            _pending = string.Empty;
            return;
        }

        _pending = value;
    }

    private static int FindCsiEnd(string input, int start)
    {
        for (var i = start; i < input.Length; i++)
        {
            if (input[i] is >= '@' and <= '~')
                return i;
        }

        return -1;
    }

    private static int FindOscEnd(string input, int start)
    {
        for (var i = start; i < input.Length; i++)
        {
            if (input[i] == '\u0007')
                return i + 1;

            if (input[i] == '\u001b' && i + 1 < input.Length && input[i + 1] == '\\')
                return i + 2;
        }

        return -1;
    }
}
