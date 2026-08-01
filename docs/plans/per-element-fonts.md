# Per-element text styling (Requirements.md §6)

Plan of record. Written 2026-07-31, after the design questions below were settled with the user and
step 1's verification spike closed the one open technical risk.

Supersedes the shorter *(planned)* line in `Requirements.md` §6 ("A separate font choice per
customizable construct"), which described fonts only. The feature as scoped here also covers size,
weight, italic and text decoration.

## Decisions taken

| Question | Decision |
| --- | --- |
| Which elements are customizable | Block + inline, headings H1–H6 separate (19 elements) |
| Source vs WYSIWYG | **Two independent style sets**, one per editor mode |
| Preferences layout | Two tabs (WYSIWYG, Source), master–detail inside each |
| Base font | Each tab has its own base family + size; elements inherit and may override |
| Font drop-down | Monospaced families hoisted above a separator, then all installed families |
| Drawn list markers | Folded in — the WYSIWYG "•" and "1." follow the `listMarker` style |
| Underline + strikethrough | Mutually exclusive in the UI (AvalonEdit cannot combine them) |
| Fractional sizes | Whole points for XSHD-driven elements; resolved size shown in the UI |

Two earlier answers were superseded during the discussion and are recorded here so they aren't
re-litigated: an initial "split gating" answer (family/size WYSIWYG-only, everything else shared)
was replaced by the two independent style sets above; and separate `Underline` / `Strikethrough`
booleans were replaced by a single `Decoration` field.

## Step 1 findings (complete)

Verified against AvalonEdit tag `v6.3.1` — the release shipped as the `6.3.1.120` package this
project references. Byte-identical to `master`, so this is not a "works on latest source" result.

`HighlightingColorizer.ApplyColorToElement` applies **all eight** properties this feature needs:

```csharp
internal static void ApplyColorToElement(VisualLineElement element, HighlightingColor color, ITextRunConstructionContext context)
{
    if (color.Foreground != null) { ... element.TextRunProperties.SetForegroundBrush(b); }
    if (color.Background != null) { ... element.BackgroundBrush = b; }
    if (color.FontStyle != null || color.FontWeight != null || color.FontFamily != null) {
        Typeface tf = element.TextRunProperties.Typeface;
        element.TextRunProperties.SetTypeface(new Typeface(
            color.FontFamily ?? tf.FontFamily,
            color.FontStyle  ?? tf.Style,
            color.FontWeight ?? tf.Weight,
            tf.Stretch));
    }
    if (color.Underline ?? false)
        element.TextRunProperties.SetTextDecorations(TextDecorations.Underline);
    if (color.Strikethrough ?? false)
        element.TextRunProperties.SetTextDecorations(TextDecorations.Strikethrough);
    if (color.FontSize.HasValue)
        element.TextRunProperties.SetFontRenderingEmSize(color.FontSize.Value);
}
```

Three consequences, all load-bearing:

1. **`FontSize` is applied**, so per-element size for the 11 XSHD-driven elements is free. The
   expensive fallback — a per-span size pass in `MarkdownLineColorizer` over `FindEmphasisSpans` /
   `FindLinkSpans` / `FindUnderlineSpans` — is **not needed**. This was the single biggest risk in
   the plan and it is closed.
2. **`null` means inherit natively.** The typeface block rebuilds from the element's *current*
   typeface for whichever fields are null, so `ElementStyle`'s nullable-everything model maps 1:1
   onto AvalonEdit's own semantics with no code from us.
3. **Underline and strikethrough cannot combine.** The second `SetTextDecorations` call *replaces*
   the first rather than merging, so both-set renders strikethrough only. Resolved by making them
   mutually exclusive in the UI — see `ElementStyle.Decoration`.

`HighlightingColor.FontSize` is `int?`, hence whole points. `Background` is applied as
`element.BackgroundBrush`, not a text-run property.

## Settings model

```csharp
internal sealed class EditorPreferences
{
    public int Version { get; set; } = 2;          // absent/0 = the pre-per-element flat shape
    public ModeStyles Wysiwyg { get; set; } = ModeStyles.WysiwygDefaults();
    public ModeStyles Source  { get; set; } = ModeStyles.SourceDefaults();
    // legacy flat properties survive for one-time migration — see Migration
}

internal sealed class ModeStyles
{
    public string BaseFontFamily { get; set; } = "";
    public double BaseFontSize { get; set; }
    public Dictionary<string, ElementStyle> Elements { get; set; } = [];
}

internal sealed class ElementStyle
{
    public string? FontFamily { get; set; }      // null = inherit the mode's base family
    public double? FontScale { get; set; }       // null = 1.0 × base size
    public string? FontWeight { get; set; }      // null = inherit; "Normal" | "SemiBold" | "Bold"
    public bool?   Italic { get; set; }          // null = inherit
    public string? Decoration { get; set; }      // null = inherit; "None" | "Underline" | "Strikethrough"
    public string? ForegroundLight { get; set; } // null = no override
    public string? ForegroundDark { get; set; }
    public string? BackgroundLight { get; set; }
    public string? BackgroundDark { get; set; }
}
```

Five choices are load-bearing:

- **Everything nullable.** "Unset" must be distinguishable from "set to whatever the base happens to
  be", or changing the base font would stop propagating to any element the user once touched and
  reverted.
- **`FontWeight` is a string, not a bool.** Headings 1–3 are `Bold` today and **4–6 are `SemiBold`**.
  A Bold checkbox cannot express SemiBold, so H4–H6 would default to unchecked and silently render
  lighter than they do now. A four-entry combo (inherit / Normal / SemiBold / Bold) preserves them.
- **`FontScale` is a multiplier, not points.** Heading sizing is already a multiplier
  (`MarkdownLineColorizer.HeadingScale`), and a multiplier survives a later change to the base size.
  Absolute points are computed as `round(BaseFontSize × FontScale)` at definition-compile time.
- **`Decoration` is one field, not two booleans** — see step 1 finding 3.
- **Element keys are stable strings**, not enum ordinals. A reordered enum would silently remap
  saved settings onto the wrong elements.

### The 19 elements, and which rendering path owns each

| Key | Label | Path | XSHD color name |
| --- | --- | --- | --- |
| `heading1`…`heading6` | Heading 1…6 | colorizer | — |
| `blockquote` | Blockquote | colorizer | — |
| `horizontalRule` | Horizontal rule | colorizer | — |
| `bold` | Bold | XSHD | `Bold` |
| `italic` | Italic | XSHD | `Italic` |
| `boldItalic` | Bold + italic | XSHD | `BoldItalic` |
| `strikethrough` | Strikethrough | XSHD | `Strike` |
| `highlight` | Highlight | XSHD | `Highlight` |
| `underline` | Underline | XSHD | `Underline` |
| `inlineCode` | Inline code | XSHD | `InlineCode` |
| `codeBlock` | Code block | XSHD | `CodeBlock` |
| `link` | Link | XSHD | `Link` |
| `listMarker` | List marker | XSHD **and** generators | `ListMarker` |
| `comment` | HTML comment | XSHD | `Comment` |

`listMarker` is the one element on both paths: the XSHD color styles the raw `-` / `1.` in source
mode, and the same style feeds `BulletListMarkerElementGenerator` /
`NumberedListMarkerElementGenerator` so the drawn WYSIWYG "•" and "1." follow it too. Without that
second half the setting would appear to do nothing in WYSIWYG.

### Why today's rendering falls out of the defaults

Every current special case becomes an ordinary default, and the code implementing it gets deleted:

| Behaviour today | How the model expresses it |
| --- | --- |
| Source mode is all-mono | `Source.BaseFontFamily = "Cascadia Code, Consolas, Courier New"` |
| WYSIWYG prose is Arial | `Wysiwyg.BaseFontFamily = "Arial"` |
| Heading scaling is WYSIWYG-only | `Source["heading1"].FontScale = null` (⇒ 1.0); `Wysiwyg["heading1"].FontScale = 1.6` |
| Code stays mono when WYSIWYG flips the base | `Wysiwyg["codeBlock"].FontFamily` set explicitly; `Source["codeBlock"].FontFamily = null` |
| Hyperlinks underline in WYSIWYG only (added after the fact) | `Wysiwyg["link"].Decoration = "Underline"`; `Source["link"].Decoration = null` — in source the `[text](url)` syntax is visible and already reads as a link |
| Strikethrough strikes in both modes (added after the fact) | `Decoration = "Strikethrough"` in the shared defaults; WYSIWYG additionally clears the grey foreground, since greyed *and* struck reads as doubly deleted. Source keeps the grey as a second cue while reading raw syntax |

**Two later changes broke the "renders identically" bar deliberately** (beyond the WYSIWYG bullet
colour noted below): the code palette became green-on-black in light theme and amber in dark, and
`Migrate` gained a `Customized`/`V1` comparison so it carries over **only values the user actually
changed**. The second exists because of the first — without it, a default changed after §6 shipped
would reach fresh installs only, pinning upgrading users to the old look with nothing in the UI to
explain why. It only helps files still at version 0; once stamped, a later default change reaches a
file only via Reset to Default.
| Bold renders bold in both modes | `FontWeight = "Bold"` in both sets |
| Blockquote is italic in both modes | `Italic = true` in both sets |
| Strikethrough is grey, not actually struck | `ForegroundLight = "#888888"`, `Decoration = null` — **no longer true**: both modes now strike, deliberately. See the deltas above |

`MarkdownLineColorizer.HeadingScale` and the `LivePreviewEnabled` gate on it both disappear. Base
size is 14 in both sets — the editor's current `MainWindow.xaml` value.

Full default tables live in `ModeStyles.SourceDefaults()` / `WysiwygDefaults()` and are pinned
element-by-element by `EditorPreferencesTests`.

## Rendering

**1. XSHD-driven elements (11).** `LoadDefinition(bool dark, bool wysiwyg)` widens its existing
override table from 3 fields to 8 and writes them onto the matching `XshdColor`. **Four cached
definitions** (light/dark × source/WYSIWYG) instead of two. `UpdateHighlighting` already computes
`dark`; it gains `_settings.LivePreview`. `UpdateLivePreviewState` must now call
`UpdateHighlighting(_files.CurrentPath)` so toggling editor mode swaps definitions — it does not
today.

**`Resources/Markdown.xshd` does change after all**, contrary to the original plan. Every styleable
property has to be *cleared* before the element's style is applied, or "inherit" doesn't inherit: an
override the user cleared would silently fall back to whatever the grammar baked in. Once clearing
is in place the file's visual attributes are dead, so they were stripped — the colours are now bare
`<Color name="…"/>` declarations that exist for the rules to reference, and the defaults of record
live only in `EditorPreferences`. `MarkdownXshdTests` pins that the file stays free of visual
attributes, since a re-added one would read as configuration while changing nothing.

**2. Colorizer-driven elements (8).** `MarkdownLineColorizer` gains a reference to the active
`ModeStyles` and resolves each line's element style. `ColorLine` grows family / size / weight /
italic / decoration parameters. The heading branch keys off `$"heading{level}"` instead of the
hardcoded `level <= 3` weight split.

**3. Base font and size.** `UpdateLivePreviewState` sets `Editor.FontFamily` *and* `Editor.FontSize`
from the active mode's base — one extra line beside the existing family swap.

**4. Drawn list markers.** `BulletListMarkerElementGenerator` and
`NumberedListMarkerElementGenerator` each gain a `Typeface` + size, pushed from
`ApplyEditorPreferences` — the same imperative-property-push convention as their existing
`Enabled` / `CaretLine`. Neither measures anything it caches, so unlike `TableRowElementGenerator`
there are no cache implications.

**The one real conflict.** `ApplyRevealedSourceFont` swaps revealed text to the mono font, and works
today *because* `ColorLine` rebuilds the typeface from `old.FontFamily`, preserving the swap. Once
`ColorLine` sets an explicit family that stops being true. Fix: `ApplyRevealedSourceFont` returns
whether it whole-line-swapped, and `ColorizeLine` passes that down so the family override is skipped
on a revealed line. Revealed text keeps swapping to the **Source** base family only, as today.
Rendering revealed text in the full Source element style is a tempting follow-up and deliberately
not in this version.

**Ordering, already verified.** `HighlightingColorizer` is installed before `MarkdownLineColorizer`
(and re-inserted at index 0 on every `SyntaxHighlighting` re-set), so the colorizer always runs last
and wins. That is what makes path 2 able to override path 1.

## Preferences window

`TabControl` with two tabs built by **one shared method** taking a `ModeStyles`, so the tabs are the
same code and cannot drift.

```
┌ Preferences ────────────────────────────────────────────────┐
│ ┌─WYSIWYG─┬─Source─┐                                        │
│ │         └────────┴───────────────────────────────────────┐│
│ │  Base font: [Arial              ▾]   Size: [14  ]        ││
│ ├──────────────────────────────────────────────────────────┤│
│ │ Elements       │  Heading 1                               ││
│ │ ───────────────│ ─────────────────────────────────────────││
│ │ Heading 1   ◀  │  Font:   [(inherit — Arial)          ▾]  ││
│ │ Heading 2      │  Size:   [1.60 ×]      = 22 pt           ││
│ │ Heading 3      │  Weight: [Bold                       ▾]  ││
│ │ Heading 4      │  Decoration: [None                   ▾]  ││
│ │ Heading 5      │  ▣ Italic                                ││
│ │ Heading 6      │                                          ││
│ │ Blockquote     │  Text:       Light [███]   Dark [███]    ││
│ │ Bold           │  Background: Light [ — ]   Dark [ — ]    ││
│ │ Italic         │ ─────────────────────────────────────────││
│ │ Inline code    │  Sample                                  ││
│ │ Link        ⋮  │  The quick brown fox                     ││
│ │                │            [Reset this element]          ││
│ ├──────────────────────────────────────────────────────────┤│
│         [Reset to Default]        [OK]        [Cancel]      │
└─────────────────────────────────────────────────────────────┘
```

- **Italic is a tri-state checkbox** (`IsThreeState`) — indeterminate *is* inherit, free from WPF.
- **Weight and Decoration are four-entry combos**, first entry `(inherit)`.
- **Font combo**: `"(inherit — <base family>)"` first, then monospaced families, a `Separator`, then
  all families. Detection lives in a new `Editing/FontCatalog.cs` — compare `GlyphTypeface`
  `AdvanceWidths` for `i` / `W` / `M`; equal advances means monospaced. Both tabs use it, since
  WYSIWYG's code elements want mono too.
- **Live sample** renders the element with its resolved style, and the resolved absolute point size
  is shown beside the multiplier so whole-point rounding is visible rather than mysterious.
- Cancel snapshot and Reset keep their current shape (JSON round-trip, wholesale replacement of
  `AppSettings.EditorPreferences`), which stays safe for the documented reason —
  `ApplyEditorPreferences` always re-reads the object fresh and never holds a stale reference.

Everything is built imperatively in code-behind, matching `BuildColorGrid` and
`RebuildRecentFilesMenu`. No data binding is introduced.

## Migration

Existing users have customized colours in the flat shape; losing them on upgrade is not acceptable.
`Version` (absent ⇒ 0) drives a one-time fold in `SettingsService.Load`.

**`Version` must have no property initializer, and is stamped by `SettingsService.Save`.**
System.Text.Json leaves a property initializer in place when the property is absent from the JSON,
so initializing it to `CurrentVersion` makes every legacy file claim to be migrated already and skip
the fold entirely — silently, with the user's palette reverting to defaults. Stamping on write is
also what the value honestly means: the schema the *file* was written with, not what some in-memory
object believes. (Found the hard way — three tests failed on exactly this.)

The mapping:

- `WysiwygFontFamily` → `Wysiwyg.BaseFontFamily`
- `CodeFontFamily` → `Source.BaseFontFamily` **and** `Wysiwyg["inlineCode"/"codeBlock"].FontFamily`
- every `*ColorLight` / `*ColorDark` → the matching element's foreground/background **in both mode
  sets**, since colours are mode-independent today and both should inherit the old value

The legacy properties stay non-nullable and in use until MainWindow is switched over to the new
model (order of work step 6). Only then do they become `string?` with
`[JsonIgnore(Condition = WhenWritingNull)]` and get nulled after folding, so they disappear from
`settings.json` on the next save rather than lingering as dead keys. Doing that earlier would break
`ApplyEditorPreferences`, which still reads them.

## Order of work

1. ~~Spike: does `HighlightingColorizer` apply `FontSize`?~~ **Done — yes.** See step 1 findings.
2. ~~Settings model + defaults, with `EditorPreferencesTests` rewritten to pin both sets element by
   element. Pure; legacy properties left untouched and still in use, so everything stays green.~~
   **Done.** `Editing/StyledElements.cs` (the canonical element catalog) was added beyond the
   original file list — it keeps keys, labels and XSHD color names in one place instead of spreading
   them across the model, MainWindow and PreferencesWindow. 12 tests, 469 total passing, 0 warnings.
3. ~~Migration + `EditorPreferencesMigrationTests`. Still pure.~~ **Done.** `SettingsService`
   gained an internal `Deserialize(string)` so the whole load-and-migrate path is testable without
   touching `%AppData%`, and `StyledElements.Heading(int level)` is now the single level→key mapping.
   10 migration tests, 479 total passing, 0 warnings.
4. ~~`FontCatalog` + `FontCatalogTests`.~~ **Done.** Detection measures glyph advance widths for
   `i` / `W` / `M` / `.` via `GlyphTypeface` — WPF exposes no fixed-pitch flag and doesn't surface
   the OS/2 table. Comma-separated fallback stacks classify correctly (the default code font is one),
   and unusable names return false rather than throwing, so one damaged font can't stop Preferences
   opening. 15 tests, 494 total passing, 0 warnings.
5. ~~`LoadDefinition` widened to 8 fields, four cached definitions, `UpdateHighlighting` /
   `UpdateLivePreviewState` wiring.~~ **Done.** `LoadDefinition` was extracted from `MainWindow`
   into `Editing/MarkdownHighlighting.Build(ModeStyles, dark)` so the compiled definitions are
   testable without a window, and the persisted-value → WPF-type mapping into
   `Editing/StyleResolver` so path 2 can't disagree with it in step 6. `Markdown.xshd` stripped —
   see Rendering. **Carries a temporary bridge**: `EditorPreferences.SyncFromLegacy()`, called from
   `ApplyEditorPreferences`, pushes the flat properties (which `PreferencesWindow` still edits) into
   the per-element model, so colour changes keep working until step 7. 15 tests, 509 total, 0 warnings.
6. ~~`MarkdownLineColorizer` + the reveal-ordering fix; delete `HeadingScale`; feed the two list
   generators.~~ **Done.** Style resolution moved into `StyleResolver.Resolve` returning a
   `ResolvedStyle`, cached per element key in the colorizer and pushed to the list generators by a
   new `MainWindow.ApplyActiveModeStyles`. `BlockquoteAccentBarRenderer` now reads the blockquote
   colour through the colorizer instead of holding its own copy (per-mode colours made two copies
   materially easier to desynchronise), matching `HorizontalRuleRenderer`. `ListMarkerStyling` is
   shared by the two list generators so bullets and numbers can't drift. 24 tests, 533 total,
   0 warnings.

   Two deliberate default changes: `blockquote` and `horizontalRule` now pin `FontWeight = Normal`,
   because the old `ColorLine` calls forced it and `> **bold**` rendered flat; and the WYSIWYG
   bullet now honours the List marker colour, so it turns blue — the one accepted break of the
   "renders identically" bar, chosen because the drawn "•" contradicting the raw "-" was the
   inconsistency folding these in was meant to end.

   **Deferred to step 7** (kept smaller than planned): the legacy properties and `SyncFromLegacy`
   still stand, because `PreferencesWindow` still edits them. Retiring them is step 7's job, done
   together with the window rewrite. **Step 7 must delete `SyncFromLegacy` and its call site** — once
   Preferences edits the model directly it would overwrite real per-element choices with the stale
   defaults in those properties.
7. ~~`PreferencesWindow` rewrite.~~ **Done.** Two tabs, each filled by one `ModeStyleEditor`
   instance (new file) so they cannot drift; the window keeps only the OK/Cancel/Reset shell and the
   snapshot. `ModeStyles` reaches each editor as a `Func<ModeStyles>`, never a captured reference,
   since Cancel and Reset replace the preferences object wholesale. **Legacy properties retired**:
   nullable, `JsonIgnore`-when-null, migration-input only, nulled once folded and dropped from the
   file on the next save; `SyncFromLegacy` deleted along with `MainWindow.FreezeBrush`/`ParseColor`,
   and the toolbar highlight swatch now resolves from the active mode. 535 total, 0 warnings.
8. ~~Docs — `Requirements.md` §6, `CLAUDE.md`.~~ **Done.** §6 rewritten around the two-tab model,
   the nineteen elements, inheritance, and the two surfaced limits. `CLAUDE.md` updated in ten
   places: the *(planned)* list, `PreferencesWindow`/`ModeStyleEditor`, `SettingsService`,
   `ThemeService`, `MarkdownLineColorizer`, `Markdown.xshd`, `BlockquoteAccentBarRenderer`, the list
   generators, the `Editing/` bullets for the five new files, and five new test-table rows.

**All steps complete.** `ReleaseNotes.md` is deliberately untouched: it is the user-facing changelog,
written once per *published* release, not per change — see the `publish-clickonce` skill.

## Follow-up: the `normal` element

Added after the eight steps, on request. Body text became a nineteenth element, **first in the
list**, so the default sample is visible at a glance — and, once it existed, the tab's separate
"Base font" row was redundant, so it was absorbed: the base *is* `normal`, and there is now exactly
one place each value is set.

It is the odd element in three ways, all of which fall out of it being the thing everything else
inherits from rather than a Markdown construct:

- **Family and size are the mode's `BaseFontFamily`/`BaseFontSize`**, not fields of its
  `ElementStyle` — a size expressed as a multiplier of itself would be circular. Its size box holds
  absolute points while every other element holds a multiplier, so the unit label and the
  resolved-size readout switch with the selection.
- **Weight, italic and foreground apply to the editor control itself** (`MainWindow.
  ApplyNormalTextStyle`), through neither construct-styling path. An unset foreground restores the
  theme's `DynamicResource` rather than clearing the property: `MainWindow.xaml` sets it as a local
  dynamic reference, so assigning a plain brush would pin the colour and silently stop it tracking
  the theme for good.
- **Decoration and background are disabled, not hidden**, with a tooltip saying why — a decoration
  would need a whole-document colorizer pass that fights the per-line construct styling, and the
  background here is the editor surface, already owned by View → Theme. `SetEnabled` also sets
  `ToolTipService.ShowOnDisabled`, since a disabled control swallows the tooltip too.

Defaults override nothing, so an unmodified install renders body text exactly as before.

Steps 2–4 land green before any rendering changes, so a failure in 5–7 is unambiguously a wiring
problem.

## Files

**New** — `MDEdit/Editing/StyledElements.cs`; `MDEdit/Editing/FontCatalog.cs`;
`MDEdit/Editing/StyleResolver.cs`; `MDEdit/Editing/MarkdownHighlighting.cs`;
`MDEdit/Editing/ListMarkerStyling.cs`; `MDEdit/ModeStyleEditor.cs`;
`MDEdit.Tests/EditorPreferencesMigrationTests.cs`; `MDEdit.Tests/FontCatalogTests.cs`;
`MDEdit.Tests/MarkdownHighlightingTests.cs`; `MDEdit.Tests/StyleResolverTests.cs`.

**Modified** — `MDEdit/Services/SettingsService.cs` (model + defaults + migration, the largest
single change); `MDEdit/MarkdownLineColorizer.cs`; `MDEdit/MainWindow.xaml.cs`;
`MDEdit/Editing/BulletListMarkerElementGenerator.cs`;
`MDEdit/Editing/NumberedListMarkerElementGenerator.cs`; `MDEdit/PreferencesWindow.xaml` + `.xaml.cs`
(substantially rewritten); `MDEdit.Tests/EditorPreferencesTests.cs`;
`MDEdit.Tests/MarkdownXshdTests.cs`; `Requirements.md`; `CLAUDE.md`.

`Resources/Markdown.xshd` loses every visual attribute (see Rendering, path 1).

## Deliberately out of scope

- **Table cell fonts.** `TableRowElementGenerator` measures column widths with `FormattedText` and
  caches a `TableLayout`; per-element fonts would need that cache re-keyed on the element style, not
  just the base-font flip it already handles. Also entangled with the separately-planned
  "inline formatting inside WYSIWYG table cells" item.
- **The drawn task-list checkbox** — shapes, not text.
- **Rendering revealed source text in the full Source element style** (see the reveal conflict
  above); revealed text uses the Source base family only.
- **Per-element styling of image and emoji elements** — no text to style.

## Known limitations to document in `Requirements.md`

- Underline and strikethrough are mutually exclusive per element.
- Sizes for the 11 XSHD-driven elements resolve to whole points; the resolved size is shown in
  Preferences.
