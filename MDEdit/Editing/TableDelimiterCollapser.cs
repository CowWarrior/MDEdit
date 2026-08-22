using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace MDEdit.Editing;

/// <summary>
/// Removes the vertical space a rendered table's delimiter row would otherwise occupy, by
/// collapsing that document line the way folding does.
/// </summary>
/// <remarks>
/// <b>This is the one place live preview touches view state rather than only rendering</b>, and it
/// exists because nothing else can. <see cref="TableRowElementGenerator"/> already replaces the
/// delimiter line's content with a zero-size element, but a <c>VisualLine</c> still gets its full
/// line height from the text line it formats — so the band remained, reading as an empty row
/// between the header and the body rather than as the separator zone it was meant to be.
/// <see cref="TextView.CollapseLines"/> is AvalonEdit's own folding mechanism and the only API that
/// removes a line's height outright.
/// <para>
/// <b>Every refresh is coalesced onto the dispatcher at <see cref="DispatcherPriority.Background"/></b>
/// — the same trick <c>RemoteImageLoader</c> and Ctrl+wheel zoom already use here. That is not an
/// optimization: collapsing invalidates the height tree and so requests another layout, and the
/// natural trigger (<see cref="TextView.VisualLinesChanged"/>) fires *from* a layout pass. Posting
/// the work behind that pass keeps the mutation out of it entirely.
/// </para>
/// <para>
/// <b>The applied set is compared before anything is touched</b>, which is what stops the
/// VisualLinesChanged → collapse → VisualLinesChanged loop from running forever: the second pass
/// wants exactly what is already applied and returns without touching the height tree. It also
/// means ordinary typing outside a table costs one list build and a comparison, not a rebuild.
/// </para>
/// </remarks>
internal sealed class TableDelimiterCollapser
{
    private readonly TextView _textView;
    private readonly TableRowElementGenerator _generator;

    private readonly List<CollapsedLineSection> _sections = [];
    private List<int> _applied = [];
    private bool _refreshQueued;

    public TableDelimiterCollapser(TextView textView, TableRowElementGenerator generator)
    {
        _textView = textView;
        _generator = generator;
    }

    /// <summary>
    /// Schedules a refresh behind the current layout pass. Safe to call as often as you like — see
    /// the coalescing note above.
    /// </summary>
    public void RequestRefresh()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        _textView.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _refreshQueued = false;
            Refresh();
        });
    }

    /// <summary>
    /// Uncollapses everything and forgets it. Called when the document is replaced wholesale, whose
    /// lines the tracked sections no longer refer to.
    /// </summary>
    public void Reset()
    {
        if (UncollapseAll()) _textView.Redraw();
    }

    /// <summary>Drops every tracked section. Returns whether anything actually changed.</summary>
    private bool UncollapseAll()
    {
        _applied = [];
        if (_sections.Count == 0) return false;

        // Uncollapse is documented as doing nothing when the section is already uncollapsed, so a
        // section whose lines were deleted underneath it needs no special handling here.
        foreach (var section in _sections) section.Uncollapse();
        _sections.Clear();
        return true;
    }

    /// <summary>
    /// Whether <paramref name="lineNumber"/> is currently collapsed by this class.
    /// </summary>
    /// <remarks>
    /// <b><c>TableRowElementGenerator</c> spans its header element across the delimiter only when
    /// this says so — never by re-evaluating <c>TryGetRenderedTable</c> itself.</b> The two must
    /// agree exactly: AvalonEdit extends a <c>VisualLine</c> across document lines purely from an
    /// element's <c>DocumentLength</c>, while the height tree is what this class changes, and its
    /// own contract is that a visual line may span lines only "if all but the first are collapsed".
    /// A header that spans an uncollapsed line leaves the gap; a collapsed line no element spans
    /// makes the next layout throw "Trying to build visual line from collapsed line". Reading the
    /// applied set means the generator sees the state actually installed in the height tree, so
    /// only the harmless direction can transiently occur — rendered but not yet collapsed, which
    /// shows the old blank band for one frame and then corrects itself.
    /// </remarks>
    public bool IsCollapsed(int lineNumber) => _applied.Contains(lineNumber);

    /// <summary>
    /// Applies the collapse set immediately.
    /// </summary>
    /// <remarks>
    /// <b>Call this synchronously from anything that can stop a table being rendered</b> — a caret
    /// move into one, an edit, the live-preview toggle. Those all schedule a layout in which the
    /// generator will no longer span the delimiter, so the line has to be uncollapsed *before* that
    /// layout runs; deferring it is the one ordering that crashes. The posted
    /// <see cref="RequestRefresh"/> path remains for the opposite case, where nothing has changed
    /// except that a first layout has finally told the generator its typeface.
    /// </remarks>
    public void Refresh()
    {
        var document = _textView.Document;
        if (document is null) { Reset(); return; }

        var desired = ComputeDelimiterLines(document);
        if (desired.SequenceEqual(_applied)) return;

        UncollapseAll();
        foreach (int lineNumber in desired)
        {
            var line = document.GetLineByNumber(lineNumber);
            _sections.Add(_textView.CollapseLines(line, line));
        }
        _applied = desired;

        // REQUIRED, not a refresh nicety. Collapsing rewrites the height tree but does not
        // invalidate the VisualLines AvalonEdit has already built and cached, so a cached line for
        // a now-collapsed document line makes the next layout throw "Trying to build visual line
        // from collapsed line" — which is what happened on scroll before this call existed.
        // Safe against the VisualLinesChanged loop: the refresh this schedules finds the applied set
        // already matching and returns above without touching anything.
        _textView.Redraw();
    }

    /// <summary>
    /// The delimiter line of every table currently rendered as a grid, in document order.
    /// </summary>
    /// <remarks>
    /// Asks <see cref="TableRowElementGenerator.TryGetRenderedTable"/> rather than re-deriving
    /// anything, so a table's delimiter collapses under exactly the conditions its rows render
    /// under — including the caret reveal, which is what makes the line reachable for editing again
    /// the moment the caret enters the table. A whole-document walk, matching what
    /// <c>UpdateDocumentStats</c> already does per change; each hit skips to the block's end, so
    /// this is one pass over the lines rather than one per table.
    /// </remarks>
    private List<int> ComputeDelimiterLines(TextDocument document)
    {
        var lines = new List<int>();
        if (!_generator.Enabled) return lines;

        for (int n = 1; n <= document.LineCount; n++)
        {
            if (!_generator.TryGetRenderedTable(document, n, out var layout)) continue;
            lines.Add(layout.StartLine + 1);
            n = layout.EndLine;
        }
        return lines;
    }
}
