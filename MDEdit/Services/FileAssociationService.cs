using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MDEdit.Services;

/// <summary>
/// Registers MDEdit as an "Open with" option for .md/.markdown/.txt under HKCU.
/// Done in-app rather than via ClickOnce's FileAssociation manifest feature, which is a no-op for
/// Launcher-based (.NET Core) ClickOnce deployments despite the tooling accepting the declaration.
/// </summary>
internal static class FileAssociationService
{
    private const string ProgId = "MDEdit.Document";
    private static readonly string[] Extensions = [".md", ".markdown", ".txt"];

    // .md/.markdown rarely already have a handler, so it's safe to make MDEdit their actual default
    // (this is what makes Explorer show MDEdit's icon on the file, not just list it as an option).
    // .txt commonly already has one, so it stays OpenWithProgids-only to avoid hijacking it.
    private static readonly string[] ExtensionsToDefault = [".md", ".markdown"];

    public static void Register()
    {
        var exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine the running executable's path.");

        using (var progIdKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
        {
            progIdKey.SetValue(null, "MDEdit Document");
            using var iconKey = progIdKey.CreateSubKey("DefaultIcon");
            iconKey.SetValue(null, $"{exePath},0");
            using var commandKey = progIdKey.CreateSubKey(@"shell\open\command");
            commandKey.SetValue(null, $"\"{exePath}\" \"%1\"");
        }

        // OpenWithProgids adds MDEdit as an "Open with" candidate without touching the extension's
        // existing default handler (if any), so this never hijacks .txt from whatever already owns it.
        foreach (var ext in Extensions)
        {
            using var extKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}\OpenWithProgids");
            extKey.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }

        foreach (var ext in ExtensionsToDefault)
        {
            using var extKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}");
            extKey.SetValue(null, ProgId);
        }

        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
    }

    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const int SHCNF_IDLIST = 0x0000;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
