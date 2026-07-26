# Welcome to MDEdit

Thanks for installing MDEdit. This document is a short tour of what the editor does — and since it
is written in Markdown, it doubles as a live example. Try switching between **Source** and **WYSIWYG**
from the *View → Editor Mode* menu and watch this page change as you read it.

## What MDEdit is

MDEdit is a lightweight desktop editor for writing Markdown on Windows. It is built for people who
write in Markdown regularly and want a dedicated tool that stays out of the way: quick to open,
comfortable to type in, and aware enough of the syntax to make formatting effortless.

It is a *text* editor, not a publishing tool. Your document is always plain Markdown — nothing is
added, rewritten, or hidden in a proprietary format. What you save is exactly what you typed.

## Writing in MDEdit

### Two ways to see your document

MDEdit shows your work in one of two modes, switched from *View → Editor Mode*:

- **Source** shows the raw Markdown with syntax highlighting, so the structure of the document stays
  visible while you write.
- **WYSIWYG** shows the formatted result directly in the editor — larger headings, real bullets,
  indented quotes — while quietly revealing the underlying syntax around your cursor so it is always
  editable.

The second mode is the interesting one. There is no separate preview pane and no conversion step:
the Markdown text is still the document, still exactly what gets saved. Only its appearance changes.

### Formatting without memorising syntax

Every common construct has a command on the toolbar and in the menus. Select some text and the
command wraps it; use it with nothing selected and MDEdit inserts the syntax and places your cursor
ready to type.

Available formatting includes:

1. Bold, italic, strikethrough, and inline code
2. Headings, levels 1 through 3
3. Bullet and numbered lists
4. Blockquotes and fenced code blocks
5. Hyperlinks

> Formatting commands work in any open document, including plain `.txt` files — MDEdit will not stop
> you writing Markdown wherever you like.

## Making it yours

From the *View* menu you can:

- Choose a **theme** — Light, Dark, or System. System follows your Windows app theme and switches
  live when Windows does.
- Toggle **line numbers** on or off.
- Toggle **word wrap** on or off.

Your choices are remembered between sessions.

## A note on saving

MDEdit reads and writes **UTF-8** text, and nothing else. There is no project file, no sidecar
metadata, and no lock on your documents — they remain ordinary files you can edit anywhere.

If you close the application, open another file, or start a new document with unsaved changes,
MDEdit will ask before discarding your work.

## Recent changes

### Version 1.0.0.48

- **Underline.** Use the underlined **U** button on the toolbar, or *Format → Underline*. Markdown
  itself has no underline — underlining is conventionally reserved for links — so MDEdit inserts the
  HTML tags `<u>` and `</u>` around your text instead. That works anywhere HTML is allowed through,
  which covers most places Markdown is read, but a stricter reader may show the tags as plain text.
- **More familiar shortcuts**, matching the ones you already know from Word: **Ctrl+U** underlines,
  **Ctrl+Shift++** raises text, and **Ctrl+Shift+_** lowers it. **Ctrl+I** now italicises reliably —
  it was previously being intercepted by the editor.
- The toolbar's formatting buttons have been reordered into a more familiar sequence, and the Format
  menu now matches it.

### Version 1.0.0.47

- **Superscript and subscript.** Write `X^2^` for a raised character and `H~2~O` for a lowered one.
  Both appear raised or lowered and slightly smaller as you type, in either editor mode. Use the
  **X²** and **X₂** buttons on the toolbar, or *Format → Superscript* and *Format → Subscript*.

### Version 1.0.0.46

- **Highlight.** You can now mark text with a highlighter, using `==` on either side of it — so
  `==this==` comes out highlighted. Use the **H** button on the toolbar or *Format → Highlight*, either
  with text selected or on its own to start typing highlighted. The colour is a muted yellow that suits
  both the light and dark themes.

### Version 1.0.0.44

- **Recent Files.** The File menu now keeps the ten documents you opened most recently, under
  *File → Recent Files*. A document joins the list when you open it and when you save it somewhere
  new with Save As, and the list survives between sessions. Choosing an entry asks about unsaved
  work first, exactly as Open does. If a file has since been moved or deleted, MDEdit says so and
  offers to drop it from the list. You can clear the list at any time.

### Prior versions

Changes made before version 1.0.0.44 were not tracked, and are not listed here.

## Where to go next

- MDEdit updates itself. When a new version is published you will receive it automatically the next
  time you launch the application — and this page will tell you what changed.

---

*Feel free to edit this document — it is only an example. If you would like to keep your changes,
use **Save As** to store a copy somewhere of your own, since the installed original is replaced
whenever MDEdit updates.*

Happy writing.
