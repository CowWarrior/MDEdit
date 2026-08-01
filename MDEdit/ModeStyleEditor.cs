using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MDEdit.Editing;
using MDEdit.Services;

namespace MDEdit;

/// <summary>
/// The editing UI for one editor mode's <see cref="ModeStyles"/> — a base font plus per-element
/// overrides (Requirements.md §6). One instance fills the WYSIWYG tab and another the Source tab.
/// </summary>
/// <remarks>
/// Built imperatively, following <c>PreferencesWindow.BuildColorGrid</c> and
/// <c>MainWindow.RebuildRecentFilesMenu</c>: this codebase has no MVVM or data-binding layer, and
/// introducing one for a single dialog would make it the odd one out.
/// <para>
/// Master–detail rather than a grid of every element × every attribute: nineteen elements each with
/// a family, size, weight, italic, decoration and four colours does not fit a readable table, and
/// splitting the attributes across tabs would separate one element's font from its colour.
/// </para>
/// <para>
/// <see cref="ModeStyles"/> arrives as a <see cref="Func{TResult}"/>, never a captured reference:
/// Cancel and Reset replace <c>AppSettings.EditorPreferences</c> wholesale, so a reference captured
/// at construction would leave this editing an object nothing renders from — the same hazard
/// <c>MainWindow.ApplyEditorPreferences</c> avoids by always re-reading.
/// </para>
/// </remarks>
internal sealed class ModeStyleEditor
{
    private const string InheritLabel = "(inherit)";
    private const string SampleText = "The quick brown fox";

    private readonly Func<ModeStyles> _styles;
    private readonly Func<ModeStyles> _defaults;
    private readonly Func<bool> _isDark;
    private readonly Action _onChanged;

    // Guards every handler while Refresh/SelectElement are themselves setting control values.
    private bool _suppress;

    private ListBox _elements = null!;
    private TextBlock _elementTitle = null!;
    private ComboBox _font = null!;
    private TextBox _scale = null!;
    private TextBlock _resolvedSize = null!;
    private ComboBox _weight = null!;
    private CheckBox _italic = null!;
    private ComboBox _decoration = null!;
    private TextBlock _scaleUnit = null!;
    private Button _backgroundClear = null!;
    private Button _foregroundLight = null!;
    private Button _foregroundDark = null!;
    private Button _backgroundLight = null!;
    private Button _backgroundDark = null!;
    private Border _sampleFrame = null!;
    private TextBlock _sample = null!;

    public FrameworkElement Content { get; }

    public ModeStyleEditor(Func<ModeStyles> styles, Func<ModeStyles> defaults, Func<bool> isDark, Action onChanged)
    {
        _styles = styles;
        _defaults = defaults;
        _isDark = isDark;
        _onChanged = onChanged;

        Content = Build();
        _elements.SelectedIndex = 0;
        Refresh();
    }

    private string SelectedKey => (_elements.SelectedItem as ElementItem)?.Key ?? StyledElements.All[0].Key;

    private bool IsNormalSelected => SelectedKey == StyledElements.Normal;

    // The list shows labels but has to round-trip keys; a record item keeps the two together rather
    // than relying on label-to-key lookups that break the moment two labels coincide.
    private sealed record ElementItem(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    // ── Construction ─────────────────────────────────────────────────────────

    private FrameworkElement Build()
    {
        // No separate "base font" row: the base *is* the Normal element, first in the list below,
        // so there is exactly one place each value is set rather than two competing ones.
        var root = new DockPanel();

        var split = new Grid();
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _elements = new ListBox { Margin = new Thickness(0, 0, 12, 0) };
        foreach (var element in StyledElements.All)
            _elements.Items.Add(new ElementItem(element.Key, element.Label));
        _elements.SelectionChanged += (_, _) => { if (!_suppress) SelectElement(); };
        Grid.SetColumn(_elements, 0);
        split.Children.Add(_elements);

        var detail = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = BuildDetailPanel(),
        };
        Grid.SetColumn(detail, 1);
        split.Children.Add(detail);

        root.Children.Add(split);
        return root;
    }

    private FrameworkElement BuildDetailPanel()
    {
        var panel = new StackPanel();

        _elementTitle = new TextBlock { FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(_elementTitle);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 6; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Font
        grid.Children.Add(Label("Font:", 0, 0));
        _font = new ComboBox { Margin = new Thickness(0, 2, 0, 2) };
        _font.SelectionChanged += (_, _) =>
        {
            if (_suppress || _font.SelectedItem is not string choice) return;
            // Normal has nothing to inherit from — it *is* the base — so its family is the mode's.
            if (IsNormalSelected) _styles().BaseFontFamily = choice;
            else Current().FontFamily = choice == InheritLabel ? null : choice;
            Changed();
        };
        Place(grid, _font, 0, 1);

        // Size
        grid.Children.Add(Label("Size:", 1, 0));
        var sizeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        _scale = new TextBox { Width = 56, VerticalContentAlignment = VerticalAlignment.Center };
        _scale.TextChanged += (_, _) =>
        {
            if (_suppress) return;
            // Ignore anything unparseable rather than fighting the user mid-keystroke; the value is
            // simply not written until the text is a usable number again.
            if (IsNormalSelected)
            {
                // Absolute points, not a multiplier — Normal is what the multipliers multiply.
                if (!TryParsePositive(_scale.Text, out double size)) return;
                _styles().BaseFontSize = size;
            }
            else if (_scale.Text.Trim().Length == 0) Current().FontScale = null;
            else if (TryParsePositive(_scale.Text, out double scale)) Current().FontScale = scale;
            else return;
            Changed();
        };
        sizeRow.Children.Add(_scale);
        _scaleUnit = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        sizeRow.Children.Add(_scaleUnit);
        // The resolved point size is shown because sizes for the XSHD-driven elements round to whole
        // points — without this, 1.10 and 1.15 silently producing the same result looks like a bug.
        _resolvedSize = new TextBlock
        {
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.7,
        };
        sizeRow.Children.Add(_resolvedSize);
        Place(grid, sizeRow, 1, 1);

        // Weight
        grid.Children.Add(Label("Weight:", 2, 0));
        _weight = ChoiceCombo(
            [InheritLabel, ElementStyle.WeightNormal, ElementStyle.WeightSemiBold, ElementStyle.WeightBold],
            value => Current().FontWeight = value);
        Place(grid, _weight, 2, 1);

        // Decoration — one combo rather than two checkboxes, because AvalonEdit cannot render
        // underline and strikethrough together (its second SetTextDecorations replaces the first).
        grid.Children.Add(Label("Decoration:", 3, 0));
        _decoration = ChoiceCombo(
            [InheritLabel, ElementStyle.DecorationNone, ElementStyle.DecorationUnderline, ElementStyle.DecorationStrikethrough],
            value => Current().Decoration = value);
        Place(grid, _decoration, 3, 1);

        // Italic — three-state, where indeterminate IS inherit.
        _italic = new CheckBox
        {
            Content = "Italic",
            IsThreeState = true,
            Margin = new Thickness(0, 6, 0, 2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _italic.Click += (_, _) =>
        {
            if (_suppress) return;
            Current().Italic = _italic.IsChecked;
            Changed();
        };
        Place(grid, _italic, 4, 1);

        panel.Children.Add(grid);
        panel.Children.Add(BuildColorSection());

        panel.Children.Add(new Separator { Margin = new Thickness(0, 12, 0, 8) });
        panel.Children.Add(new TextBlock { Text = "Sample", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
        _sample = new TextBlock { Text = SampleText, TextWrapping = TextWrapping.NoWrap };
        _sampleFrame = new Border { Padding = new Thickness(8), Child = _sample };
        panel.Children.Add(_sampleFrame);

        var reset = new Button
        {
            Content = "Reset this element",
            Padding = new Thickness(8, 4, 8, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        reset.Click += (_, _) =>
        {
            var key = SelectedKey;
            var styles = _styles();
            var defaults = _defaults();

            // Normal's family and size live on the mode itself, so resetting it has to restore
            // those too — otherwise the one element whose reset matters most would half-work.
            if (key == StyledElements.Normal)
            {
                styles.BaseFontFamily = defaults.BaseFontFamily;
                styles.BaseFontSize = defaults.BaseFontSize;
            }

            if (defaults.Elements.TryGetValue(key, out var fallback)) styles.Style(key).CopyFrom(fallback);
            else styles.Elements.Remove(key);

            SelectElement();
            Changed();
        };
        panel.Children.Add(reset);

        return panel;
    }

    private FrameworkElement BuildColorSection()
    {
        var grid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _foregroundLight = ColorSwatch(dark: false, background: false);
        _foregroundDark = ColorSwatch(dark: true, background: false);
        _backgroundLight = ColorSwatch(dark: false, background: true);
        _backgroundDark = ColorSwatch(dark: true, background: true);

        AddColorRow(grid, 0, "Text:", _foregroundLight, _foregroundDark, background: false);
        AddColorRow(grid, 1, "Background:", _backgroundLight, _backgroundDark, background: true);
        return grid;
    }

    private void AddColorRow(Grid grid, int row, string label, Button light, Button dark, bool background)
    {
        grid.Children.Add(Label(label, row, 0));

        var swatches = new StackPanel { Orientation = Orientation.Horizontal };
        swatches.Children.Add(new TextBlock { Text = "Light", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        swatches.Children.Add(light);
        swatches.Children.Add(new TextBlock { Text = "Dark", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 4, 0) });
        swatches.Children.Add(dark);

        // Clears both halves at once: a colour is a light/dark pair, and inheriting one while
        // pinning the other is a state with no sensible rendering.
        var clear = new Button { Content = "Clear", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(12, 0, 0, 0) };
        clear.Click += (_, _) =>
        {
            var style = Current();
            if (background) { style.BackgroundLight = null; style.BackgroundDark = null; }
            else { style.ForegroundLight = null; style.ForegroundDark = null; }
            SelectElement();
            Changed();
        };
        swatches.Children.Add(clear);
        if (background) _backgroundClear = clear;

        Place(grid, swatches, row, 1);
    }

    private Button ColorSwatch(bool dark, bool background)
    {
        var button = new Button { Width = 48, Height = 22, Margin = new Thickness(0, 3, 0, 3) };
        button.Click += (_, _) =>
        {
            var style = Current();
            var currentHex = background
                ? (dark ? style.BackgroundDark : style.BackgroundLight)
                : (dark ? style.ForegroundDark : style.ForegroundLight);

            using var dialog = new System.Windows.Forms.ColorDialog
            {
                Color = System.Drawing.ColorTranslator.FromHtml(currentHex ?? "#000000"),
                FullOpen = true,
            };
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            var hex = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
            if (background)
            {
                if (dark) style.BackgroundDark = hex; else style.BackgroundLight = hex;
            }
            else
            {
                if (dark) style.ForegroundDark = hex; else style.ForegroundLight = hex;
            }

            SelectElement();
            Changed();
        };
        return button;
    }

    private ComboBox ChoiceCombo(string[] choices, Action<string?> apply)
    {
        var combo = new ComboBox { Margin = new Thickness(0, 2, 0, 2), HorizontalAlignment = HorizontalAlignment.Left, Width = 160 };
        foreach (var choice in choices) combo.Items.Add(choice);
        combo.SelectionChanged += (_, _) =>
        {
            if (_suppress || combo.SelectedItem is not string choice) return;
            apply(choice == InheritLabel ? null : choice);
            Changed();
        };
        return combo;
    }

    // ── Refresh ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Re-reads every control from the current settings — after construction, and after Cancel or
    /// Reset has replaced the whole preferences object.
    /// </summary>
    public void Refresh() => SelectElement();

    // Repopulates the detail panel for whichever element is selected.
    private void SelectElement()
    {
        _suppress = true;

        var styles = _styles();
        var key = SelectedKey;
        bool normal = key == StyledElements.Normal;
        var element = StyledElements.All.First(e => e.Key == key);
        styles.Elements.TryGetValue(key, out var style);

        _elementTitle.Text = element.Label;

        // Normal has no inherit option and reads the mode's base; every other element inherits it.
        _font.ItemsSource = normal
            ? FontItems(inheritLabel: null, styles.BaseFontFamily)
            : FontItems($"(inherit — {styles.BaseFontFamily})", style?.FontFamily);
        _font.SelectedItem = normal ? styles.BaseFontFamily : style?.FontFamily ?? _font.Items[0];

        _scale.Text = normal
            ? styles.BaseFontSize.ToString(CultureInfo.CurrentCulture)
            : style?.FontScale?.ToString(CultureInfo.CurrentCulture) ?? "";
        _scaleUnit.Text = normal ? " pt" : " ×";

        _weight.SelectedItem = style?.FontWeight ?? InheritLabel;
        _decoration.SelectedItem = style?.Decoration ?? InheritLabel;
        _italic.IsChecked = style?.Italic;

        PaintSwatch(_foregroundLight, style?.ForegroundLight);
        PaintSwatch(_foregroundDark, style?.ForegroundDark);
        PaintSwatch(_backgroundLight, style?.BackgroundLight);
        PaintSwatch(_backgroundDark, style?.BackgroundDark);

        // Two attributes have no meaning for body text and are disabled rather than silently
        // ignored: a decoration would have to be painted over the whole document by the colorizer,
        // fighting the per-line construct styling; and the background here is the editor's own
        // surface, which View → Theme already owns.
        SetEnabled(_decoration, !normal, "Not available for body text — it would apply to every line of the document.");
        foreach (var control in new Control[] { _backgroundLight, _backgroundDark, _backgroundClear })
            SetEnabled(control, !normal, "Body text uses the editor background, which follows View → Theme.");

        UpdateSample();
        _suppress = false;
    }

    private static void SetEnabled(Control control, bool enabled, string disabledReason)
    {
        control.IsEnabled = enabled;
        control.ToolTip = enabled ? null : disabledReason;
        // A disabled control swallows mouse events, tooltip included, so the explanation would never
        // be readable without this.
        ToolTipService.SetShowOnDisabled(control, !enabled);
    }

    private void UpdateSample()
    {
        var styles = _styles();
        var resolved = StyleResolver.Resolve(SelectedKey, styles, _isDark());

        _sample.FontFamily = resolved.Family ?? new FontFamily(styles.BaseFontFamily);
        _sample.FontSize = resolved.EmSize ?? styles.BaseFontSize;
        _sample.FontWeight = resolved.Weight ?? FontWeights.Normal;
        _sample.FontStyle = resolved.Style ?? FontStyles.Normal;
        _sample.TextDecorations = resolved.Decorations;
        _sample.Foreground = resolved.Foreground ?? SystemColors.ControlTextBrush;
        _sampleFrame.Background = resolved.Background;

        // Normal's box already holds points, so restating them would just be noise.
        _resolvedSize.Text = IsNormalSelected ? ""
            : resolved.EmSize is double em
                ? string.Format(CultureInfo.CurrentCulture, "= {0:0.#} pt ({1} whole)", em, Math.Round(em))
                : $"= {styles.BaseFontSize:0.#} pt";
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    // Monospaced families are hoisted above a separator: source mode is fixed-width by design, and
    // WYSIWYG's code elements want a fixed-width family too, so making them findable matters more
    // than avoiding the duplication with the full list below.
    private static List<object> FontItems(string? inheritLabel, string? currentValue)
    {
        var items = new List<object>();
        if (inheritLabel is not null) items.Add(inheritLabel);

        int insertAt = items.Count;
        items.AddRange(FontCatalog.Monospaced);
        // Disabled so it can't be arrowed onto and "selected" — a Separator is its own container in
        // an ItemsControl, so nothing else would stop it becoming SelectedItem.
        items.Add(new Separator { IsEnabled = false });
        items.AddRange(FontCatalog.Installed);

        // A fallback stack ("Cascadia Code, Consolas, Courier New") is never a single installed
        // family, so without this the combo would show blank for the shipped default.
        if (currentValue is not null && !FontCatalog.Installed.Contains(currentValue))
            items.Insert(insertAt, currentValue);

        return items;
    }

    private ElementStyle Current() => _styles().Style(SelectedKey);

    private void Changed()
    {
        UpdateSample();
        _onChanged();
    }

    private static void PaintSwatch(Button swatch, string? hex)
    {
        swatch.Background = hex is null ? null : new SolidColorBrush(StyleResolver.ParseColor(hex));
        swatch.Content = hex is null ? "—" : "";
        swatch.ToolTip = hex ?? "Inherited";
    }

    private static bool TryParsePositive(string text, out double value)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) && value > 0;

    private static TextBlock Label(string text, int row, int column, Thickness? margin = null)
    {
        var block = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = margin ?? new Thickness(0, 0, 8, 0),
        };
        Grid.SetRow(block, row);
        Grid.SetColumn(block, column);
        return block;
    }

    private static void Place(Grid grid, UIElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }
}
