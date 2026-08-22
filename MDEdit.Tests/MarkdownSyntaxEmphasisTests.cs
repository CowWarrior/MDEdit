using ICSharpCode.AvalonEdit.Document;
using MDEdit.Editing;

namespace MDEdit.Tests;

public class MarkdownSyntaxEmphasisTests
{
    private static EmphasisSpan Single(string text)
    {
        var doc  = new TextDocument(text);
        var line = doc.GetLineByNumber(1);
        var spans = MarkdownSyntax.FindEmphasisSpans(doc, line);
        return Assert.Single(spans);
    }

    [Theory]
    [InlineData("**bold**", 2)]
    [InlineData("__bold__", 2)]
    [InlineData("*italic*", 1)]
    [InlineData("_italic_", 1)]
    [InlineData("***both***", 3)]
    [InlineData("___both___", 3)]
    [InlineData("~~strike~~", 2)]
    [InlineData("==highlight==", 2)]
    [InlineData("`code`", 1)]
    public void FindEmphasisSpans_WholeLineIsOneRun_MatchesEntireLineWithExpectedMarkerLength(string text, int expectedMarkerLength)
    {
        var span = Single(text);

        Assert.Equal(0, span.Start);
        Assert.Equal(text.Length, span.End);
        Assert.Equal(expectedMarkerLength, span.MarkerLength);
    }

    [Fact]
    public void FindEmphasisSpans_BoldTakesPrecedenceOverItalic()
    {
        // Without bold-before-italic precedence, "*" would greedily match italic first and
        // never recognize the "**...**" run as bold — this locks in the same ordering as
        // Markdown.xshd (BoldItalic, then Bold, then Italic).
        var span = Single("**bold**");
        Assert.Equal(2, span.MarkerLength);
    }

    [Fact]
    public void FindEmphasisSpans_MultipleRunsOnOneLine_FindsBothInOrder()
    {
        var text = "one **two** three *four*";
        var doc  = new TextDocument(text);
        var line = doc.GetLineByNumber(1);

        var spans = MarkdownSyntax.FindEmphasisSpans(doc, line);

        Assert.Equal(2, spans.Count);
        Assert.Equal(new EmphasisSpan(4, 11, 2), spans[0]);   // "**two**"
        Assert.Equal(new EmphasisSpan(18, 24, 1), spans[1]);  // "*four*"
    }

    [Theory]
    [InlineData("no emphasis here")]
    [InlineData("")]
    [InlineData("a single * asterisk")]
    [InlineData("trailing unmatched **bold")]
    [InlineData("trailing unmatched ~~strike")]
    [InlineData("~~~")]   // fenced-code-block delimiter line, not strikethrough
    [InlineData("trailing unmatched ==highlight")]
    [InlineData("===")]   // setext heading underline, not highlight
    [InlineData("=====================")]
    [InlineData("a = b")]
    [InlineData("if (a == b)")]   // a lone "==" has no closing delimiter to pair with
    [InlineData("a single ` backtick")]
    [InlineData("trailing unmatched `code")]
    public void FindEmphasisSpans_NoValidRun_ReturnsEmpty(string text)
    {
        var doc  = new TextDocument(text);
        var line = doc.GetLineByNumber(1);

        var spans = MarkdownSyntax.FindEmphasisSpans(doc, line);

        Assert.Empty(spans);
    }

    // Mixed-delimiter nesting: CommonMark forbids nesting the same delimiter inside itself, so
    // "_**text**_" (italic wrapping bold) and "**_text_**" (bold wrapping italic) are the
    // standard way authors combine the two as two distinct nested runs, rather than "***text***".
    [Theory]
    [InlineData("_**example one**_")]   // italic (underscore) wrapping bold (star)
    [InlineData("*__example two__*")]   // italic (star) wrapping bold (underscore)
    public void FindEmphasisSpans_ItalicWrappingBold_FindsBothOuterAndInnerSpans(string text)
    {
        var doc  = new TextDocument(text);
        var line = doc.GetLineByNumber(1);

        var spans = MarkdownSyntax.FindEmphasisSpans(doc, line);

        Assert.Equal(2, spans.Count);
        var outer = spans[0];
        var inner = spans[1];
        Assert.Equal(new EmphasisSpan(0, text.Length, 1), outer);        // whole line, italic marker
        Assert.Equal(new EmphasisSpan(1, text.Length - 1, 2), inner);    // inside the outer markers, bold marker
    }

    [Theory]
    [InlineData("**_example three_**")]  // bold (star) wrapping italic (underscore)
    [InlineData("__*example four*__")]   // bold (underscore) wrapping italic (star)
    public void FindEmphasisSpans_BoldWrappingItalic_FindsBothOuterAndInnerSpans(string text)
    {
        var doc  = new TextDocument(text);
        var line = doc.GetLineByNumber(1);

        var spans = MarkdownSyntax.FindEmphasisSpans(doc, line);

        Assert.Equal(2, spans.Count);
        var outer = spans[0];
        var inner = spans[1];
        Assert.Equal(new EmphasisSpan(0, text.Length, 2), outer);        // whole line, bold marker
        Assert.Equal(new EmphasisSpan(2, text.Length - 2, 1), inner);    // inside the outer markers, italic marker
    }

    // Strikethrough's '~' delimiter is a third family alongside stars and underscores, so it
    // nests with either of them the same way they nest with each other.
    [Theory]
    [InlineData("~~**text one**~~", 2)]   // strike wrapping bold
    [InlineData("~~_text two_~~", 1)]     // strike wrapping italic
    public void FindEmphasisSpans_StrikeWrappingEmphasis_FindsBothOuterAndInnerSpans(string text, int innerMarkerLength)
    {
        var doc  = new TextDocument(text);
        var line = doc.GetLineByNumber(1);

        var spans = MarkdownSyntax.FindEmphasisSpans(doc, line);

        Assert.Equal(2, spans.Count);
        Assert.Equal(new EmphasisSpan(0, text.Length, 2), spans[0]);                                // whole line, ~~ marker
        Assert.Equal(new EmphasisSpan(2, text.Length - 2, innerMarkerLength), spans[1]);            // nested run inside
    }

    // Highlight's '=' delimiter is a fourth family, nesting on the same terms as the others.
    [Theory]
    [InlineData("==**text one**==", 2)]   // highlight wrapping bold
    [InlineData("==_text two_==", 1)]     // highlight wrapping italic
    public void FindEmphasisSpans_HighlightWrappingEmphasis_FindsBothOuterAndInnerSpans(string text, int innerMarkerLength)
    {
        var doc  = new TextDocument(text);
        var line = doc.GetLineByNumber(1);

        var spans = MarkdownSyntax.FindEmphasisSpans(doc, line);

        Assert.Equal(2, spans.Count);
        Assert.Equal(new EmphasisSpan(0, text.Length, 2), spans[0]);                     // whole line, == marker
        Assert.Equal(new EmphasisSpan(2, text.Length - 2, innerMarkerLength), spans[1]); // nested run inside
    }

    [Fact]
    public void FindEmphasisSpans_EmphasisWrappingHighlight_FindsBothOuterAndInnerSpans()
    {
        var text = "**==text==**";
        var doc  = new TextDocument(text);
        var line = doc.GetLineByNumber(1);

        var spans = MarkdownSyntax.FindEmphasisSpans(doc, line);

        Assert.Equal(2, spans.Count);
        Assert.Equal(new EmphasisSpan(0, text.Length, 2), spans[0]);   // whole line, bold marker
        Assert.Equal(new EmphasisSpan(2, 10, 2), spans[1]);            // "==text==" inside
    }

    [Fact]
    public void FindEmphasisSpans_HighlightInsideInlineCode_IsLiteral()
    {
        var doc  = new TextDocument("`==not highlighted==`");
        var line = doc.GetLineByNumber(1);

        var span = Assert.Single(MarkdownSyntax.FindEmphasisSpans(doc, line));

        Assert.Equal(1, span.MarkerLength);   // the code span only — its content is literal
    }

    [Fact]
    public void FindEmphasisSpans_EmphasisWrappingStrike_FindsBothOuterAndInnerSpans()
    {
        var text = "**~~text~~**";
        var doc  = new TextDocument(text);
        var line = doc.GetLineByNumber(1);

        var spans = MarkdownSyntax.FindEmphasisSpans(doc, line);

        Assert.Equal(2, spans.Count);
        Assert.Equal(new EmphasisSpan(0, text.Length, 2), spans[0]);              // whole line, bold marker
        Assert.Equal(new EmphasisSpan(2, text.Length - 2, 2), spans[1]);          // nested strike inside
    }

    // A code span's content is literal (CommonMark gives code spans precedence over emphasis),
    // so emphasis markers inside backticks must NOT produce nested spans — otherwise live
    // preview would hide the "**" of "`**not bold**`" even though it renders as plain text.
    [Theory]
    [InlineData("`**not bold**`")]
    [InlineData("`_not italic_`")]
    [InlineData("`~~not struck~~`")]
    public void FindEmphasisSpans_EmphasisInsideCode_IsLiteralSingleCodeSpan(string text)
    {
        var span = Single(text);

        Assert.Equal(new EmphasisSpan(0, text.Length, 1), span);
    }

    [Fact]
    public void FindEmphasisSpans_CodeInsideBold_FindsBothOuterAndInnerSpans()
    {
        var text = "**bold `code` here**";
        var doc  = new TextDocument(text);
        var line = doc.GetLineByNumber(1);

        var spans = MarkdownSyntax.FindEmphasisSpans(doc, line);

        Assert.Equal(2, spans.Count);
        Assert.Equal(new EmphasisSpan(0, 20, 2), spans[0]);   // whole line, bold marker
        Assert.Equal(new EmphasisSpan(7, 13, 1), spans[1]);   // "`code`" nested inside
    }

    [Fact]
    public void FindEmphasisSpans_StrikeAndBoldOnOneLine_FindsBothInOrder()
    {
        var text = "one ~~two~~ three **four**";
        var doc  = new TextDocument(text);
        var line = doc.GetLineByNumber(1);

        var spans = MarkdownSyntax.FindEmphasisSpans(doc, line);

        Assert.Equal(2, spans.Count);
        Assert.Equal(new EmphasisSpan(4, 11, 2), spans[0]);   // "~~two~~"
        Assert.Equal(new EmphasisSpan(18, 26, 2), spans[1]);  // "**four**"
    }

    // Markdown.xshd's Link rule is a flat Rule (no nested RuleSet) that swallows its "[...]"
    // portion as literal content, so "**bold**" inside link text is never separately
    // Bold-colored — otherwise live preview would hide the "**" in "[**bold**](url)" even though
    // it doesn't render bold. See MarkdownSyntaxLinkTests for the reverse case.
    [Fact]
    public void FindEmphasisSpans_EmphasisInsideLinkText_IsNotFoundSeparately()
    {
        var doc  = new TextDocument("[**bold** link](url)");
        var line = doc.GetLineByNumber(1);

        var spans = MarkdownSyntax.FindEmphasisSpans(doc, line);

        Assert.Empty(spans);
    }

    private static IReadOnlyList<EmphasisSpan> Spans(string text)
    {
        var doc = new TextDocument(text);
        return MarkdownSyntax.FindEmphasisSpans(doc, doc.GetLineByNumber(1));
    }

    // ── Backslash escaping (CommonMark) ──────────────────────────────────────
    //
    // One of the two gaps that silently misrendered ORDINARY TEXT rather than merely failing to
    // support extra syntax: before this, "\*text\*" rendered as italic and the asterisks the user
    // explicitly escaped disappeared from view.

    [Theory]
    [InlineData(@"\*not emphasis\*")]
    [InlineData(@"\_not emphasis\_")]
    [InlineData(@"\`not code\`")]
    [InlineData(@"\~\~not strike\~\~")]
    [InlineData(@"\==not highlight\==")]
    public void FindEmphasisSpans_EscapedDelimiters_OpenNothing(string text)
    {
        Assert.Empty(Spans(text));
    }

    [Fact]
    public void FindEmphasisSpans_EscapedCloser_DoesNotEndTheRunEarly()
    {
        // The half the scanner's opener-skip cannot cover: a closing delimiter is matched by the
        // pattern rather than visited by the scan, so this rides on the content class instead.
        var span = Assert.Single(Spans(@"*a \* b*"));

        Assert.Equal(0, span.Start);
        Assert.Equal(8, span.End);
    }

    [Fact]
    public void FindEmphasisSpans_EscapedBackslashBeforeDelimiter_StillOpens()
    {
        // "\\" is a literal backslash, so the "*" after it is a real delimiter — the escape is
        // spent on the backslash itself. Markdown.xshd's lookbehind gets this case wrong (it sees
        // only the adjacent backslash); the divergence is documented there and is colour-only.
        var span = Assert.Single(Spans(@"\\*bold*"));

        Assert.Equal(2, span.Start);
        Assert.Equal(8, span.End);
    }

    [Fact]
    public void FindEmphasisSpans_BackslashBeforeNonPunctuation_IsNotAnEscape()
    {
        // CommonMark escapes ASCII punctuation only. "\w" is two literal characters, so it must not
        // swallow the following character and shift everything after it.
        var span = Assert.Single(Spans(@"\word *bold*"));

        Assert.Equal(6, span.Start);
    }

    [Fact]
    public void FindEmphasisSpans_BackslashInsideCodeSpan_StaysLiteral()
    {
        // Code spans outrank escapes in CommonMark, so the scan must reach the closing backtick
        // rather than treating "\`" inside as an escaped delimiter.
        var span = Assert.Single(Spans(@"`a \* b`"));

        Assert.Equal(0, span.Start);
        Assert.Equal(8, span.End);
        Assert.Equal(1, span.MarkerLength);
    }

    // ── Intraword underscore (CommonMark flanking) ───────────────────────────
    //
    // The other silent misrender: "snake_case_name" italicised its middle section, so ordinary
    // identifiers in prose came out mangled.

    [Theory]
    [InlineData("snake_case_name")]
    [InlineData("a_b_c")]
    [InlineData("snake__case__name")]
    [InlineData("a___b___c")]
    [InlineData("_italic_word")]      // closer is followed by a word character
    [InlineData("word_italic_")]      // opener is preceded by one
    public void FindEmphasisSpans_IntrawordUnderscore_IsNotEmphasis(string text)
    {
        Assert.Empty(Spans(text));
    }

    [Theory]
    [InlineData("_italic_", 1)]
    [InlineData("__bold__", 2)]
    [InlineData("___both___", 3)]
    [InlineData("(_italic_)", 1)]     // punctuation flanking still opens and closes
    [InlineData("a _italic_ b", 1)]
    public void FindEmphasisSpans_UnderscoreAtWordBoundaries_IsStillEmphasis(string text, int markerLength)
    {
        Assert.Equal(markerLength, Assert.Single(Spans(text)).MarkerLength);
    }

    [Fact]
    public void FindEmphasisSpans_IntrawordAsterisk_IsStillEmphasis()
    {
        // The flanking restriction is '_'-only in CommonMark, and deliberately so here: "foo*bar*"
        // IS emphasis. Guarding '*' the same way would break a documented behaviour.
        var span = Assert.Single(Spans("foo*bar*baz"));

        Assert.Equal(3, span.Start);
        Assert.Equal(8, span.End);
    }

    [Fact]
    public void FindEmphasisSpans_UnderscoreNestedInsideAsterisks_StillRecurses()
    {
        // The recursion scans a SUBSTRING, so the '_' lookbehind has nothing to inspect at its
        // position 0. That is only safe because inner content always begins right after a delimiter
        // character — if this regressed, every mixed-delimiter nesting would silently stop nesting.
        var spans = Spans("**_italic_**");

        Assert.Equal(2, spans.Count);
        Assert.Equal(2, spans[0].MarkerLength);
        Assert.Equal(1, spans[1].MarkerLength);
    }
}
