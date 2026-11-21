namespace MDEdit.Editing;

/// <summary>
/// The pure decisions behind Help → Set Default Apps and the one-time first-run question — pulled
/// out of <c>MainWindow</c>/<c>DefaultAppsService</c> so they're unit-testable, the same reasoning
/// as <see cref="ReleaseNotesGate"/>.
/// </summary>
/// <remarks>
/// MDEdit follows Windows' default-apps rules: it registers as a <em>candidate</em> handler and
/// sends the user to Settings to choose, and never writes a default itself (Windows 8+ forbids
/// that without user interaction in system UI). See <c>Services/DefaultAppsService</c>.
/// </remarks>
internal static class DefaultAppsGate
{
    /// <summary>The <c>RegisteredApplications</c> value name, and so the name Settings links to.</summary>
    public const string RegisteredAppName = "MDEdit";

    /// <summary>
    /// Whether a ProgID's open command survives application updates. Only ClickOnce's own command
    /// does — <c>rundll32.exe dfshim.dll, ShOpenVerbExtension {deployment-guid} %1</c> names the
    /// deployment rather than a version folder, and activates through ClickOnce so a double-click
    /// also checks for updates. A plain exe path points into one version's folder under
    /// <c>%LocalAppData%\Apps\2.0</c> and goes stale on the next update, so MDEdit refuses to
    /// advertise itself on top of one.
    /// </summary>
    public static bool IsUpdateProofCommand(string? command)
        => command is not null
           && command.Contains("dfshim.dll", StringComparison.OrdinalIgnoreCase)
           && command.Contains("ShOpenVerbExtension", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether to ask the first-run question. Asked once, ever (Microsoft's guidance: no repeated
    /// prompts), and only once MDEdit is actually registered — asking on a build where Settings
    /// wouldn't list MDEdit would spend the one question on a dead end.
    /// </summary>
    public static bool ShouldPrompt(bool alreadyAsked, bool registered) => !alreadyAsked && registered;

    /// <summary>
    /// The Settings page to open: MDEdit's own Default apps page when it is registered, otherwise
    /// the general page. The per-app link needs Windows 11 with the 2023-04 cumulative update or
    /// later; on older builds Windows opens the general page instead.
    /// </summary>
    public static string SettingsUri(bool registered)
        => registered
            ? "ms-settings:defaultapps?registeredAppUser=" + Uri.EscapeDataString(RegisteredAppName)
            : "ms-settings:defaultapps";
}
