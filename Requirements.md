# MDEdit — Business Requirements

MDEdit is a desktop application for creating and editing Markdown documents. It targets users who write in Markdown regularly and want a dedicated, lightweight editor that stays out of the way: fast to open, comfortable to type in, and aware enough of Markdown syntax to make formatting effortless. The editor supports both Markdown and plain text files, offers real-time syntax highlighting to keep the structure of a document visible while writing, and provides a full set of formatting commands so common Markdown constructs can be inserted without remembering the exact syntax. A WYSIWYG mode — where the formatted output is displayed directly in the editor rather than the raw syntax — is planned for a future version.

## 1. Document Management

- The user can create a new empty document at any time.
- The user can open an existing Markdown file (`.md`, `.markdown`) or plain text file (`.txt`) from disk.
- The user can save the current document to its existing location.
- The user can save the current document to a new location and file type (Save As), choosing between Markdown and plain text formats.
- When there are unsaved changes and the user attempts to create a new document, open another file, or close the application, the user is prompted to save, discard, or cancel the operation.
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

### Block formatting
- Heading levels 1, 2, and 3
- Fenced code block
- Blockquote
- Bullet (unordered) list item
- Numbered (ordered) list item

### Insertions
- Hyperlink (inserts link syntax with placeholder text and URL)

## 4. Syntax Highlighting

- When a Markdown file is open, the editor visually distinguishes Markdown elements using color and style cues (e.g. headings, bold, italic, code, links, blockquotes, list markers).
- When a plain text file is open, no syntax highlighting is applied.
- Highlighting updates in real time as the user types.

## 5. WYSIWYG Mode *(planned)*

- The user can toggle between a syntax-highlighted source view and a rendered WYSIWYG view that displays the formatted output directly in the editor.
- The toggle is accessible from the View menu.

## 6. View Options

- The user can toggle line numbers on or off.
- The user can toggle word wrap on or off.

## 7. Keyboard Shortcuts

All common operations are accessible via keyboard shortcuts:

| Action | Shortcut |
|---|---|
| New | Ctrl+N |
| Open | Ctrl+O |
| Save | Ctrl+S |
| Save As | Ctrl+Shift+S |
| Bold | Ctrl+B |
| Italic | Ctrl+I |
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

A toolbar provides one-click access to the most common formatting operations: bold, italic, strikethrough, headings 1–3, inline code, code block, link, bullet list, numbered list, and blockquote. File operations (new, open, save) and the word wrap toggle are available from the menu and keyboard shortcuts rather than the toolbar.

## 9. Status Bar

A status bar is always visible and displays:
- The current filename (or "Untitled" for unsaved documents).
- An indicator when the document has unsaved changes.
- The current cursor position (line and column number).
