using System.Text;

namespace CaelestiaWin.Terminal;

public sealed class TerminalLine
{
    public const string DefaultForeground = "#FFF0F7FA";
    public const string MutedForeground = "#99B5C6D1";
    public const string AccentForeground = "#FF79E6F5";
    public const string ErrorForeground = "#FFFF6B7A";
    private const char Escape = '\u001b';

    public TerminalLine(IReadOnlyList<TerminalTextRun> runs)
    {
        Runs = runs;
    }

    public IReadOnlyList<TerminalTextRun> Runs { get; }

    public static TerminalLine Empty { get; } = FromPlain(string.Empty);

    public static TerminalLine FromPlain(string text, string foreground = DefaultForeground)
    {
        return new TerminalLine([new TerminalTextRun(text, foreground)]);
    }

    public static TerminalLine FromSegments(params TerminalTextRun[] runs)
    {
        return new TerminalLine(runs);
    }

    public static TerminalLine FromAnsi(string text, string fallbackForeground = DefaultForeground)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Empty;
        }

        var runs = new List<TerminalTextRun>();
        var buffer = new StringBuilder(text.Length);
        var foreground = fallbackForeground;

        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != Escape || index + 1 >= text.Length)
            {
                buffer.Append(text[index]);
                continue;
            }

            if (text[index + 1] == '[')
            {
                var terminator = FindCsiTerminator(text, index + 2);
                if (terminator < 0)
                {
                    buffer.Append(text[index]);
                    continue;
                }

                var command = text[terminator];
                if (command == 'm')
                {
                    FlushRun(runs, buffer, foreground);
                    ApplySgr(text.AsSpan(index + 2, terminator - index - 2), fallbackForeground, ref foreground);
                }
                else if (command == 'K')
                {
                    // Progress renderers commonly use ESC[K / ESC[2K to redraw one terminal line.
                    runs.Clear();
                    buffer.Clear();
                }

                index = terminator;
                continue;
            }

            if (text[index + 1] == ']')
            {
                var terminator = FindOscTerminator(text, index + 2);
                if (terminator < 0)
                {
                    buffer.Append(text[index]);
                    continue;
                }

                index = terminator;
                continue;
            }

            buffer.Append(text[index]);
        }

        FlushRun(runs, buffer, foreground);
        return runs.Count == 0 ? Empty : new TerminalLine(runs);
    }

    private static int FindCsiTerminator(string text, int startIndex)
    {
        for (var index = startIndex; index < text.Length; index++)
        {
            if (text[index] is >= '@' and <= '~')
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindOscTerminator(string text, int startIndex)
    {
        for (var index = startIndex; index < text.Length; index++)
        {
            if (text[index] == '\a')
            {
                return index;
            }

            if (text[index] == Escape && index + 1 < text.Length && text[index + 1] == '\\')
            {
                return index + 1;
            }
        }

        return -1;
    }

    private static void ApplySgr(ReadOnlySpan<char> sequence, string fallbackForeground, ref string foreground)
    {
        if (sequence.IsEmpty)
        {
            foreground = fallbackForeground;
            return;
        }

        var codes = sequence.ToString()
            .Split(';')
            .Select(codeText => int.TryParse(codeText, out var parsed) ? parsed : 0)
            .ToArray();

        for (var index = 0; index < codes.Length; index++)
        {
            var code = codes[index];
            if (code == 38 && TryReadExtendedColor(codes, ref index, out var extendedForeground))
            {
                foreground = extendedForeground;
                continue;
            }

            foreground = code switch
            {
                0 or 39 => fallbackForeground,
                30 => "#FF7B8794",
                31 => "#FFFF6B7A",
                32 => "#FF8BE98B",
                33 => "#FFF7D774",
                34 => "#FF7EB7FF",
                35 => "#FFE58CFF",
                36 => "#FF79E6F5",
                37 => "#FFF0F7FA",
                90 => "#FF8FA7B7",
                91 => "#FFFF8A98",
                92 => "#FFA6F5A6",
                93 => "#FFFFE49A",
                94 => "#FFA6CDFF",
                95 => "#FFF0A8FF",
                96 => "#FFA1F4FF",
                97 => "#FFFFFFFF",
                _ => foreground
            };
        }
    }

    private static bool TryReadExtendedColor(IReadOnlyList<int> codes, ref int index, out string color)
    {
        color = DefaultForeground;
        if (index + 1 >= codes.Count)
        {
            return false;
        }

        var mode = codes[index + 1];
        if (mode == 2 && index + 4 < codes.Count)
        {
            var red = Math.Clamp(codes[index + 2], 0, 255);
            var green = Math.Clamp(codes[index + 3], 0, 255);
            var blue = Math.Clamp(codes[index + 4], 0, 255);
            color = $"#FF{red:X2}{green:X2}{blue:X2}";
            index += 4;
            return true;
        }

        if (mode == 5 && index + 2 < codes.Count)
        {
            color = FromAnsi256(Math.Clamp(codes[index + 2], 0, 255));
            index += 2;
            return true;
        }

        return false;
    }

    private static string FromAnsi256(int code)
    {
        if (code < 16)
        {
            return code switch
            {
                0 => "#FF0A1118",
                1 => "#FFFF6B7A",
                2 => "#FF8BE98B",
                3 => "#FFF7D774",
                4 => "#FF7EB7FF",
                5 => "#FFE58CFF",
                6 => "#FF79E6F5",
                7 => "#FFF0F7FA",
                8 => "#FF8FA7B7",
                9 => "#FFFF8A98",
                10 => "#FFA6F5A6",
                11 => "#FFFFE49A",
                12 => "#FFA6CDFF",
                13 => "#FFF0A8FF",
                14 => "#FFA1F4FF",
                _ => "#FFFFFFFF"
            };
        }

        if (code is >= 232 and <= 255)
        {
            var level = 8 + ((code - 232) * 10);
            return $"#FF{level:X2}{level:X2}{level:X2}";
        }

        var cube = code - 16;
        var red = AnsiCubeComponent(cube / 36);
        var green = AnsiCubeComponent((cube / 6) % 6);
        var blue = AnsiCubeComponent(cube % 6);
        return $"#FF{red:X2}{green:X2}{blue:X2}";
    }

    private static int AnsiCubeComponent(int value)
    {
        return value == 0 ? 0 : 55 + (value * 40);
    }

    private static void FlushRun(ICollection<TerminalTextRun> runs, StringBuilder buffer, string foreground)
    {
        if (buffer.Length == 0)
        {
            return;
        }

        runs.Add(new TerminalTextRun(buffer.ToString(), foreground));
        buffer.Clear();
    }
}
