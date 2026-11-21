using System.Diagnostics;
using System.Runtime.InteropServices;
using MDEdit.Editing;
using Microsoft.Win32;

namespace MDEdit.Services;

/// <summary>
/// Registers MDEdit as a <em>candidate</em> handler for <c>.md</c>, <c>.markdown</c> and
/// <c>.txt</c>, and opens Windows' Default apps settings so the user can choose. Never writes a
/// default itself.
/// </summary>
/// <remarks>
/// <b>The open command is owned by ClickOnce, not by this class</b> — the pubxml's
/// <c>FileAssociation</c> items register the <see cref="ProgId"/> with an update-proof
/// <c>dfshim</c> command. Everything written here refers to that ProgID <em>by name only</em>
/// (<c>OpenWithProgids</c>, <c>Capabilities\FileAssociations</c>, <c>RegisteredApplications</c>),
/// so there is no path anywhere for an update to make stale. That is the whole design: the previous
/// <c>FileAssociationService</c> wrote the running exe's version-specific path, which broke on every
/// update and overwrote ClickOnce's command — don't reintroduce a path here.
/// <para>
/// <see cref="EnsureRegistered"/> runs on every launch, so anything removed self-heals; it writes
/// only values that differ, so an ordinary launch touches nothing and sends no shell notification.
/// It does nothing unless the ProgID's command is ClickOnce's (a dev build, or a machine where the
/// association was never installed), so MDEdit never advertises itself on top of a stale command.
/// </para>
/// <para>
/// <c>.txt</c> is deliberately not in the pubxml: a ClickOnce association is fixed at publish time
/// and asks nobody, and <c>.txt</c> nearly always has an owner already. Here it is only a
/// candidate, which takes nothing until the user picks MDEdit in Settings.
/// </para>
/// </remarks>
internal static class DefaultAppsService
{
    public const string ProgId = "MDEdit.Document";
    private static readonly string[] Extensions = [".md", ".markdown", ".txt"];

    private const string CapabilitiesPath = @"Software\MDEdit\Capabilities";

    /// <summary>
    /// Ensures MDEdit is registered as a candidate handler. Returns whether it is registered —
    /// false when there's no update-proof ProgID to point at. Never throws: a registry failure
    /// must not stop the editor starting.
    /// </summary>
    public static bool EnsureRegistered()
    {
        try
        {
            using var command = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}\shell\open\command");
            if (!DefaultAppsGate.IsUpdateProofCommand(command?.GetValue(null) as string)) return false;

            bool changed = false;

            foreach (var ext in Extensions)
            {
                using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}\OpenWithProgids");
                if (!key.GetValueNames().Contains(ProgId))
                {
                    key.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
                    changed = true;
                }
            }

            using (var caps = Registry.CurrentUser.CreateSubKey(CapabilitiesPath))
            {
                changed |= SetIfDifferent(caps, "ApplicationName", "MDEdit");
                changed |= SetIfDifferent(caps, "ApplicationDescription", "Markdown editor");
                using var assoc = caps.CreateSubKey("FileAssociations");
                foreach (var ext in Extensions)
                    changed |= SetIfDifferent(assoc, ext, ProgId);
            }

            using (var apps = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
                changed |= SetIfDifferent(apps, DefaultAppsGate.RegisteredAppName, CapabilitiesPath);

            if (changed) SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Opens Windows' Default apps settings — MDEdit's own page when registered.</summary>
    public static void OpenSettings(bool registered)
        => Process.Start(new ProcessStartInfo(DefaultAppsGate.SettingsUri(registered)) { UseShellExecute = true });

    private static bool SetIfDifferent(RegistryKey key, string name, string value)
    {
        if (key.GetValue(name) as string == value) return false;
        key.SetValue(name, value);
        return true;
    }

    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const int SHCNF_IDLIST = 0x0000;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
