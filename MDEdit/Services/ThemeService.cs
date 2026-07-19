using System.Windows;
using Microsoft.Win32;

namespace MDEdit.Services;

internal enum AppTheme { Light, Dark, System }

internal static class ThemeService
{
    public static AppTheme Parse(string? value) =>
        Enum.TryParse<AppTheme>(value, ignoreCase: true, out var theme) ? theme : AppTheme.System;

    /// <summary>
    /// Applies the theme app-wide: Fluent ThemeMode covers standard control styling
    /// (menus, popups, buttons, dialogs), and the merged Themes/*.xaml dictionary drives
    /// the brushes MainWindow references via DynamicResource (chrome strips, editor).
    /// </summary>
    public static void Apply(AppTheme theme)
    {
        Application.Current.ThemeMode = theme switch
        {
            AppTheme.Light => ThemeMode.Light,
            AppTheme.Dark  => ThemeMode.Dark,
            _              => ThemeMode.System,
        };

        var dark = IsDarkEffective(theme);
        var dict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{(dark ? "Dark" : "Light")}.xaml")
        };

        // Remove only OUR previous theme dictionary. The filter must not match the Fluent
        // dictionaries the ThemeMode setter merges (".../PresentationFramework.Fluent;component/
        // Themes/Fluent.Dark.xaml") — matching on "Themes/" removed Fluent entirely, which left
        // classic light menu popups and toolbar buttons under the dark chrome brushes.
        var merged = Application.Current.Resources.MergedDictionaries;
        for (int i = merged.Count - 1; i >= 0; i--)
        {
            if (IsAppThemeDictionary(merged[i].Source?.OriginalString))
                merged.RemoveAt(i);
        }
        merged.Add(dict);
    }

    private static bool IsAppThemeDictionary(string? source) =>
        source is "Themes/Light.xaml" or "Themes/Dark.xaml"   // relative form used in App.xaml
        || source?.EndsWith(",/Themes/Light.xaml") == true    // pack URIs added by Apply
        || source?.EndsWith(",/Themes/Dark.xaml") == true;

    /// <summary>Resolves System to the OS setting; Light/Dark answer directly.</summary>
    public static bool IsDarkEffective(AppTheme theme) => theme switch
    {
        AppTheme.Dark  => true,
        AppTheme.Light => false,
        _              => IsSystemDark(),
    };

    private static bool IsSystemDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }
}
