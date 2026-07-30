using System.Windows;
using System.Windows.Media;

namespace MDEdit.Tests;

// Loads the real compiled Resources/ToolbarIcons.xaml through WPF's own pack URI resolution — the
// same mechanism App.xaml uses to merge it — so a malformed Data string (a typo makes Geometry
// parsing throw) fails a `dotnet test` run instead of only surfacing when the app starts and the
// toolbar fails to render. Mirrors MarkdownXshdTests' "load the real thing" approach for Markdown.xshd.
public class ToolbarIconsTests
{
    private static readonly string[] ExpectedKeys =
    [
        "IconEye", "IconEyeClosed",
        "IconBold", "IconItalic", "IconUnderline", "IconStrikethrough", "IconSubscript",
        "IconSuperscript", "IconHighlight", "IconHeading1", "IconHeading2", "IconHeading3",
        "IconLink", "IconInlineCode", "IconCodeBlock", "IconEmoji", "IconTable",
        "IconBulletList", "IconTaskList", "IconNumberedList", "IconBlockquote",
        "IconSettings",
    ];

    [Fact]
    public void ToolbarIcons_EveryExpectedKey_ResolvesToNonEmptyGeometry()
        => WpfTestApplication.RunOnSta(() =>
        {
            // The "pack" URI scheme is normally registered by Application's static constructor;
            // nothing else in this headless test process ever runs it, so ResourceDictionary.Source
            // below would otherwise fail to parse with "Invalid URI: Invalid port specified".
            WpfTestApplication.EnsureApplicationCreated();

            var dict = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/MDEdit;component/Resources/ToolbarIcons.xaml"),
            };

            foreach (var key in ExpectedKeys)
            {
                Assert.True(dict.Contains(key), $"missing icon resource '{key}'");
                var geometry = Assert.IsAssignableFrom<Geometry>(dict[key]);
                Assert.False(geometry.IsEmpty(), $"'{key}' parsed to an empty geometry");
            }
        });
}
