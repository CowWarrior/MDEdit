namespace MDEdit.Editing;

/// <summary>
/// The zoom ladder (Requirements.md §6): 10% increments across 10%–500%, matching the range Word and
/// Notepad state.
/// </summary>
/// <remarks>
/// Pure arithmetic rather than a step table, kept out of MainWindow so the clamping and grid-aligning
/// edge cases can be unit-tested without a window — the same reason <see cref="RecentFiles"/> and
/// <see cref="CharacterCounter"/> live here.
/// <para>
/// <b>Every calculation goes through whole percent as an <c>int</c>, never through the double
/// directly.</b> Stepping by 0.1 in binary floating point does not land on the values it looks like
/// it should: <c>0.7 / 0.1</c> is 6.999999999999999, so a <c>Math.Floor</c>-based "next multiple of
/// 0.1" returns 0.7 again and <see cref="In"/> silently stops advancing at certain levels. Integer
/// percent has no such failure, and the ladder is defined in percent anyway.
/// </para>
/// </remarks>
internal static class ZoomLevels
{
    /// <summary>10%. Below this the editor is unreadable; see the note on <see cref="Sanitize"/>.</summary>
    public const double Min = 0.10;

    /// <summary>500%.</summary>
    public const double Max = 5.00;

    /// <summary>100% — the configured Preferences size, unscaled.</summary>
    public const double Default = 1.00;

    private const int MinPercent = 10;
    private const int MaxPercent = 500;
    private const int StepPercent = 10;

    /// <summary>
    /// The levels offered in the status bar's drop-down. 75% and 125% deliberately sit off the 10%
    /// grid: they are reachable by picking them, and <see cref="In"/>/<see cref="Out"/> re-align to
    /// the grid from there.
    /// </summary>
    public static readonly IReadOnlyList<double> Presets = [0.50, 0.75, 1.00, 1.25, 1.50];

    /// <summary>
    /// The next multiple of 10% strictly above <paramref name="level"/>, clamped to <see cref="Max"/>.
    /// </summary>
    /// <remarks>
    /// Re-aligns to the grid rather than adding 10 points blindly, so stepping up from a 75% preset
    /// gives 80% rather than 85% — one press of + from any level lands somewhere predictable.
    /// </remarks>
    public static double In(double level)
    {
        int percent = ToPercent(level);
        return FromPercent(percent / StepPercent * StepPercent + StepPercent);
    }

    /// <summary>
    /// The next multiple of 10% strictly below <paramref name="level"/>, clamped to <see cref="Min"/>.
    /// </summary>
    public static double Out(double level)
    {
        int percent = ToPercent(level);
        return FromPercent((percent - 1) / StepPercent * StepPercent);
    }

    /// <summary>
    /// Brings a persisted or hand-edited level back into range, to the nearest whole percent.
    /// </summary>
    /// <remarks>
    /// <c>settings.json</c> is user-editable, so the stored level is re-checked rather than trusted —
    /// the same treatment <c>RecentFiles.Sanitize</c> gives the MRU list. <b>Deliberately does not
    /// snap to the 10% grid</b>: a 75% or 125% preset must survive a restart, and grid-snapping here
    /// would quietly turn it into 80% or 130% on the next launch.
    /// </remarks>
    public static double Sanitize(double level)
        => double.IsFinite(level) ? FromPercent(ToPercent(level)) : Default;

    private static int ToPercent(double level)
        => double.IsFinite(level)
            ? Math.Clamp((int)Math.Round(level * 100), MinPercent, MaxPercent)
            : (int)(Default * 100);

    private static double FromPercent(int percent)
        => Math.Clamp(percent, MinPercent, MaxPercent) / 100.0;
}
