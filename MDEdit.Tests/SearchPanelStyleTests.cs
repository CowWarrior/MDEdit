using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Search;

namespace MDEdit.Tests;

// Loads the real compiled Resources/SearchPanelStyle.xaml through WPF's own pack URI resolution —
// the same mechanism App.xaml uses to merge it — and installs a real SearchPanel with it applied, so
// a typo in any named part fails a `dotnet test` run instead of silently breaking Find (or its
// theming) at runtime. A named-part typo wouldn't throw on its own — Template.FindName just returns
// null — so this checks the names directly rather than only that the XAML parses. Covers both
// AvalonEdit's two load-bearing names (PART_searchTextBox/PART_dropdownPopup, per
// SearchPanel.OnApplyTemplate) and every part MainWindow.ApplySearchPanelColors looks up by name.
// This only proves the names resolve structurally — it can't catch the two timing/template causes
// documented in CLAUDE.md's SearchPanelStyle.xaml entry, which only manifest in an actually-shown,
// actually-opened window. Mirrors ToolbarIconsTests' "load the real thing" approach.
public class SearchPanelStyleTests
{
    private static readonly string[] ExpectedPartNames =
    [
        "PART_searchTextBox", "PART_dropdownPopup", "PART_outerBorder", "PART_dropdownBorder",
        "PART_matchCaseCheckBox", "PART_wholeWordsCheckBox", "PART_useRegexCheckBox",
        "PART_prevIcon", "PART_nextIcon", "PART_closeIcon",
        "PART_prevButton", "PART_nextButton", "PART_closeButton",
    ];

    [Fact]
    public void SearchPanelStyle_EveryNamedPart_ResolvesAfterTemplateApplied()
        => WpfTestApplication.RunOnSta(() =>
        {
            var app = WpfTestApplication.EnsureApplicationCreated();

            var dict = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/MDEdit;component/Resources/SearchPanelStyle.xaml"),
            };
            app.Resources.MergedDictionaries.Add(dict);

            var editor = new TextEditor();
            editor.Measure(new Size(800, 600));
            editor.Arrange(new Rect(0, 0, 800, 600));

            var panel = SearchPanel.Install(editor);
            panel.ApplyTemplate();

            foreach (var name in ExpectedPartNames)
                Assert.True(panel.Template.FindName(name, panel) is not null, $"missing named part '{name}'");
        });
}
