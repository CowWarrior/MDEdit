using System.Reflection;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using MDEdit.Editing;

namespace MDEdit.Tests;

// Loads the real embedded Markdown.xshd through AvalonEdit's actual loader — the same path
// MainWindow.LoadDefinition uses — so a malformed grammar (bad XML, an unresolvable Span/Rule
// reference, etc.) fails a `dotnet test` run instead of only surfacing when someone opens the
// app and a .md file.
public class MarkdownXshdTests
{
    [Fact]
    public void MarkdownXshd_LoadsWithoutError()
    {
        using var stream = typeof(MarkdownSyntax).Assembly
            .GetManifestResourceStream("MDEdit.Resources.Markdown.xshd");
        Assert.NotNull(stream);

        using var reader = new XmlTextReader(stream);
        var xshd = HighlightingLoader.LoadXshd(reader);
        var definition = HighlightingLoader.Load(xshd, HighlightingManager.Instance);

        // Most rules are Spans (Bold/Italic/code blocks/comments) rather than flat Rules now,
        // so check both collections rather than assuming rule counts.
        Assert.True(definition.MainRuleSet.Rules.Count + definition.MainRuleSet.Spans.Count > 0);
    }

    // The fontFamily on these colors is what keeps code fixed-width when WYSIWYG swaps the
    // editor's base font to the document font — nothing else would catch the attribute being
    // stripped, since the loss is only visible by eye and only in WYSIWYG mode.
    [Theory]
    [InlineData("InlineCode")]
    [InlineData("CodeBlock")]
    public void MarkdownXshd_CodeColors_PinAFontFamily(string colorName)
    {
        using var stream = typeof(MarkdownSyntax).Assembly
            .GetManifestResourceStream("MDEdit.Resources.Markdown.xshd");
        Assert.NotNull(stream);

        using var reader = new XmlTextReader(stream);
        var definition = HighlightingLoader.Load(HighlightingLoader.LoadXshd(reader), HighlightingManager.Instance);

        var color = definition.GetNamedColor(colorName);
        Assert.NotNull(color);
        Assert.NotNull(color.FontFamily);
    }
}
