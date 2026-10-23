using System.Globalization;
using System.Text;

namespace SdqTerminal;

internal readonly record struct DirectionalSpan(
    int Start,
    int Length,
    bool IsRightToLeft);

internal static class SmartRtl
{
    /// <summary>
    /// Restores logical character order from text an application already
    /// reordered for display (Claude Code emits visual-order RTL text).
    /// Applying the same directional spans a second time would mirror the text.
    /// </summary>
    internal static TerminalLine ToLogicalOrder(TerminalLine line)
    {
        if (!line.ContainsRightToLeft)
            return line;

        var text = string.Concat(line.Runs.Select(run => run.Text));
        var elements = new List<(int Start, int Length)>();
        var iterator = StringInfo.GetTextElementEnumerator(text);
        while (iterator.MoveNext())
            elements.Add((iterator.ElementIndex, iterator.GetTextElement().Length));

        // Prefix marks (Claude's bullets, prompt glyphs and indentation) and
        // erased padding are separate layout pieces, not part of the paragraph.
        var first = 0;
        while (first < elements.Count && IsDecoration(text, elements, first))
            first++;
        var last = elements.Count;
        while (last > first && char.IsWhiteSpace(text[elements[last - 1].Start]))
            last--;
        if (first == last)
            return line;

        var paragraphStart = elements[first].Start;
        var paragraphEnd = elements[last - 1].Start + elements[last - 1].Length;
        var paragraph = text[paragraphStart..paragraphEnd];
        if (!ContainsRightToLeft(paragraph))
            return line;

        var baseRightToLeft = FirstStrongIsRightToLeft(paragraph);
        var spans = GetDirectionalSpans(paragraph, baseRightToLeft);
        var order = new List<int>(elements.Count);
        for (var index = 0; index < first; index++)
            order.Add(index);
        foreach (var span in baseRightToLeft ? spans.Reverse() : spans)
        {
            var members = new List<int>();
            for (var index = first; index < last; index++)
            {
                var offset = elements[index].Start - paragraphStart;
                if (offset >= span.Start && offset < span.Start + span.Length)
                    members.Add(index);
            }
            if (span.IsRightToLeft)
                members.Reverse();
            order.AddRange(members);
        }
        for (var index = last; index < elements.Count; index++)
            order.Add(index);

        var styles = new TerminalStyle[text.Length];
        var styleOffset = 0;
        foreach (var run in line.Runs)
        {
            for (var index = 0; index < run.Text.Length; index++)
                styles[styleOffset + index] = run.Style;
            styleOffset += run.Text.Length;
        }

        var runs = new List<TerminalRun>();
        StringBuilder? current = null;
        var currentStyle = default(TerminalStyle);
        foreach (var index in order)
        {
            var (start, length) = elements[index];
            var piece = text.Substring(start, length);
            var style = styles[start];
            if (current is not null && style == currentStyle)
            {
                current.Append(piece);
                continue;
            }
            if (current is not null)
                runs.Add(new TerminalRun(current.ToString(), currentStyle));
            current = new StringBuilder(piece);
            currentStyle = style;
        }
        if (current is not null)
            runs.Add(new TerminalRun(current.ToString(), currentStyle));

        return new TerminalLine(runs, line.CellLength)
        {
            WrappedFromPrevious = line.WrappedFromPrevious,
            BaseRightToLeft = baseRightToLeft,
            ParagraphStart = paragraphStart,
            ParagraphEnd = paragraphEnd
        };
    }

    private static bool IsDecoration(
        string text,
        List<(int Start, int Length)> elements,
        int index)
    {
        var (start, length) = elements[index];
        if (length != 1)
            return false;
        var character = text[start];
        if (char.IsWhiteSpace(character))
            return true;
        var category = char.GetUnicodeCategory(character);
        if (category is UnicodeCategory.OtherSymbol or
            UnicodeCategory.ModifierSymbol or
            UnicodeCategory.MathSymbol)
            return true;
        // Prompt markers such as ❯ are bracketing punctuation. A marker stands
        // on its own, while punctuation glued to a word belongs to the text.
        return (category is UnicodeCategory.OpenPunctuation or
            UnicodeCategory.ClosePunctuation or
            UnicodeCategory.InitialQuotePunctuation or
            UnicodeCategory.FinalQuotePunctuation) &&
            index + 1 < elements.Count && char.IsWhiteSpace(text[elements[index + 1].Start]);
    }

    private static bool FirstStrongIsRightToLeft(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            var direction = GetStrongDirection(rune);
            if (direction >= 0)
                return direction == 1;
        }
        return false;
    }

    private static bool ContainsRightToLeft(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            if (IsStrongRtlLetter(rune))
                return true;
        }
        return false;
    }

    public static bool IsRightToLeft(TerminalLine line)
    {
        return line.ContainsRightToLeft;
    }

    public static bool ShouldRightAlign(
        TerminalLine line,
        bool smartRtlEnabled,
        bool preserveTerminalGrid)
    {
        return smartRtlEnabled &&
            !preserveTerminalGrid &&
            IsRightToLeft(line);
    }

    internal static bool ContainsRightToLeft(
        IReadOnlyList<TerminalRun> runs)
    {
        foreach (var run in runs)
        {
            foreach (var rune in run.Text.EnumerateRunes())
            {
                if (IsStrongRtlLetter(rune))
                    return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<DirectionalSpan> GetDirectionalSpans(
        string text,
        bool baseRightToLeft)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        var baseDirection = baseRightToLeft ? 1 : 0;
        var directions = new int[text.Length];
        var whitespace = new bool[text.Length];
        Array.Fill(directions, -1);

        var index = 0;

        foreach (var rune in text.EnumerateRunes())
        {
            var direction = GetStrongDirection(rune);
            var isWhitespace = Rune.IsWhiteSpace(rune);

            for (var offset = 0; offset < rune.Utf16SequenceLength; offset++)
            {
                directions[index + offset] = direction;
                whitespace[index + offset] = isWhitespace;
            }

            index += rune.Utf16SequenceLength;
        }

        ResolveBracketDirections(text, directions);
        ResolveNeutralDirections(directions, whitespace, baseDirection);

        var spans = new List<DirectionalSpan>();
        var spanStart = 0;
        var spanDirection = directions[0];

        for (index = 1; index < text.Length; index++)
        {
            if (directions[index] == spanDirection)
                continue;

            spans.Add(new DirectionalSpan(
                spanStart,
                index - spanStart,
                spanDirection == 1));
            spanStart = index;
            spanDirection = directions[index];
        }

        spans.Add(new DirectionalSpan(
            spanStart,
            text.Length - spanStart,
            spanDirection == 1));
        return spans;
    }

    private static int GetStrongDirection(Rune rune)
    {
        if (IsStrongRtlLetter(rune))
            return 1;

        var category = Rune.GetUnicodeCategory(rune);

        if (category is UnicodeCategory.UppercaseLetter or
            UnicodeCategory.LowercaseLetter or
            UnicodeCategory.TitlecaseLetter or
            UnicodeCategory.ModifierLetter or
            UnicodeCategory.OtherLetter or
            UnicodeCategory.DecimalDigitNumber or
            UnicodeCategory.LetterNumber or
            UnicodeCategory.OtherNumber)
        {
            return 0;
        }

        return -1;
    }

    private static void ResolveBracketDirections(
        string text,
        int[] directions)
    {
        var brackets = new Stack<(char Character, int Index)>();

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];

            if (character is '(' or '[' or '{' or '<')
            {
                brackets.Push((character, index));
                continue;
            }

            if (character is not (')' or ']' or '}' or '>'))
                continue;

            var expectedOpening = character switch
            {
                ')' => '(',
                ']' => '[',
                '}' => '{',
                _ => '<'
            };
            var matchingBracket = brackets
                .FirstOrDefault(item => item.Character == expectedOpening);

            if (matchingBracket.Character == '\0')
                continue;

            while (brackets.Count > 0)
            {
                var popped = brackets.Pop();

                if (popped.Index == matchingBracket.Index)
                    break;
            }

            var contentDirection = GetEnclosedDirection(
                directions,
                matchingBracket.Index + 1,
                index);

            if (contentDirection < 0)
                continue;

            directions[matchingBracket.Index] = contentDirection;
            directions[index] = contentDirection;
        }
    }

    private static int GetEnclosedDirection(
        int[] directions,
        int start,
        int end)
    {
        var foundDirection = -1;

        for (var index = start; index < end; index++)
        {
            if (directions[index] < 0)
                continue;

            if (foundDirection >= 0 && foundDirection != directions[index])
                return -1;

            foundDirection = directions[index];
        }

        return foundDirection;
    }

    private static void ResolveNeutralDirections(
        int[] directions,
        bool[] whitespace,
        int baseDirection)
    {
        var index = 0;

        while (index < directions.Length)
        {
            if (directions[index] >= 0)
            {
                index++;
                continue;
            }

            var start = index;
            var containsWhitespace = false;

            while (index < directions.Length && directions[index] < 0)
            {
                containsWhitespace |= whitespace[index];
                index++;
            }

            var previousDirection = start > 0
                ? directions[start - 1]
                : baseDirection;
            var nextDirection = index < directions.Length
                ? directions[index]
                : baseDirection;
            var resolvedDirection = previousDirection == nextDirection
                ? previousDirection
                : !containsWhitespace &&
                    (previousDirection == 0 || nextDirection == 0)
                    ? 0
                    : baseDirection;

            for (var neutralIndex = start;
                 neutralIndex < index;
                 neutralIndex++)
            {
                directions[neutralIndex] = resolvedDirection;
            }
        }
    }

    private static bool IsStrongRtlLetter(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);

        if (category is not (
                UnicodeCategory.UppercaseLetter or
                UnicodeCategory.LowercaseLetter or
                UnicodeCategory.TitlecaseLetter or
                UnicodeCategory.ModifierLetter or
                UnicodeCategory.OtherLetter))
        {
            return false;
        }

        var value = rune.Value;
        return value is >= 0x0590 and <= 0x08ff
            or >= 0xfb1d and <= 0xfdff
            or >= 0xfe70 and <= 0xfeff
            or >= 0x10800 and <= 0x10fff
            or >= 0x1e800 and <= 0x1eeff;
    }
}
