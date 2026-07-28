# MDEdit — Business Requirements

MDEdit is a desktop application for creating and editing Markdown documents. It targets users who write in Markdown regularly and want a dedicated, lightweight editor that stays out of the way: fast to open, comfortable to type in, and aware enough of Markdown syntax to make formatting effortless. The editor supports both Markdown and plain text files, offers real-time syntax highlighting to keep the structure of a document visible while writing, and provides a full set of formatting commands so common Markdown constructs can be inserted without remembering the exact syntax. A WYSIWYG mode displays the formatted output directly in the editor rather than the raw syntax, revealing the underlying Markdown around the cursor so it stays editable at all times.

## 1. Document Management

- The user can create a new empty document at any time.
- The user can open an existing Markdown file (`.md`, `.markdown`) or plain text file (`.txt`) from disk.
- The user can save the current document to its existing location.
- The user can save the current document to a new location and file type (Save As), choosing between Markdown and plain text formats.
- When there are unsaved changes and the user attempts to create a new document, open another file, or close the application, the user is prompted to save, discard, or cancel the operation.
- The user can reopen a recently used document from an MRU (most recently used) list in the File menu.
  - The list holds the ten most recent documents, newest first, and persists between sessions.
  - A document joins the list when it is opened, and when it is saved to a new location via Save As.
    The same file never appears twice, however it was reached.
  - Choosing an entry prompts to save any unsaved changes first, exactly as Open does.
  - Choosing an entry whose file no longer exists reports this and offers to remove it from the list,
    rather than failing silently or leaving a dead entry behind.
  - The user can clear the list.
- The title bar and status bar always reflect the current filename and whether the document has unsaved changes.

## 2. Editing

- The user can type and edit text freely regardless of file format.
- The editor supports standard text operations: undo, redo, cut, copy, paste, and select all.
- The editor supports a find/search function within the current document.

## 3. Markdown Formatting

The editor provides commands to insert or toggle Markdown formatting in any open document, regardless of file type. When text is selected, the command wraps the selection; when no text is selected, the command inserts the syntax and positions the cursor ready for input.

Commands that work on whole lines — bullet list, numbered list, task list, blockquote, and the
headings — apply to **every line the selection touches**, not only the first. Where a selection spans
a mixture, the command normalizes rather than flipping each line independently: it marks the lines
that aren't yet marked, so pressing it a second time clears them all. Blank lines inside a multi-line
selection are left alone, though the command still applies to a blank line when it is the only one
selected, since that is how a list is started. The whole affected block stays selected afterwards, and
one undo reverses the entire change.

The numbered list command numbers the selected lines in sequence — 1, 2, 3 — rather than repeating the
same number. Numbering continues across any blank lines within the selection, and a line that already
carries a number is renumbered in place.

### Inline formatting
- Bold
- Italic
- Strikethrough
- Inline code
- Highlight — `==text==`
- Superscript — `X^2^`
- Subscript — `H~2~O`
- Underline — `<u>text</u>`

### Block formatting
- Heading levels 1, 2, and 3
- Fenced code block
- Blockquote
- Bullet (unordered) list item
- Numbered (ordered) list item
- Task list item — `- [ ] todo` / `- [x] done`; the command inserts an unchecked item, adds a box to an existing bullet item, and toggles an existing task between checked and unchecked

### Insertions
- Hyperlink (inserts link syntax with placeholder text and URL)
- Table — inserts a starter table (header row, delimiter row, three body rows) with the cursor placed in the first header cell. Column alignment via `:---` (left), `:---:` (center), and `---:` (right) is supported in the delimiter row
- Emoji — inserts an emoji shortcode such as `:joy:`. Shortcodes are drawn from a catalogue shipped
  with the application; a `:name:` that is not in the catalogue is ordinary text, so everyday writing
  such as `10:30:45` is never mistaken for an emoji
- Emoji picker — the user can browse and search the shipped catalogue and insert an emoji from it,
  without having to remember the shortcode name. Selecting an entry inserts its **shortcode**, not the
  literal character, so the document stays consistent with emoji typed by hand
- Convert emoji to shortcodes — a command that replaces literal emoji characters with their catalogue
  shortcodes, across the selection if there is one and the whole document otherwise. This makes a
  pasted or imported document consistent with the shortcode form MDEdit writes, and keeps the source
  readable in editors that cannot display emoji. Characters with no catalogue entry are left alone
  *(planned)*

Highlight, superscript, subscript, task lists, tables, and emoji shortcodes are all *extended* Markdown syntax rather than part of the original specification. They are widely supported (GitHub, GitLab, and most modern renderers) but not universally, and a document using them may render as literal text elsewhere.

**Underline is a deliberate exception.** Markdown has no underline syntax in any dialect — underlining is conventionally reserved for hyperlinks, which is why no dialect added one. MDEdit therefore provides it as literal inline HTML: the command wraps the selection in `<u>` and `</u>`, relying on Markdown's own rule that inline HTML is passed through to the renderer untouched. Two consequences follow, and both are accepted rather than worked around:

- Underline renders only where the consuming renderer permits inline HTML. A renderer that strips or escapes HTML will drop the effect or show the tags as text.
- It is the only formatting command that emits HTML rather than Markdown, and the only one whose opening and closing markers differ from each other.

## 4. Syntax Highlighting

- When a Markdown file is open, the editor visually distinguishes Markdown elements using color and style cues (e.g. headings, bold, italic, code, links, blockquotes, list markers).
- When a plain text file is open, no syntax highlighting is applied.
- Highlighting updates in real time as the user types.
- Highlighted text is shown with a muted yellow background, in both the light and dark themes.
- Superscript and subscript text is raised or lowered from the baseline and shown smaller than the surrounding text, in both editor modes.
- Table rows are recognized when a header row sits directly above a delimiter row: the pipes and the
  delimiter row are visually muted so the cell content stays prominent. A row must start and end with
  a pipe to count — prose containing pipes is never mistaken for a table.
- Underlined text is shown underlined, in both editor modes.

## 5. WYSIWYG Mode

- The user can toggle between a syntax-highlighted source view and a WYSIWYG view that displays the formatted output directly in the editor.
- The toggle is accessible from the View menu, under Editor Mode (Source / WYSIWYG).
- The chosen editor mode persists between sessions.
- In WYSIWYG mode the document's Markdown text remains the single source of truth — the mode changes only how that text is displayed. Nothing is rewritten, and switching modes never alters the document.
- Syntax markers are hidden rather than deleted: they still occupy their position in the document, so selection, undo, and the saved file are unaffected.
- Hidden syntax is revealed again around the cursor so it stays directly editable. The amount revealed suits the construct: the whole line for headings, blockquotes, list items, and horizontal rules; the individual run for bold, italic, strikethrough, inline code, and links; and both fences of a fenced code block whenever the cursor is anywhere inside it.
- Constructs are rendered rather than merely stripped of their markers where that aids readability: headings display at a larger size per level, bullet items display a bullet glyph, blockquotes are indented with a vertical accent bar spanning the quote, one bar per nesting level, and a horizontal rule displays as a single drawn line spanning the width of the editor, reverting to its source text (`---`, `***`, and the like) while the cursor is on that line.
- A recognized emoji shortcode displays as the emoji character it stands for, reverting to the
  shortcode while the cursor is inside it. The character renders in monochrome rather than colour —
  a limitation of the underlying UI framework, not a defect in the editor.
- A task list item displays as a checkbox, ticked or empty according to its state, reverting to the source text while the cursor is on that line.
- An image reference (`![alt](path)`) displays as the rendered picture in place of its Markdown,
  reverting to the source syntax while the cursor is inside it — the same reveal-and-render treatment
  as an emoji shortcode. This lands in two phases: local, file-relative images first; images loaded
  from a remote `http(s)://` URL second, since fetching one automatically discloses that the document
  was opened, the same tracking-pixel concern email clients guard against — a decision to make
  deliberately, not a default to fall into *(planned)*
- Document text displays in a proportional document font, so writing feels like editing a document
  rather than code. Code blocks and inline code keep the editor's fixed-width font. Markdown
  revealed around the cursor — a heading, list, or blockquote line being edited, a table
  reverted to its source, or an inline run (bold, link, emoji, and the like) showing its
  markers — also shows in the fixed-width font, signalling that source text is being edited
  there. The source view is unaffected and remains entirely fixed-width.
- A table displays as a grid: columns sized to their widest cell, a bold shaded header row, the
  delimiter row hidden, and the delimiter row's column alignments honored. Moving the cursor into
  the table reverts the whole table to its source text — like a fenced code block — so it is always
  edited as raw Markdown. Cell text is shown as written; rendering inline formatting (bold, emoji,
  links) inside cells is *(planned)*.
- Underlined text displays as underlined, with its `<u>` and `</u>` tags hidden, on the same terms as the other inline constructs.

## 6. View Options

- The user can toggle line numbers on or off.
- The user can toggle word wrap on or off.
- The user can choose the application theme: Light, Dark, or System (follows the Windows app theme, including live OS theme changes).
- The user can choose how the status bar's character count charges a line break: as 0, 1, or 2
  characters (§9).
- The user can customize the editor's appearance from a Preferences window, opened via View →
  Preferences:
  - A separate font choice for WYSIWYG document text and for code (inline code and fenced code
    blocks), rather than today's fixed WYSIWYG font (Arial) and fixed monospace stack.
  - Text and background colors for the various formatted spans (headings, links, highlights,
    blockquotes, and the like), rather than today's fixed per-theme palette.
  - A separate font choice per customizable construct — for example, a different font for headings
    than for bold text — rather than today's single WYSIWYG font applied uniformly to every construct.
    This extends the same per-construct customization already offered for colors to fonts *(planned)*
  - A Reset to Default action, restoring every font and color choice on the form to its original
    fixed value in one step.

## 7. Keyboard Shortcuts

All common operations are accessible via keyboard shortcuts. Where an equivalent operation exists in
common word processors, its established shortcut is used in preference to inventing one:

| Action | Shortcut |
|---|---|
| New | Ctrl+N |
| Open | Ctrl+O |
| Save | Ctrl+S |
| Save As | Ctrl+Shift+S |
| Bold | Ctrl+B |
| Italic | Ctrl+I |
| Underline | Ctrl+U |
| Subscript | Ctrl+Shift+_ |
| Superscript | Ctrl+Shift++ |
| Heading 1 | Ctrl+1 |
| Heading 2 | Ctrl+2 |
| Heading 3 | Ctrl+3 |
| Find | Ctrl+F |
| Undo | Ctrl+Z |
| Redo | Ctrl+Y |
| Cut | Ctrl+X |
| Copy | Ctrl+C |
| Paste | Ctrl+V |
| Select All | Ctrl+A |

## 8. Toolbar

A toolbar provides one-click access to the most common formatting operations: bold, italic, strikethrough, highlight, superscript, subscript, underline, headings 1–3, inline code, code block, link, table, bullet list, task list, numbered list, and blockquote. File operations (new, open, save) and the word wrap toggle are available from the menu and keyboard shortcuts rather than the toolbar.

- The toolbar's first section, ahead of the formatting operations, is a single toggle button
  switching between Source and WYSIWYG editor mode — the toolbar equivalent of View → Editor Mode,
  kept in sync with it either way.

- Toolbar buttons currently show plain text/symbol labels (`B`, `I`, `H1`, `Link`, and so on) rather
  than icons. Replacing them with a proper icon set — possibly drawn from an icon font such as Font
  Awesome — is *(planned)*.

## 9. Status Bar

A status bar is always visible and displays:
- The document's size, as `512 bytes`, `1.5 KB`, `2.4 MB` and so on — bytes below one kilobyte, and
  one decimal place above it. Units are 1024-based and labelled KB/MB/GB, matching what Windows
  Explorer reports, so the two figures agree for a saved file.
- The document's character count.
- When text is selected, an additional section showing the number of characters selected. The section
  is hidden entirely when nothing is selected.
- The current cursor position (line and column number).

The filename and the unsaved-changes indicator are deliberately **not** shown in the status bar — the
title bar already carries both, and repeating them wastes the space.

Both the size and the character count reflect the document as it currently stands, including unsaved
edits, rather than the file as last written to disk.

- The character count charges each line break — however many raw characters it actually is —
  according to a user-chosen weight of 0, 1, or 2 characters, set from View → Line Breaks Count.
  The default is 2, matching a literal CRLF, so an existing document's displayed count is
  unaffected unless the user changes the setting.

## 10. Sample Documents

- A set of sample Markdown documents is installed alongside the application, including a
  `ReleaseNotes.md` that greets the user and doubles as the changelog. Further sample documents
  demonstrating supported Markdown constructs may be added *(planned)*.
- `ReleaseNotes.md` carries a "Recent changes" section describing what changed in each released version,
  as a bullet list per version, so a user who receives an automatic update can see what is new at a
  glance. It is updated as part of every release, and lists user-visible changes only.
- The samples are reachable at any time from Help → Release Notes, without the user needing to know
  where they were installed.
- The installed copy of `ReleaseNotes.md` is read-only, so it can't be overwritten by accident: attempting
  to save over it fails with a message pointing at Save As, which writes the edited copy to a location of
  the user's choosing instead. This only protects a single session's edits, not edits across an
  upgrade — the installed copy is replaced wholesale whenever the application updates regardless.

### First run

- `ReleaseNotes.md` opens automatically the first time the application is run after being installed, and
  again the first time it is run after being updated to a new version — so a returning user sees what has
  changed rather than the same greeting on every launch.
- It opens once per installed version, never on an ordinary launch.
- It opens as a normal, unmodified document: the user is not prompted to save it on exit unless they have
  actually edited it.
- If the application was launched to open a specific file — from a file association, a "send to", or a
  command-line argument — that file takes precedence and the release notes are not shown. The user's
  intent to open a particular document is never overridden.
