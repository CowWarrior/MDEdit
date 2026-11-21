namespace MDEdit.Editing;

/// <summary>
/// Works out which file, if any, MDEdit was launched to open — pulled out of <c>MainWindow</c> so
/// it's unit-testable, the same reasoning as <see cref="ReleaseNotesGate"/>.
/// </summary>
/// <remarks>
/// There are two delivery routes, and a double-clicked file arrives by the second one.
/// <list type="bullet">
/// <item><b>The command line</b> (<c>args[1]</c>): "Open with" an exe picked by hand, a shortcut,
/// a terminal, and <c>MainWindow.NewWindow</c>.</item>
/// <item><b>ClickOnce activation data.</b> The pubxml's <c>FileAssociation</c> items register
/// <c>.md</c>/<c>.markdown</c> with an open command of <c>rundll32 dfshim.dll,ShOpenVerbExtension</c>,
/// which activates the app <em>through ClickOnce</em> (so a double-click also checks for updates,
/// and the command never names a version-specific folder that an update would leave stale). That
/// route does <b>not</b> put the file on the command line: for .NET 5+ the launcher exposes it as
/// the environment variable <see cref="ActivationDataVariable"/>. Reading only <c>args[1]</c> is
/// what made a double-clicked file open a blank window.</item>
/// </list>
/// The command line wins when both are present, since it is the more specific request.
/// <para>
/// Under .NET Framework ClickOnce the same activation data was a <c>file:</c> URI, and Microsoft's
/// .NET 5+ documentation doesn't say which form the variable carries, so both are accepted. Only a
/// value that literally starts with <c>file:</c> is treated as a URI: <c>Uri.TryCreate</c> would
/// also claim a plain <c>C:\…</c> path, and would read a <c>#</c> in it as a fragment.
/// </para>
/// </remarks>
internal static class StartupFile
{
    /// <summary>The variable the .NET ClickOnce launcher sets for the first activation-data element.</summary>
    public const string ActivationDataVariable = "ClickOnce_ActivationData_0";

    /// <summary>
    /// The file to open at startup, or null for none. <paramref name="args"/> is
    /// <c>Environment.GetCommandLineArgs()</c>, whose element 0 is the exe itself.
    /// </summary>
    public static string? Resolve(string[] args, string? activationData)
    {
        if (args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]))
            return args[1];

        if (string.IsNullOrWhiteSpace(activationData))
            return null;

        var value = activationData.Trim();
        if (value.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            && Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.IsFile)
        {
            return uri.LocalPath;
        }

        return value;
    }
}
