using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Spectre.Console;

internal sealed class InteractiveConsoleView : IDisposable
{
    private readonly List<string> _outputLines = [];
    private readonly List<string> _outputStyles = [];
    private readonly StringBuilder _input = new();
    private readonly Timer _resizeTimer;
    private readonly object _renderLock = new();
    private IReadOnlyList<string> _history = Array.Empty<string>();
    private string _header;
    private int _lastWidth;
    private int _lastHeight;
    private int _cursor;
    private int _scrollFromBottom;
    private string _currentOutputStyle = string.Empty;
    private bool _suspended;
    private bool _disposed;

    public InteractiveConsoleView(string header)
    {
        _header = header;
        (_lastWidth, _lastHeight) = GetTerminalSize();
        Console.Write("\u001b[?1049h\u001b[?25l");
        Render();
        _resizeTimer = new Timer(CheckForResize, null, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250));
    }

    private (string? Input, string? Shortcut) SubmitInput()
    {
        var submitted = _input.ToString();
        _input.Clear();
        _cursor = 0;
        _scrollFromBottom = 0;
        AppendText($"> {submitted}\n");
        return (submitted, null);
    }

    public static bool IsInteractive => !Console.IsInputRedirected && !Console.IsOutputRedirected;

    public void SetHeader(string header)
    {
        _header = header;
        Render();
    }

    public void AppendMarkupLine(string markup)
    {
        AppendText(RenderMarkup(markup, line: true));
    }

    public void AppendMarkup(string markup)
    {
        AppendText(RenderMarkup(markup, line: false));
    }

    public void AppendSectionHeader(string title, string style)
    {
        var safeTitle = string.Concat(title.Where(character => !char.IsControl(character)));
        _currentOutputStyle = style;
        AppendText($"\n── {safeTitle} ──\n");
    }

    public void ScrollToLastSection()
    {
        var headerIndex = _outputLines.FindLastIndex(line => line.StartsWith("── ", StringComparison.Ordinal));
        if (headerIndex < 0)
        {
            return;
        }

        var layout = GetLayout();
        var wrappedOutput = WrapOutput(layout.ContentWidth);
        var sectionStart = 0;
        for (var index = 0; index < headerIndex; index++)
        {
            sectionStart += Wrap(_outputLines[index], layout.ContentWidth).Count;
        }

        _scrollFromBottom = Math.Max(0, wrappedOutput.Count - sectionStart - layout.ViewportHeight);
        Render();
    }

    public void AppendText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        lock (_renderLock)
        {
            if (_suspended || _disposed)
            {
                return;
            }

            var wasAtBottom = _scrollFromBottom == 0;
            var contentWidth = GetLayout().ContentWidth;
            var previousLineCount = WrapOutput(contentWidth).Count;
            var normalized = SanitizeOutput(text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'));
            var parts = normalized.Split('\n');
            for (var index = 0; index < parts.Length; index++)
            {
                if (_outputLines.Count == 0)
                {
                    _outputLines.Add(string.Empty);
                    _outputStyles.Add(_currentOutputStyle);
                }

                _outputLines[^1] += parts[index];
                if (index < parts.Length - 1)
                {
                    _outputLines.Add(string.Empty);
                    _outputStyles.Add(_currentOutputStyle);
                }
            }

            if (!wasAtBottom)
            {
                _scrollFromBottom += Math.Max(0, WrapOutput(contentWidth).Count - previousLineCount);
            }

            Render();
        }
    }

    public (string? Input, string? Shortcut) ReadInput(IReadOnlyList<string> history)
    {
        _history = history;
        _input.Clear();
        _cursor = 0;
        var historyIndex = history.Count;
        var draft = string.Empty;
        Render();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            var text = _input.ToString();

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    if (key.Modifiers.HasFlag(ConsoleModifiers.Control))
                    {
                        return SubmitInput();
                    }

                    _input.Insert(_cursor, '\n');
                    _cursor++;
                    break;
                case ConsoleKey.F4:
                    return SubmitInput();
                case ConsoleKey.F2:
                    return (null, "settings");
                case ConsoleKey.F3:
                    return (null, "logs");
                case ConsoleKey.F5:
                    return (null, "reload");
                case ConsoleKey.PageUp:
                    _scrollFromBottom += Math.Max(1, GetLayout().ViewportHeight - 1);
                    break;
                case ConsoleKey.PageDown:
                    _scrollFromBottom = Math.Max(0, _scrollFromBottom - Math.Max(1, GetLayout().ViewportHeight - 1));
                    break;
                case ConsoleKey.LeftArrow:
                    _cursor = PreviousTextElement(text, _cursor);
                    break;
                case ConsoleKey.RightArrow:
                    _cursor = NextTextElement(text, _cursor);
                    break;
                case ConsoleKey.UpArrow:
                    if (!TryMoveCursorVertically(-1, GetLayout().InputWidth) && historyIndex > 0)
                    {
                        if (historyIndex == history.Count)
                        {
                            draft = text;
                        }

                        _input.Clear().Append(history[--historyIndex]);
                        _cursor = _input.Length;
                    }
                    break;
                case ConsoleKey.DownArrow:
                    if (!TryMoveCursorVertically(1, GetLayout().InputWidth) && historyIndex < history.Count)
                    {
                        historyIndex++;
                        var replacement = historyIndex == history.Count ? draft : history[historyIndex];
                        _input.Clear().Append(replacement);
                        _cursor = _input.Length;
                    }
                    break;
                case ConsoleKey.Home:
                    _cursor = text.LastIndexOf('\n', Math.Max(0, _cursor - 1)) + 1;
                    break;
                case ConsoleKey.End:
                    var end = text.IndexOf('\n', _cursor);
                    _cursor = end < 0 ? text.Length : end;
                    break;
                case ConsoleKey.Backspace:
                    if (_cursor > 0)
                    {
                        var previous = PreviousTextElement(text, _cursor);
                        _input.Remove(previous, _cursor - previous);
                        _cursor = previous;
                    }
                    break;
                case ConsoleKey.Delete:
                    if (_cursor < text.Length)
                    {
                        _input.Remove(_cursor, NextTextElement(text, _cursor) - _cursor);
                    }
                    break;
                default:
                    if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.A)
                    {
                        _cursor = text.LastIndexOf('\n', Math.Max(0, _cursor - 1)) + 1;
                    }
                    else if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.E)
                    {
                        var lineEnd = text.IndexOf('\n', _cursor);
                        _cursor = lineEnd < 0 ? text.Length : lineEnd;
                    }
                    else if (!char.IsControl(key.KeyChar))
                    {
                        _input.Insert(_cursor, key.KeyChar);
                        _cursor++;
                    }
                    break;
            }

            Render();
        }
    }

    public void Suspend()
    {
        if (_suspended || _disposed)
        {
            return;
        }

        _suspended = true;
        Console.Write("\u001b[?25h\u001b[?1049l\u001b[2J\u001b[H");
    }

    public void Resume()
    {
        if (!_suspended || _disposed)
        {
            return;
        }

        _suspended = false;
        Console.Write("\u001b[?1049h\u001b[?25l");
        Render();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (!_suspended)
        {
            Console.Write("\u001b[?25h\u001b[?1049l");
        }

        _resizeTimer.Dispose();
        _disposed = true;
    }

    private bool TryMoveCursorVertically(int direction, int width)
    {
        var input = _input.ToString();
        var rows = BuildInputRows(width);
        var currentRow = GetInputCursorRow(rows);
        var targetRow = currentRow + direction;
        if (targetRow < 0 || targetRow >= rows.Count)
        {
            return false;
        }

        var currentColumn = GetDisplayWidth(input[rows[currentRow].Start.._cursor]);
        var targetText = input[rows[targetRow].Start..rows[targetRow].End];
        _cursor = rows[targetRow].Start;
        var column = 0;
        var enumerator = StringInfo.GetTextElementEnumerator(targetText);
        while (enumerator.MoveNext())
        {
            var element = enumerator.GetTextElement();
            var elementWidth = GetTextElementWidth(element);
            if (column + elementWidth > currentColumn)
            {
                break;
            }

            _cursor += element.Length;
            column += elementWidth;
        }

        return true;
    }

    private int GetInputCursorRow(IReadOnlyList<InputRow> rows)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            if (_cursor < rows[index].End ||
                (_cursor == rows[index].End && (index == rows.Count - 1 || rows[index + 1].Start != _cursor)))
            {
                return index;
            }
        }

        return rows.Count - 1;
    }

    private List<InputRow> BuildInputRows(int width)
    {
        var text = _input.ToString();
        var rows = new List<InputRow>();
        var lineStart = 0;
        while (lineStart <= text.Length)
        {
            var lineEnd = text.IndexOf('\n', lineStart);
            if (lineEnd < 0)
            {
                lineEnd = text.Length;
            }

            var rowStart = lineStart;
            var rowEnd = lineStart;
            var rowWidth = 0;
            var enumerator = StringInfo.GetTextElementEnumerator(text[lineStart..lineEnd]);
            while (enumerator.MoveNext())
            {
                var element = enumerator.GetTextElement();
                if (rowWidth > 0 && rowWidth + GetTextElementWidth(element) > width)
                {
                    rows.Add(new InputRow(rowStart, rowEnd));
                    rowStart = lineStart + enumerator.ElementIndex;
                    rowWidth = 0;
                }

                rowEnd = lineStart + enumerator.ElementIndex + element.Length;
                rowWidth += GetTextElementWidth(element);
            }

            rows.Add(new InputRow(rowStart, rowEnd));
            if (lineEnd == text.Length)
            {
                break;
            }

            lineStart = lineEnd + 1;
        }

        return rows;
    }

    private static string SanitizeOutput(string text)
    {
        var sanitized = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (!char.IsControl(character) || character == '\n')
            {
                sanitized.Append(character);
            }
            else if (character == '\t')
            {
                sanitized.Append("    ");
            }
        }

        return sanitized.ToString();
    }

    private static string GetOutputStyle(OutputRow row)
    {
        if (row.Text.StartsWith("──", StringComparison.Ordinal))
        {
            return row.Style switch
            {
                "yellow" => "\u001b[1;38;5;179m",
                "magenta" => "\u001b[1;38;5;139m",
                "green" => "\u001b[1;38;5;108m",
                _ => "\u001b[1;38;5;110m"
            };
        }

        return row.Style switch
        {
            "cyan" => "\u001b[38;5;110m",
            "yellow" => "\u001b[38;5;179m",
            "magenta" => "\u001b[38;5;139m",
            "green" => "\u001b[38;5;108m",
            _ => "\u001b[38;5;252m"
        };
    }

    private void Render()
    {
        if (_suspended || _disposed)
        {
            return;
        }

        lock (_renderLock)
        {
            if (_suspended || _disposed)
            {
                return;
            }

            var layout = GetLayout();
            var wrappedOutput = WrapOutput(layout.ContentWidth);
            var maxScroll = Math.Max(0, wrappedOutput.Count - layout.ViewportHeight);
            _scrollFromBottom = Math.Clamp(_scrollFromBottom, 0, maxScroll);
            var end = wrappedOutput.Count - _scrollFromBottom;
            var start = Math.Max(0, end - layout.ViewportHeight);
            var visible = wrappedOutput.Skip(start).Take(layout.ViewportHeight).ToArray();

            Console.Write("\u001b[?25l");
            WriteRow(0, Truncate(_header, layout.Width), layout.Width, "\u001b[1;38;5;110m");
            WriteRow(1, new string('─', layout.Width), layout.Width, "\u001b[38;5;240m");
            var thumbHeight = maxScroll > 0
                ? Math.Clamp((int)((long)layout.ViewportHeight * layout.ViewportHeight / wrappedOutput.Count), 1, layout.ViewportHeight)
                : 0;
            var thumbTop = maxScroll > 0
                ? (int)((long)(maxScroll - _scrollFromBottom) * (layout.ViewportHeight - thumbHeight) / maxScroll)
                : 0;
            for (var row = 0; row < layout.ViewportHeight; row++)
            {
                var content = row < visible.Length ? visible[row].Text : string.Empty;
                if (layout.Width > 1)
                {
                    content = Truncate(content, layout.ContentWidth) +
                              (maxScroll == 0 ? " " : row >= thumbTop && row < thumbTop + thumbHeight ? "▏" : "│");
                }

                var style = row < visible.Length ? GetOutputStyle(visible[row]) : "\u001b[38;5;252m";
                WriteRow(row + 2, content, layout.Width, style);
            }

            WriteRow(layout.ViewportHeight + 2, new string('─', layout.Width), layout.Width, "\u001b[38;5;240m");
            WriteRow(layout.ViewportHeight + 3,
                "Enter 換行 | F4/Ctrl+Enter 送出 | ↑/↓ 歷史/移動 | PageUp/PageDown 捲動 | F2 設定 | F3 日誌 | F5 重載",
                layout.Width,
                "\u001b[38;5;245m");

            var inputLines = WrapInput(layout.InputWidth);
            var inputRowsToShow = layout.FooterRows - 3;
            var inputStart = Math.Max(0, inputLines.Count - inputRowsToShow);
            var inputCursorRow = GetInputCursorRow(BuildInputRows(layout.InputWidth));
            if (inputCursorRow < inputStart)
            {
                inputStart = inputCursorRow;
            }
            else if (inputCursorRow >= inputStart + inputRowsToShow)
            {
                inputStart = inputCursorRow - inputRowsToShow + 1;
            }

            for (var row = 0; row < inputRowsToShow; row++)
            {
                var lineIndex = inputStart + row;
                var prefix = lineIndex == 0 ? "SuperAgent> " : "             ";
                var line = lineIndex < inputLines.Count ? inputLines[lineIndex] : string.Empty;
                WriteRow(layout.ViewportHeight + 4 + row, prefix + line, layout.Width, "\u001b[38;5;110m");
            }

            WriteRow(layout.ViewportHeight + 4 + inputRowsToShow, string.Empty, layout.Width, string.Empty);
            PositionInputCursor(layout, inputLines, inputStart);
        }
    }

    private Layout GetLayout()
    {
        var (terminalWidth, terminalHeight) = GetTerminalSize();
        var width = Math.Max(20, terminalWidth);
        var height = Math.Max(8, terminalHeight);
        var footerRows = Math.Clamp(WrapInput(Math.Max(1, width - 13)).Count + 3, 5, Math.Max(5, height / 2));
        var viewportHeight = Math.Max(1, height - footerRows - 2);
        return new Layout(width, footerRows, viewportHeight, Math.Max(1, width - 1), Math.Max(1, width - 13));
    }

    private List<OutputRow> WrapOutput(int width)
    {
        var wrapped = new List<OutputRow>();
        for (var index = 0; index < _outputLines.Count; index++)
        {
            wrapped.AddRange(Wrap(_outputLines[index], width)
                .Select(text => new OutputRow(text, _outputStyles[index])));
        }

        return wrapped;
    }

    private List<string> WrapInput(int width)
    {
        return BuildInputRows(width).Select(row => _input.ToString()[row.Start..row.End]).ToList();
    }

    private void PositionInputCursor(Layout layout, IReadOnlyList<string> inputLines, int inputStart)
    {
        var rows = BuildInputRows(layout.InputWidth);
        var cursorRow = GetInputCursorRow(rows);
        var cursorColumn = GetDisplayWidth(_input.ToString()[rows[cursorRow].Start.._cursor]);
        var shownRow = cursorRow - inputStart;
        var screenRow = layout.ViewportHeight + 4 + shownRow;
        var screenColumn = (cursorRow == 0 ? 12 : 13) + cursorColumn;
        if (shownRow >= 0 && screenRow < Console.WindowHeight)
        {
            Console.Write($"\u001b[{screenRow + 1};{Math.Min(layout.Width, screenColumn + 1)}H\u001b[?25h");
        }
        else
        {
            Console.Write("\u001b[?25l");
        }
    }

    private static IReadOnlyList<string> Wrap(string value, int width)
    {
        if (value.Length == 0)
        {
            return [string.Empty];
        }

        var lines = new List<string>();
        var line = new StringBuilder();
        var lineWidth = 0;
        var enumerator = StringInfo.GetTextElementEnumerator(value);
        while (enumerator.MoveNext())
        {
            var element = enumerator.GetTextElement();
            var elementWidth = GetTextElementWidth(element);
            if (lineWidth > 0 && lineWidth + elementWidth > width)
            {
                lines.Add(line.ToString());
                line.Clear();
                lineWidth = 0;
            }

            line.Append(element);
            lineWidth += elementWidth;
        }

        lines.Add(line.ToString());
        return lines;
    }

    private static int GetDisplayWidth(string value)
    {
        var width = 0;
        var enumerator = StringInfo.GetTextElementEnumerator(value);
        while (enumerator.MoveNext())
        {
            width += GetTextElementWidth(enumerator.GetTextElement());
        }

        return width;
    }

    private static int GetTextElementWidth(string element)
    {
        var width = 0;
        foreach (var rune in element.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format)
            {
                continue;
            }

            width = Math.Max(width, IsWideRune(rune.Value) ? 2 : 1);
        }

        return width;
    }

    private static bool IsWideRune(int value)
    {
        return value is >= 0x1100 and <= 0x115F
            or >= 0x2329 and <= 0x232A
            or >= 0x2E80 and <= 0x303E
            or >= 0x3040 and <= 0xA4CF
            or >= 0xAC00 and <= 0xD7A3
            or >= 0xF900 and <= 0xFAFF
            or >= 0xFE10 and <= 0xFE19
            or >= 0xFE30 and <= 0xFE6F
            or >= 0xFF01 and <= 0xFF60
            or >= 0xFFE0 and <= 0xFFE6
            or >= 0x1F300 and <= 0x1FAFF
            or >= 0x20000 and <= 0x3FFFD;
    }

    private static int PreviousTextElement(string text, int cursor)
    {
        var starts = StringInfo.ParseCombiningCharacters(text);
        for (var index = starts.Length - 1; index >= 0; index--)
        {
            if (starts[index] < cursor)
            {
                return starts[index];
            }
        }

        return 0;
    }

    private static int NextTextElement(string text, int cursor)
    {
        var starts = StringInfo.ParseCombiningCharacters(text);
        return starts.FirstOrDefault(start => start > cursor, text.Length);
    }

    private static string RenderMarkup(string markup, bool line)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.No,
            Interactive = InteractionSupport.No
        });
        console.Markup(markup);
        if (line)
        {
            console.WriteLine();
        }

        return writer.ToString();
    }

    private void CheckForResize(object? state)
    {
        if (_suspended || _disposed)
        {
            return;
        }

        try
        {
            var size = GetTerminalSize();
            if (size.Width != _lastWidth || size.Height != _lastHeight)
            {
                _lastWidth = size.Width;
                _lastHeight = size.Height;
                Render();
            }
        }
        catch (IOException)
        {
        }
    }

    private static (int Width, int Height) GetTerminalSize()
    {
        if (OperatingSystem.IsLinux() &&
            ioctl(1, 0x5413, out var windowSize) == 0 &&
            windowSize.Columns > 0 &&
            windowSize.Rows > 0)
        {
            return (windowSize.Columns, windowSize.Rows);
        }

        return (Console.WindowWidth, Console.WindowHeight);
    }

    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static extern int ioctl(int fileDescriptor, uint request, out TerminalWindowSize windowSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct TerminalWindowSize
    {
        public ushort Rows;
        public ushort Columns;
        public ushort PixelWidth;
        public ushort PixelHeight;
    }

    private static void WriteRow(int row, string value, int width, string style)
    {
        Console.Write($"\u001b[{row + 1};1H");
        Console.Write("\u001b[2K");
        if (style.Length > 0)
        {
            Console.Write(style);
        }

        Console.Write(Truncate(value, width));
        if (style.Length > 0)
        {
            Console.Write("\u001b[0m");
        }
    }

    private static string Truncate(string value, int width)
    {
        if (GetDisplayWidth(value) <= width)
        {
            return value;
        }

        var result = new StringBuilder();
        var displayWidth = 0;
        var enumerator = StringInfo.GetTextElementEnumerator(value);
        while (enumerator.MoveNext())
        {
            var element = enumerator.GetTextElement();
            var elementWidth = GetTextElementWidth(element);
            if (displayWidth + elementWidth > width)
            {
                break;
            }

            result.Append(element);
            displayWidth += elementWidth;
        }

        return result.ToString();
    }

    private readonly record struct Layout(
        int Width,
        int FooterRows,
        int ViewportHeight,
        int ContentWidth,
        int InputWidth);

    private readonly record struct InputRow(int Start, int End);

    private readonly record struct OutputRow(string Text, string Style);
}
