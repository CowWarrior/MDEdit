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

    // The grammar declares colour NAMES for its rules to reference and nothing else: every visual
    // property is written on at load time by MarkdownHighlighting.Build, from EditorPreferences.
    // A value re-added here would be silently dead — Build clears each property before applying the
    // user's style — so it would read as configuration while changing nothing, which is exactly the
    // kind of thing that costs an afternoon later. (What each colour actually renders as is covered
    // by MarkdownHighlightingTests, against the compiled definition rather than the file.)
    [Fact]
    public void MarkdownXshd_DeclaresNoVisualAttributes()
    {
        using var stream = typeof(MarkdownSyntax).Assembly
            .GetManifestResourceStream("MDEdit.Resources.Markdown.xshd");
        Assert.NotNull(stream);

        using var reader = new XmlTextReader(stream);
        var xshd = HighlightingLoader.LoadXshd(reader);

        foreach (var color in xshd.Elements.OfType<XshdColor>())
        {
            Assert.NotNull(color.Name);
            Assert.Null(color.Foreground);
            Assert.Null(color.Background);
            Assert.Null(color.FontFamily);
            Assert.Null(color.FontSize);
            Assert.Null(color.FontWeight);
            Assert.Null(color.FontStyle);
            Assert.Null(color.Underline);
            Assert.Null(color.Strikethrough);
        }
    }
}
