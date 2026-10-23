using System.Globalization;
using System.Text;

namespace SdqTerminal;

/// <summary>Reflows only known soft-wrapped Persian paragraphs for presentation.
/// The VT buffer and application-owned hard line breaks remain authoritative.</summary>
internal static class TerminalWordWrap
{
    public static TerminalSnapshot Reflow(TerminalSnapshot source)
    {
        if (source.Modes.AlternateScreen || source.Modes.MouseTrackingMode != 0 ||
            !source.Lines.Any(line => line.WrappedFromPrevious)) return source;
        var lines = new List<TerminalLine>();
        var cursorRow = source.CursorRow;
        var cursorColumn = source.CursorColumn;
        var scrollback = source.ScrollbackCount;
        var changed = false;
        for (var first = 0; first < source.Lines.Count;)
        {
            var end = first + 1;
            while (end < source.Lines.Count && source.Lines[end].WrappedFromPrevious) end++;
            var outputStart = lines.Count;
            if (end - first < 2 || !source.Lines.Skip(first).Take(end - first).Any(line => line.ContainsRightToLeft))
            {
                for (var row = first; row < end; row++) lines.Add(source.Lines[row]);
                if (source.CursorRow >= first && source.CursorRow < end) cursorRow = outputStart + source.CursorRow - first;
                if (source.ScrollbackCount >= first && source.ScrollbackCount < end) scrollback = outputStart + source.ScrollbackCount - first;
                first = end;
                continue;
            }
            var columns = source.Columns > 0 ? source.Columns : source.Lines[first].CellLength;
            var text = new StringBuilder();
            var styles = new List<TerminalStyle>();
            var sourceOffsets = new List<int>();
            for (var row = first; row < end; row++)
            {
                sourceOffsets.Add(text.Length);
                foreach (var run in source.Lines[row].Runs)
                {
                    text.Append(run.Text);
                    for (var i = 0; i < run.Text.Length; i++) styles.Add(run.Style);
                }
            }
            var value = text.ToString();
            var elements = new List<(int Start, int Length, int Width, bool Space)>();
            var iterator = StringInfo.GetTextElementEnumerator(value);
            while (iterator.MoveNext())
            {
                var element = iterator.GetTextElement();
                elements.Add((iterator.ElementIndex, element.Length,
                    element.EnumerateRunes().Max(TerminalBuffer.GetCellWidth),
                    element == " " || element == "\t"));
            }
            var breaks = new List<(int Offset, int End, int Width)>();
            for (var start = 0; start < elements.Count;)
            {
                var next = start; var width = 0; var separator = -1; var separatorWidth = 0;
                while (next < elements.Count && (width + elements[next].Width <= columns || next == start))
                {
                    width += elements[next].Width;
                    if (elements[next].Space) { separator = next + 1; separatorWidth = width; }
                    next++;
                }
                if (next < elements.Count && !elements[next].Space && separator > start)
                { next = separator; width = separatorWidth; }
                var from = elements[start].Start;
                var to = next < elements.Count ? elements[next].Start : value.Length;
                breaks.Add((from, to, width));
                var runs = new List<TerminalRun>();
                for (var offset = from; offset < to;)
                {
                    var stop = offset + 1;
                    while (stop < to && styles[stop] == styles[offset]) stop++;
                    runs.Add(new TerminalRun(value[offset..stop], styles[offset]));
                    offset = stop;
                }
                lines.Add(new TerminalLine(runs, width) { WrappedFromPrevious = start > 0 || source.Lines[first].WrappedFromPrevious });
                start = next;
            }
            if (breaks.Count == 0) { lines.Add(source.Lines[first]); first = end; continue; }
            int FindRow(int offset) => Math.Max(0, breaks.FindLastIndex(item => item.Offset <= offset));
            if (source.CursorRow >= first && source.CursorRow < end)
            {
                var offset = sourceOffsets[source.CursorRow - first];
                var consumed = 0;
                foreach (var element in elements.Where(item => item.Start >= offset))
                {
                    if (consumed + element.Width > source.CursorColumn) break;
                    consumed += element.Width;
                    offset = element.Start + element.Length;
                    if (consumed == source.CursorColumn) break;
                }
                // At column zero the cursor precedes the first text element.
                if (source.CursorColumn == 0) offset = sourceOffsets[source.CursorRow - first];
                var visualRow = FindRow(offset);
                cursorRow = outputStart + visualRow;
                cursorColumn = elements.Where(item => item.Start >= breaks[visualRow].Offset && item.Start < offset).Sum(item => item.Width);
            }
            if (source.ScrollbackCount >= first && source.ScrollbackCount < end)
                scrollback = outputStart + FindRow(sourceOffsets[source.ScrollbackCount - first]);
            changed = true;
            first = end;
        }
        return changed ? source with { Lines = lines, CursorRow = cursorRow, CursorColumn = cursorColumn, ScrollbackCount = scrollback } : source;
    }
}
