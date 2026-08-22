using MDEdit;
using MDEdit.Services;

namespace MDEdit.Tests;

// MarkdownLineColorizer is WPF-bound almost everywhere (ColorizeLine needs visual lines), so only
// its style-resolution contract is covered here — the part TableRowElementGenerator depends on to
// know when its own derived cache is stale.
public class MarkdownLineColorizerTests
{
    [Fact]
    public void StyleGeneration_ChangesWheneverResolvedStylesDo()
    {
        var colorizer = new MarkdownLineColorizer();

        int initial = colorizer.StyleGeneration;

        colorizer.Styles = ModeStyles.WysiwygDefaults();
        int afterStyles = colorizer.StyleGeneration;
        Assert.NotEqual(initial, afterStyles);

        colorizer.IsDark = !colorizer.IsDark;
        Assert.NotEqual(afterStyles, colorizer.StyleGeneration);
    }

    [Fact]
    public void StyleGeneration_IsStableWhenNothingChanges()
    {
        // TableRowElementGenerator re-measures every table in the document when this moves, so a
        // counter that ticked on each redraw would quietly turn the layout cache off.
        var colorizer = new MarkdownLineColorizer { IsDark = false };

        int generation = colorizer.StyleGeneration;

        colorizer.Resolve(MDEdit.Editing.StyledElements.Bold);
        colorizer.Resolve(MDEdit.Editing.StyledElements.InlineCode);
        colorizer.IsDark = false;   // setter short-circuits on an unchanged value

        Assert.Equal(generation, colorizer.StyleGeneration);
    }
}
