using System.Text.Json;
using System.Windows;
using MDEdit.Services;

namespace MDEdit;

/// <summary>
/// View → Preferences (Requirements.md §6): per-element text styling, one tab per editor mode.
/// Owned dialog, same convention as <see cref="AboutWindow"/> (<c>Owner</c> +
/// <see cref="Window.ShowDialog"/>), but unlike it, every change applies live via the
/// <c>onChanged</c> callback — there is no separate Apply step.
/// </summary>
/// <remarks>
/// Cancel restores a snapshot of <see cref="AppSettings.EditorPreferences"/> taken when this window
/// opened (a <see cref="JsonSerializer"/> round-trip — <see cref="EditorPreferences"/> is a plain
/// data class, so no hand-written clone is needed) and re-applies it. Reset to Default does the
/// same with a fresh <see cref="EditorPreferences"/>. Both simply *replace*
/// <c>AppSettings.EditorPreferences</c> wholesale rather than mutating it property-by-property —
/// safe because <c>MainWindow.ApplyEditorPreferences</c> always re-reads
/// <c>_settings.EditorPreferences</c> fresh, and because the two editors reach their
/// <see cref="ModeStyles"/> through a callback rather than a captured reference.
/// </remarks>
public partial class PreferencesWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Action _onChanged;
    private readonly string _openedWithJson; // snapshot of EditorPreferences at open time, for Cancel

    private readonly ModeStyleEditor _wysiwyg;
    private readonly ModeStyleEditor _source;

    internal PreferencesWindow(AppSettings settings, Action onChanged)
    {
        InitializeComponent();
        _settings = settings;
        _onChanged = onChanged;
        _openedWithJson = JsonSerializer.Serialize(_settings.EditorPreferences);

        // The sample text renders in whichever theme is actually in effect, so it matches the
        // editor behind the dialog rather than always showing the light palette.
        bool IsDark() => ThemeService.IsDarkEffective(ThemeService.Parse(_settings.Theme));

        _wysiwyg = new ModeStyleEditor(
            () => _settings.EditorPreferences.Wysiwyg, ModeStyles.WysiwygDefaults, IsDark, _onChanged);
        _source = new ModeStyleEditor(
            () => _settings.EditorPreferences.Source, ModeStyles.SourceDefaults, IsDark, _onChanged);

        WysiwygTabHost.Content = _wysiwyg.Content;
        SourceTabHost.Content = _source.Content;
    }

    private void RefreshFromSettings()
    {
        _wysiwyg.Refresh();
        _source.Refresh();
    }

    private void BtnResetToDefault_Click(object sender, RoutedEventArgs e)
    {
        _settings.EditorPreferences = new EditorPreferences { Version = EditorPreferences.CurrentVersion };
        RefreshFromSettings();
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
