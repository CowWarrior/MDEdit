using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MDEdit.Services;

namespace MDEdit;

/// <summary>
/// View → Preferences (Requirements.md §6): fonts and formatted-span colors, in separate Light and
/// Dark palettes matching the app's own architecture. Owned dialog, same convention as
/// <see cref="AboutWindow"/> (<c>Owner</c> + <see cref="Window.ShowDialog"/>), but unlike it, every
/// change here applies live via the <c>onChanged</c> callback — there is no separate Apply step.
/// </summary>
/// <remarks>
/// Cancel restores a snapshot of <see cref="AppSettings.EditorPreferences"/> taken when this window
/// opened (a <see cref="JsonSerializer"/> round-trip — <see cref="EditorPreferences"/> is a plain
/// data class, so no hand-written clone is needed) and re-applies it. Reset to Default does the
/// same with a fresh <see cref="EditorPreferences"/>. Both simply *replace*
/// <c>AppSettings.EditorPreferences</c> wholesale rather than mutating it property-by-property —
/// safe because <c>MainWindow.ApplyEditorPreferences</c> always re-reads
/// <c>_settings.EditorPreferences</c> fresh, never holds a stale reference to the old instance.
/// </remarks>
public partial class PreferencesWindow : Window
{
    // One row per customizable construct (Requirements.md §6's list — every construct that
    // already has an assigned color, nothing more). Code splits into two rows (text/background)
    // rather than trying to pack fg+bg into one row's four columns — keeps every row the same
    // uniform Light/Dark shape.
    private sealed record ColorRow(
        string Label,
        Func<EditorPreferences, string> GetLight, Action<EditorPreferences, string> SetLight,
        Func<EditorPreferences, string> GetDark, Action<EditorPreferences, string> SetDark);

    private static readonly ColorRow[] ColorRows =
    [
        new("Heading",
            p => p.HeadingColorLight, (p, v) => p.HeadingColorLight = v,
            p => p.HeadingColorDark,  (p, v) => p.HeadingColorDark = v),
        new("Blockquote",
            p => p.BlockquoteColorLight, (p, v) => p.BlockquoteColorLight = v,
            p => p.BlockquoteColorDark,  (p, v) => p.BlockquoteColorDark = v),
        new("Horizontal Rule",
            p => p.HorizontalRuleColorLight, (p, v) => p.HorizontalRuleColorLight = v,
            p => p.HorizontalRuleColorDark,  (p, v) => p.HorizontalRuleColorDark = v),
        new("Highlight (background)",
            p => p.HighlightBackgroundLight, (p, v) => p.HighlightBackgroundLight = v,
            p => p.HighlightBackgroundDark,  (p, v) => p.HighlightBackgroundDark = v),
        new("Strikethrough",
            p => p.StrikethroughColorLight, (p, v) => p.StrikethroughColorLight = v,
            p => p.StrikethroughColorDark,  (p, v) => p.StrikethroughColorDark = v),
        new("Code (text)",
            p => p.CodeForegroundLight, (p, v) => p.CodeForegroundLight = v,
            p => p.CodeForegroundDark,  (p, v) => p.CodeForegroundDark = v),
        new("Code (background)",
            p => p.CodeBackgroundLight, (p, v) => p.CodeBackgroundLight = v,
            p => p.CodeBackgroundDark,  (p, v) => p.CodeBackgroundDark = v),
        new("Link",
            p => p.LinkColorLight, (p, v) => p.LinkColorLight = v,
            p => p.LinkColorDark,  (p, v) => p.LinkColorDark = v),
        new("List Marker",
            p => p.ListMarkerColorLight, (p, v) => p.ListMarkerColorLight = v,
            p => p.ListMarkerColorDark,  (p, v) => p.ListMarkerColorDark = v),
        new("Comment",
            p => p.CommentColorLight, (p, v) => p.CommentColorLight = v,
            p => p.CommentColorDark,  (p, v) => p.CommentColorDark = v),
    ];

    private readonly AppSettings _settings;
    private readonly Action _onChanged;
    private readonly string _openedWithJson; // snapshot of EditorPreferences at open time, for Cancel

    private readonly Button[] _lightSwatches = new Button[ColorRows.Length];
    private readonly Button[] _darkSwatches  = new Button[ColorRows.Length];

    // Guards against SelectionChanged firing (and re-applying) while PopulateFontCombos/
    // RefreshControlsFromSettings are themselves setting SelectedItem.
    private bool _suppressFontEvents;

    internal PreferencesWindow(AppSettings settings, Action onChanged)
    {
        InitializeComponent();
        _settings = settings;
        _onChanged = onChanged;
        _openedWithJson = JsonSerializer.Serialize(_settings.EditorPreferences);

        PopulateFontCombos();
        BuildColorGrid();
        RefreshControlsFromSettings();
    }

    // Every installed font, individually — Fonts.SystemFontFamilies has no notion of a fallback
    // stack. Computed once; WithCurrentValueEnsured layers the actual current setting on top of
    // this each time a combo is refreshed, since that setting won't always be a plain entry here.
    private List<string> _installedFontFamilies = [];

    private void PopulateFontCombos()
    {
        _installedFontFamilies = Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // The code font's default is a fallback stack ("Cascadia Code, Consolas, Courier New"), which
    // will never match a single entry in Fonts.SystemFontFamilies — left as a plain lookup, the
    // combo would show blank on open and again after Reset to Default, even though the real,
    // working value is right there in the setting. Prepending the current value itself (when it
    // isn't already a plain installed-font match) guarantees there's always something to select
    // and display accurately, without turning the combo into a free-text/editable one.
    private static List<string> WithCurrentValueEnsured(List<string> installed, string currentValue)
        => installed.Contains(currentValue) ? installed : [currentValue, .. installed];

    private void BuildColorGrid()
    {
        ColorGrid.RowDefinitions.Add(new RowDefinition());
        AddHeaderCell("", 0, 0);
        AddHeaderCell("Light", 0, 1);
        AddHeaderCell("Dark", 0, 2);

        for (int i = 0; i < ColorRows.Length; i++)
        {
            int rowIndex = i + 1;
            int index = i; // fresh per-iteration local for the closures below — a `for` loop's
                            // own variable is shared across iterations, unlike foreach's.
            ColorGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });

            var label = new TextBlock { Text = ColorRows[i].Label, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(label, rowIndex);
            Grid.SetColumn(label, 0);
            ColorGrid.Children.Add(label);

            _lightSwatches[i] = MakeSwatchButton(() => OpenColorPicker(index, isDark: false));
            _darkSwatches[i]  = MakeSwatchButton(() => OpenColorPicker(index, isDark: true));
            Grid.SetRow(_lightSwatches[i], rowIndex);
            Grid.SetColumn(_lightSwatches[i], 1);
            Grid.SetRow(_darkSwatches[i], rowIndex);
            Grid.SetColumn(_darkSwatches[i], 2);
            ColorGrid.Children.Add(_lightSwatches[i]);
            ColorGrid.Children.Add(_darkSwatches[i]);
        }
    }

    private void AddHeaderCell(string text, int row, int column)
    {
        var block = new TextBlock { Text = text, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center };
        Grid.SetRow(block, row);
        Grid.SetColumn(block, column);
        ColorGrid.Children.Add(block);
    }

    private static Button MakeSwatchButton(Action onClick)
    {
        var button = new Button { Width = 48, Height = 22, Margin = new Thickness(4) };
        button.Click += (_, _) => onClick();
        return button;
    }

    private void OpenColorPicker(int index, bool isDark)
    {
        var row = ColorRows[index];
        var prefs = _settings.EditorPreferences;
        var currentHex = isDark ? row.GetDark(prefs) : row.GetLight(prefs);

        using var dialog = new System.Windows.Forms.ColorDialog
        {
            Color = System.Drawing.ColorTranslator.FromHtml(currentHex),
            FullOpen = true,
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        var newHex = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        if (isDark) row.SetDark(prefs, newHex); else row.SetLight(prefs, newHex);

        var swatch = isDark ? _darkSwatches[index] : _lightSwatches[index];
        swatch.Background = new SolidColorBrush(ParseColor(newHex));

        _onChanged();
    }

    // Re-syncs every control (font combos, all swatches) from the current, possibly just-replaced
    // AppSettings.EditorPreferences — called after construction and after Reset to Default.
    private void RefreshControlsFromSettings()
    {
        var prefs = _settings.EditorPreferences;

        _suppressFontEvents = true;
        WysiwygFontCombo.ItemsSource = WithCurrentValueEnsured(_installedFontFamilies, prefs.WysiwygFontFamily);
        WysiwygFontCombo.SelectedItem = prefs.WysiwygFontFamily;
        CodeFontCombo.ItemsSource = WithCurrentValueEnsured(_installedFontFamilies, prefs.CodeFontFamily);
        CodeFontCombo.SelectedItem = prefs.CodeFontFamily;
        _suppressFontEvents = false;

        for (int i = 0; i < ColorRows.Length; i++)
        {
            _lightSwatches[i].Background = new SolidColorBrush(ParseColor(ColorRows[i].GetLight(prefs)));
            _darkSwatches[i].Background  = new SolidColorBrush(ParseColor(ColorRows[i].GetDark(prefs)));
        }
    }

    private static Color ParseColor(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private void WysiwygFontCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFontEvents || WysiwygFontCombo.SelectedItem is not string family) return;
        _settings.EditorPreferences.WysiwygFontFamily = family;
        _onChanged();
    }

    private void CodeFontCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFontEvents || CodeFontCombo.SelectedItem is not string family) return;
        _settings.EditorPreferences.CodeFontFamily = family;
        _onChanged();
    }

    private void BtnResetToDefault_Click(object sender, RoutedEventArgs e)
    {
        _settings.EditorPreferences = new EditorPreferences();
        RefreshControlsFromSettings();
        _onChanged();
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        _settings.EditorPreferences = JsonSerializer.Deserialize<EditorPreferences>(_openedWithJson)!;
        _onChanged();
        DialogResult = false;
        Close();
    }
}
