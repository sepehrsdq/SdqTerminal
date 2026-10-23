using System.IO;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Media;
using SdqTerminal;

internal static partial class Program
{
    private static void CheckPersianWordWrapAndHtml()
    {
        const string phrase = "از محدودهٔ تغییرات نگه می‌دارم. پروژهٔ قدیمی و کلاینت اسکرچ در farsiscratch باقی می‌مانند.";
        var view = new TerminalView { Width = 800, Height = 420, FontFamily = new FontFamily("Consolas"), FontSize = 18, Background = Brushes.Black };
        var buffer = new TerminalBuffer(20, 16);
        var source = buffer.Process("\x1b[?25l" + phrase);
        Render(view, source, "persian-word-wrap.png");
        var presented = (TerminalSnapshot)typeof(TerminalView).GetField("_snapshot", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!;
        string Text(TerminalLine line) => string.Concat(line.Runs.Select(run => run.Text));
        for (var row = 1; row < presented.Lines.Count; row++)
        {
            var previous = Text(presented.Lines[row - 1]);
            var current = Text(presented.Lines[row]);
            if (previous.Length > 0 && current.Length > 0 && !char.IsWhiteSpace(previous[^1]) && !char.IsWhiteSpace(current[0]))
                throw new Exception("A Persian word was split between presentation rows");
        }
        view.SelectAll();
        if (view.GetSelectedText().TrimEnd() != phrase) throw new Exception("Reflow changed copied text or inserted a newline inside a word");
        view.ClearSelection();
        var html = view.CreateHtmlPreview();
        var plain = WebUtility.HtmlDecode(Regex.Replace(html[(html.IndexOf("<main>") + 6)..html.IndexOf("</main>")], "<[^>]+>", ""));
        if (plain.TrimEnd() != phrase) throw new Exception("HTML lost text at a soft-wrap boundary");
        if (Regex.Matches(html, "class=\"line\"").Count != 1) throw new Exception("Soft wraps became hard paragraph breaks in HTML");
        if (!html.Contains("word-break:normal") || !html.Contains("overflow-wrap:normal")) throw new Exception("HTML allows words to be broken");
        Directory.CreateDirectory("artifacts/render-checks");
        File.WriteAllText("artifacts/render-checks/persian-preview.html", html);

        var reflow = typeof(TerminalView).Assembly.GetType("SdqTerminal.TerminalWordWrap")!.GetMethod("Reflow")!;
        int Width(string value) => System.Globalization.StringInfo.ParseCombiningCharacters(value).Length;
        string Prefix(TerminalSnapshot state)
        {
            var prefix = string.Concat(state.Lines.Take(state.CursorRow).Select(Text));
            var value = Text(state.Lines[state.CursorRow]);
            var offsets = System.Globalization.StringInfo.ParseCombiningCharacters(value);
            var end = state.CursorColumn < offsets.Length ? offsets[state.CursorColumn] : value.Length;
            return prefix + value[..end];
        }
        for (var row = 0; row < source.Lines.Count; row++)
        {
            foreach (var column in new[] { 0, 1, Math.Max(0, Width(Text(source.Lines[row])) - 1) })
            {
                var positioned = source with { CursorRow = row, CursorColumn = column };
                var mapped = (TerminalSnapshot)reflow.Invoke(null, new object[] { positioned })!;
                if (Prefix(mapped) != Prefix(positioned)) throw new Exception("Word wrap moved the logical cursor position");
            }
        }
        var historyBuffer = new TerminalBuffer(20, 5);
        var history = historyBuffer.Process(phrase + "\r\n" + phrase + "\r\n" + phrase);
        var historyWrapped = (TerminalSnapshot)reflow.Invoke(null, new object[] { history })!;
        if (string.Concat(historyWrapped.Lines.Select(Text)) != string.Concat(history.Lines.Select(Text)))
            throw new Exception("Scrollback reflow lost text or spaces");
        var links = TerminalHtmlExporter.Create(new TerminalBuffer(100, 5).Process("https://example.com/?a=1&b=2").Lines, "Consolas", 18, 25);
        if (!links.Contains("href=\"https://example.com/?a=1&amp;b=2\"") || !links.Contains("color:#6495ED"))
            throw new Exception("Preview lost a link or failed to escape its address");
        var hard = new TerminalBuffer(40, 6).Process("متن اول\r\nمتن دوم");
        var hardHtml = TerminalHtmlExporter.Create(hard.Lines, "Consolas", 18, 25);
        if (Regex.Matches(hardHtml, "class=\"line\"").Count != 2) throw new Exception("Explicit line breaks were merged");
        var styled = new TerminalBuffer(100, 6).Process("\x1b[1;3;4;9;38;2;10;20;30;48;2;40;50;60mمتن رنگی\x1b[0m <script>alert('x')</script> & \"quote\"");
        var styledHtml = TerminalHtmlExporter.Create(styled.Lines, "Consolas'\"><script>", 18, 25);
        if (styledHtml.Contains("<script>") || !styledHtml.Contains("&lt;script&gt;") ||
            !styledHtml.Contains("#0A141E") || !styledHtml.Contains("#28323C") ||
            !styledHtml.Contains("font-weight:bold") || !styledHtml.Contains("font-style:italic") ||
            !styledHtml.Contains("underline line-through")) throw new Exception("HTML escaping or ANSI styling failed");
        var alternate = source with { Modes = source.Modes with { AlternateScreen = true } };
        Render(view, alternate, "persian-wrap-fixed-grid.png");
        var untouched = (TerminalSnapshot)typeof(TerminalView).GetField("_snapshot", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!;
        if (!ReferenceEquals(untouched, alternate)) throw new Exception("Word reflow moved a full-screen application's grid");
        Console.WriteLine("PASS Persian whole-word wrapping, lossless copy, explicit line breaks, styled safe HTML and TUI grid preservation");
    }

    private static void CheckApplicationOwnedBidi()
    {
        const string mirrored = ".دینک زاب هژورپ هشوپ رد ار README.md لیاف";
        const string expected = "فایل README.md را در پوشه پروژه باز کنید.";
        TerminalSnapshot Feed(string title)
        {
            var buffer = new TerminalBuffer(80, 8);
            return buffer.Process($"\x1b]0;{title}\x1b\\\x1b[?1049h\x1b[H" +
                $"❯ مالس\r\n● {mirrored}\r\nOpen README.md ناوخب ار مود لیاف دعب و.");
        }
        string Text(TerminalLine line) => string.Concat(line.Runs.Select(run => run.Text));
        var view = new TerminalView { Width = 800, Height = 300, FontFamily = new FontFamily("Consolas"), FontSize = 18, Background = Brushes.Black };
        var owned = Feed("✳ Claude Code");
        Render(view, owned, "application-bidi.png");
        view.SelectAll();
        var copied = view.GetSelectedText().Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();
        view.ClearSelection();
        if (copied[0] != "❯ سلام") throw new Exception("App-reordered prompt did not copy back as Persian text: " + copied[0]);
        if (copied[1] != "● " + expected) throw new Exception("App-reordered answer did not copy back as logical text: " + copied[1]);
        if (copied[2] != "Open README.md و بعد فایل دوم را بخوان.")
            throw new Exception("App-reordered mixed line did not copy back as logical text: " + copied[2]);
        var exported = string.Concat(view.LogicalSnapshot(owned).Lines.Select(Text));
        if (!exported.Contains(expected)) throw new Exception("Export kept application visual order instead of logical text");
        // The prompt mark keeps the column the application gave it; only the
        // mirrored paragraph takes part in bidi layout.
        var layouts = (System.Collections.IDictionary)typeof(TerminalView)
            .GetField("_layouts", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!;
        var layout = layouts[0]!;
        var glyphs = ((System.Collections.IEnumerable)layout.GetType().GetProperty("Glyphs")!.GetValue(layout)!).Cast<object>().ToArray();
        double X(object glyph) => (double)glyph.GetType().GetProperty("X")!.GetValue(glyph)!;
        string T(object glyph) => (string)glyph.GetType().GetProperty("Text")!.GetValue(glyph)!;
        var leftmost = glyphs.OrderBy(X).First();
        if (T(leftmost) != "❯" || X(leftmost) != 0)
            throw new Exception("The prompt mark left its column: " + T(leftmost) + " at " + X(leftmost));
        // Ordinary full-screen applications keep writing logical order, so the
        // same bytes under another title must pass through untouched.
        var plain = Feed("my app");
        var other = new TerminalView { Width = 800, Height = 300, FontFamily = new FontFamily("Consolas"), FontSize = 18, Background = Brushes.Black };
        Render(other, plain, "application-bidi-plain.png");
        var presented = (TerminalSnapshot)typeof(TerminalView).GetField("_snapshot", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(other)!;
        if (Text(presented.Lines[1]) != "● " + mirrored)
            throw new Exception("A full-screen application's own text order was rewritten: " + Text(presented.Lines[1]));
        Console.WriteLine("PASS Application-owned visual order is restored for copy, export and drawing");
    }
}
