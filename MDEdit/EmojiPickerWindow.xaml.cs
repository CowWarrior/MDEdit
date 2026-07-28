using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MDEdit.Editing;

namespace MDEdit;

/// <summary>
/// Format → Emoji / the toolbar's emoji button (Requirements.md §3): a browsable, searchable list
/// of <see cref="EmojiCatalog"/>'s shortcodes. Third owned dialog after <see cref="AboutWindow"/>
/// and <see cref="PreferencesWindow"/>, but simpler than either — it's a pure chooser with no
/// settings/document dependency. Picking an emoji sets <see cref="SelectedShortcode"/>, sets
/// <c>DialogResult = true</c>, and closes immediately (pick-and-close, not a stay-open session) —
/// the caller (<c>MainWindow.BtnEmoji_Click</c>) reads it back once <see cref="Window.ShowDialog"/>
/// returns.
/// </summary>
public partial class EmojiPickerWindow : Window
{
    /// <summary>The shortcode the user picked (without colons), set only when <c>DialogResult</c> is true.</summary>
    public string? SelectedShortcode { get; private set; }

    // The entries BuildPanel most recently rendered, so Enter can commit "the first visible match"
    // without re-filtering EmojiCatalog.All a second time.
    private IReadOnlyList<(string Shortcode, string Emoji)> _visible = [];

    public EmojiPickerWindow()
    {
        InitializeComponent();
        BuildPanel(EmojiCatalog.All);
        Loaded += (_, _) => SearchBox.Focus();
    }

    private void BuildPanel(IReadOnlyList<(string Shortcode, string Emoji)> entries)
    {
        _visible = entries;
        EmojiPanel.Children.Clear();

        foreach (var (shortcode, emoji) in entries)
        {
            var content = new StackPanel { Width = 64 };
            content.Children.Add(new TextBlock
            {
                Text = emoji, FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center,
            });
            content.Children.Add(new TextBlock
            {
                Text = shortcode, FontSize = 10, Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });

            var button = new Button
            {
                Content = content, Margin = new Thickness(2), Padding = new Thickness(4),
                ToolTip = $":{shortcode}:",
            };
            button.Click += (_, _) => Commit(shortcode);
            EmojiPanel.Children.Add(button);
        }
    }

    private void Commit(string shortcode)
    {
        SelectedShortcode = shortcode;
        DialogResult = true;
        Close();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text.Trim();
        var filtered = query.Length == 0
            ? EmojiCatalog.All
            : EmojiCatalog.All.Where(entry => entry.Shortcode.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        BuildPanel(filtered);
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _visible.Count == 0) return;
        Commit(_visible[0].Shortcode);
        e.Handled = true;
    }
}
