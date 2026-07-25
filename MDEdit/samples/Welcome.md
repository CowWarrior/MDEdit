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

### Code, kept readable

Fenced code blocks are highlighted and left strictly alone, so nothing inside them is reinterpreted
as formatting:

```csharp
public static string Greet(string name)
{
    return $"Hello, {name}";
}
```

## Keyboard shortcuts

The operations you will reach for most:

- **Ctrl+N** — new document
- **Ctrl+O** — open
- **Ctrl+S** — save
- **Ctrl+Shift+S** — save as
- **Ctrl+B** — bold
- **Ctrl+I** — italic
- **Ctrl+1**, **Ctrl+2**, **Ctrl+3** — heading levels 1 to 3
- **Ctrl+F** — find
- **Ctrl+Z** / **Ctrl+Y** — undo and redo

The usual **Ctrl+X**, **Ctrl+C**, **Ctrl+V**, and **Ctrl+A** behave exactly as you would expect.

## Making it yours

From the *View* menu you can:

- Choose a **theme** — Light, Dark, or System. System follows your Windows app theme and switches
  live when Windows does.
- Toggle **line numbers** on or off.
- Toggle **word wrap** on or off.

Your choices are remembered between sessions.

### Opening Markdown files by double-clicking

MDEdit can register itself as the handler for `.md` and `.markdown` files, and offer itself as an
"Open with" choice for `.txt`. This is not done automatically — choose *Help → Register File
Associations* when you want it. Your existing `.txt` default is deliberately left alone.

## A note on saving

MDEdit reads and writes **UTF-8** text, and nothing else. There is no project file, no sidecar
metadata, and no lock on your documents — they remain ordinary files you can edit anywhere.

If you close the application, open another file, or start a new document with unsaved changes,
MDEdit will ask before discarding your work.

## Where to go next

- The `samples` folder installed alongside MDEdit contains further example documents.
- MDEdit updates itself. When a new version is published you will receive it automatically the next
  time you launch the application.

---

*Feel free to edit this document — it is only an example. If you would like to keep your changes,
use **Save As** to store a copy somewhere of your own, since the installed original is replaced
whenever MDEdit updates.*

Happy writing.
