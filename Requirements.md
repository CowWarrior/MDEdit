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
- Task list item — `- [ ] todo` / `- [x] done`; the command inserts an unchecked item, and the user can toggle an existing item between checked and unchecked *(planned)*

### Insertions
- Hyperlink (inserts link syntax with placeholder text and URL)
- Table — inserts a starter table (header row, delimiter row, one body row) with the cursor placed in the first header cell. Column alignment via `:---` (left), `:---:` (center), and `---:` (right) is supported in the delimiter row *(planned)*
- Emoji — inserts an emoji shortcode such as `:joy:`. Shortcodes are drawn from a catalogue shipped
  with the application; a `:name:` that is not in the catalogue is ordinary text, so everyday writing
  such as `10:30:45` is never mistaken for an emoji
- Emoji picker — the user can browse and search the shipped catalogue and insert an emoji from it,
  without having to remember the shortcode name. Selecting an entry inserts its **shortcode**, not the
  literal character, so the document stays consistent with emoji typed by hand *(planned)*
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
- The remaining extended constructs listed in §3 (task lists, tables) are highlighted on the same terms as the core constructs *(planned)*.
- Underlined text is shown underlined, in both editor modes.

## 5. WYSIWYG Mode

- The user can toggle between a syntax-highlighted source view and a WYSIWYG view that displays the formatted output directly in the editor.
- The toggle is accessible from the View menu, under Editor Mode (Source / WYSIWYG).
- The chosen editor mode persists between sessions.
- In WYSIWYG mode the document's Markdown text remains the single source of truth — the mode changes only how that text is displayed. Nothing is rewritten, and switching modes never alters the document.
- Syntax markers are hidden rather than deleted: they still occupy their position in the document, so selection, undo, and the saved file are unaffected.
- Hidden syntax is revealed again around the cursor so it stays directly editable. The amount revealed suits the construct: the whole line for headings, blockquotes, and list items; the individual run for bold, italic, strikethrough, inline code, and links; and both fences of a fenced code block whenever the cursor is anywhere inside it.
- Constructs are rendered rather than merely stripped of their markers where that aids readability: headings display at a larger size per level, bullet items display a bullet glyph, and blockquotes are indented with a vertical accent bar spanning the quote, one bar per nesting level.
- A recognized emoji shortcode displays as the emoji character it stands for, reverting to the
  shortcode while the cursor is inside it. The character renders in monochrome rather than colour —
  a limitation of the underlying UI framework, not a defect in the editor.
- The remaining extended constructs listed in §3 (task lists, tables) are displayed in WYSIWYG mode on the same terms as the core constructs *(planned)*.
- Underlined text displays as underlined, with its `<u>` and `</u>` tags hidden, on the same terms as the other inline constructs.

## 6. View Options

- The user can toggle line numbers on or off.
- The user can toggle word wrap on or off.
- The user can choose the application theme: Light, Dark, or System (follows the Windows app theme, including live OS theme changes).
- The user can change the editor's display font via a Font item in the View menu *(planned)*.

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

A toolbar provides one-click access to the most common formatting operations: bold, italic, strikethrough, highlight, superscript, subscript, underline, headings 1–3, inline code, code block, link, bullet list, numbered list, and blockquote. File operations (new, open, save) and the word wrap toggle are available from the menu and keyboard shortcuts rather than the toolbar.

## 9. Status Bar

A status bar is always visible and displays:
- The current filename (or "Untitled" for unsaved documents).
- An indicator when the document has unsaved changes.
- The current cursor position (line and column number).
- The document's file size and character count *(planned)*.
- The character count can treat each line break as one character or as two (CR+LF) — either as a user-selectable option or by displaying both counts *(planned)*.
- When text is selected, an additional status bar section displays "Selected" and the number of characters selected *(planned)*.

## 10. Sample Documents

- A set of sample Markdown documents is installed alongside the application, including a `Welcome.md`
  introducing the product. Further sample documents demonstrating supported Markdown constructs may be
  added *(planned)*.
- `Welcome.md` carries a "Recent changes" section describing what changed in each released version, so a
  user who receives an automatic update can see what is new. It is updated as part of every release, and
  lists user-visible changes only.
- The samples are reachable at any time from the Help menu, without the user needing to know where they
  were installed *(planned)*.
- Editing a sample and saving it prompts for a new location rather than writing over the installed copy,
  because the installed copy is replaced whenever the application updates. The user's own edits are never
  silently lost to an upgrade *(planned)*.

### First run *(planned)*

- `Welcome.md` opens automatically the first time the application is run after being installed, and again
  the first time it is run after being updated to a new version — so a returning user sees what has
  changed rather than the same greeting on every launch.
- It opens once per installed version, never on an ordinary launch.
- It opens as a normal, unmodified document: the user is not prompted to save it on exit unless they have
  actually edited it.
- If the application was launched to open a specific file — from a file association, a "send to", or a
  command-line argument — that file takes precedence and the welcome document is not shown. The user's
  intent to open a particular document is never overridden.
