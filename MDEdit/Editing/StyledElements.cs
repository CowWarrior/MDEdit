namespace MDEdit.Editing;

/// <summary>
/// The canonical list of text-styleable elements (Requirements.md §6) — the single place that says
/// which constructs the user can restyle, what each is called in Preferences, and which
/// <c>Markdown.xshd</c> color (if any) carries it.
/// </summary>
/// <remarks>
/// Keys are <b>stable strings, never enum ordinals</b>: they are persisted as dictionary keys in
/// <c>settings.json</c>, so a reordered or renamed enum would silently remap a user's saved
/// settings onto the wrong elements. Renaming a key here is therefore a breaking change and needs a
/// migration; adding one is free (an absent entry simply inherits everything).
/// <para>
/// <see cref="Element.XshdColorName"/> is null for the elements rendered by
/// <c>MarkdownLineColorizer</c> rather than by the syntax-highlighting definition — headings,
/// blockquote and horizontal rule. Those are line-level constructs that XSHD's line-local rule
/// matching cannot express, which is why they were colorizer-driven before this setting existed.
/// </para>
/// <para>
/// <see cref="ListMarker"/> is the one element on <i>both</i> paths: its XSHD color styles the raw
/// <c>-</c>/<c>1.</c> in source mode, and the same style feeds
/// <c>BulletListMarkerElementGenerator</c>/<c>NumberedListMarkerElementGenerator</c> so the drawn
/// WYSIWYG "•" follows it too. Without that second half the setting would appear to do nothing in
/// WYSIWYG, since the marker is replaced by a rendered element there rather than colored in place.
/// </para>
/// </remarks>
internal static class StyledElements
{
    /// <summary>
    /// Default body text — the one element that is not a Markdown construct at all.
    /// </summary>
    /// <remarks>
    /// It is the mode's base: its family and size are <see cref="ModeStyles.BaseFontFamily"/> and
    /// <see cref="ModeStyles.BaseFontSize"/> rather than fields of its <see cref="ElementStyle"/>,
    /// since a size expressed as a multiplier of itself would be circular. Only its weight, italic
    /// and text colour live in the style, and they are applied to the editor control itself rather
    /// than through either construct-styling path. Decoration and background are deliberately not
    /// offered — see <c>MainWindow.ApplyActiveModeStyles</c>.
    /// </remarks>
    public const string Normal = "normal";

    public const string Heading1 = "heading1";
    public const string Heading2 = "heading2";
    public const string Heading3 = "heading3";
    public const string Heading4 = "heading4";
    public const string Heading5 = "heading5";
    public const string Heading6 = "heading6";
    public const string Blockquote = "blockquote";
    public const string HorizontalRule = "horizontalRule";
    public const string Bold = "bold";
    public const string Italic = "italic";
    public const string BoldItalic = "boldItalic";
    public const string Strikethrough = "strikethrough";
    public const string Highlight = "highlight";
    public const string Underline = "underline";
    public const string InlineCode = "inlineCode";
    public const string CodeBlock = "codeBlock";
    public const string Link = "link";
    public const string ListMarker = "listMarker";
    public const string Comment = "comment";

    /// <summary>
    /// The key for an ATX heading of the given level. Levels above 6 clamp to
    /// <see cref="Heading6"/>, matching <c>MarkdownSyntax.TryGetHeadingLevel</c>, which caps there.
    /// </summary>
    /// <remarks>
    /// The one place a heading level becomes a key. Building it by interpolating
    /// <c>$"heading{level}"</c> at call sites would work today and quietly break the moment a key is
    /// renamed, since nothing would reference the constants any more.
    /// </remarks>
    public static string Heading(int level) => level switch
    {
        1 => Heading1,
        2 => Heading2,
        3 => Heading3,
        4 => Heading4,
        5 => Heading5,
        _ => Heading6,
    };

    /// <param name="Key">Persisted identifier — see the stability note above.</param>
    /// <param name="Label">Display name, shown in the Preferences element list.</param>
    /// <param name="XshdColorName">
    /// Matching <c>&lt;Color name="…"&gt;</c> in <c>Resources/Markdown.xshd</c>, or null when the
    /// element is styled by <c>MarkdownLineColorizer</c> instead.
    /// </param>
    public sealed record Element(string Key, string Label, string? XshdColorName);

    /// <summary>
    /// Every styleable element, in the order Preferences lists them: block constructs first
    /// (headings top-down, then the other block-level ones), then inline constructs.
    /// </summary>
    public static IReadOnlyList<Element> All { get; } =
    [
        // First deliberately: it is what everything else inherits from, and seeing the default
        // sample is the quickest way to judge the rest.
        new(Normal, "Normal", null),
        new(Heading1, "Heading 1", null),
        new(Heading2, "Heading 2", null),
        new(Heading3, "Heading 3", null),
        new(Heading4, "Heading 4", null),
        new(Heading5, "Heading 5", null),
        new(Heading6, "Heading 6", null),
        new(Blockquote, "Blockquote", null),
        new(HorizontalRule, "Horizontal rule", null),
        new(CodeBlock, "Code block", "CodeBlock"),
        // Directly under Code block: the two share a palette by default and are almost always
        // adjusted together, so separating them would mean scrolling between two halves of one idea.
        new(InlineCode, "Inline code", "InlineCode"),
        new(ListMarker, "List marker", "ListMarker"),
        new(Bold, "Bold", "Bold"),
        new(Italic, "Italic", "Italic"),
        new(BoldItalic, "Bold + italic", "BoldItalic"),
        new(Strikethrough, "Strikethrough", "Strike"),
        new(Highlight, "Highlight", "Highlight"),
        new(Underline, "Underline", "Underline"),
        // Label only — the persisted key stays "link", since renaming a key would remap saved
        // settings onto the wrong element.
        new(Link, "Hyperlink", "Link"),
        new(Comment, "HTML comment", "Comment"),
    ];
}
