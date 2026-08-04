# MDEdit

A lightweight desktop Markdown editor for Windows, with live WYSIWYG rendering in the editor itself.

<!-- Screenshot goes here once captured. Suggested shot: WYSIWYG mode on a document with a heading,
     a list and a link, with the caret sitting on one formatted line so its raw Markdown is revealed
     while everything around it stays rendered — that single image explains the core idea below.
     Save it as docs/screenshot.png and uncomment:
![MDEdit in WYSIWYG mode](docs/screenshot.png)
-->

## What makes it different

Most Markdown editors either show you raw text, or show you a rendered HTML preview in a second
pane. MDEdit renders **inside the editor**: headings grow, links underline, tables become grids,
bullets become dots — while the caret's own line reverts to raw Markdown so you can edit the syntax
directly.

The important consequence is what *isn't* happening. There is no HTML view and no
`contenteditable` surface, so nothing is ever converted from HTML back into Markdown. The Markdown
text is the single source of truth at all times, and switching between Source and WYSIWYG changes
only how that text is drawn. A round-trip that can't happen can't lose your formatting.

## Install

Windows only.

**[Install MDEdit](https://cowwarrior.github.io/MDEdit/)**

Requires the [.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/10.0) to
already be present — the deployment is framework-dependent and carries no bootstrapper, so install
that first if you don't have it.

Installation is [ClickOnce](https://learn.microsoft.com/en-us/visualstudio/deployment/clickonce-security-and-deployment):
no administrator rights needed, and MDEdit checks for updates each time it launches. Builds are
Authenticode-signed.

To make MDEdit the default handler for `.md` and `.markdown` files, use **Help → Register File
Associations** after installing. This is deliberately manual rather than automatic.

## Markdown support

Headings (H1–H6) · bold · italic · bold+italic · strikethrough · highlight (`==text==`) ·
superscript (`^x^`) · subscript (`~x~`) · underline · inline code · fenced code blocks · links ·
images · bullet, numbered and task lists · nested blockquotes · tables with column alignment ·
horizontal rules · emoji shortcodes (`:rocket:`, around 240 in the built-in catalogue, with a
picker under Format → Emoji).

Local images render inline. Remote `http(s)` images are **off by default** and enabled under
**View → Load Remote Images** — with the setting off, MDEdit makes no network request for a
document's contents at all, so opening a file can never disclose to a third-party host that you
opened it.

Underline is the one deliberate departure from Markdown: no dialect defines an underline syntax, so
MDEdit emits literal `<u>` tags, relying on Markdown's pass-through of inline HTML.

## Known issues

- Switching the theme while the Find panel is open closes the panel, and it cannot be reopened.
  Restarting MDEdit restores it.
- A backslash does not escape formatting: `\*text\*` still renders as italic instead of showing the
  asterisks.
- An underscore inside a word starts italics, so a name like `snake_case_name` renders partly
  italicised. Use `*` for emphasis to avoid this.
- In WYSIWYG mode, moving the caret onto a blockquote reveals its `>` marker as intended, but the
  quote's vertical accent bar does not hide with it and can overlap the marker. Cosmetic only, and
  it corrects itself as soon as the caret leaves the line.
- Emoji display in black and white rather than colour. This is a limitation of the Windows text
  engine MDEdit draws with, not a setting.

## Planned

Specified and intended for a future release, with no date attached:

- **Convert emoji to shortcodes** — a command to replace literal emoji characters with their
  `:shortcode:` equivalents, across the selection or the whole document.
- **Find and replace** — replace matches one at a time or all at once, from the same panel as Find,
  with Ctrl+H. Replacing all counts as a single undo.
- **Formatting inside table cells** — bold, links and emoji inside a table will render in WYSIWYG
  mode instead of showing their raw markers.
- **Improved Markdown conformance** — better support for niche Markdown features and edge cases.

## Built with

- **C# on .NET 10** (`net10.0-windows`), **WPF** — Fluent theming, with Light/Dark/System modes
- **[AvalonEdit](https://github.com/icsharpcode/AvalonEdit) 6.3.0.90** — the text editor control
  everything is built on
- **xUnit** — 575 tests over the editing logic, deliberately factored out of the window to be
  testable without one
- **ClickOnce**, published to GitHub Pages from `docs/`

## Architecture

A single-window WPF application with no MVVM layer — most UI logic lives in `MainWindow.xaml.cs`,
and the parts worth testing are pulled out into UI-free classes under `MDEdit/Editing/`.

Two pieces are worth knowing about before reading the code:

- **Live preview** is built from AvalonEdit `VisualLineElementGenerator`s, one per Markdown
  construct. Each hides or replaces the syntax markers in a line while the caret is elsewhere —
  headings, links, tables, blockquotes and the rest each pick a *reveal scope* (per-line, per-span
  or per-block) and a *replacement* (hide, spacer, or a rendered element). The characters always
  keep their real document offsets, so selection, undo and the saved file are untouched.
- **Syntax highlighting** comes from an AvalonEdit XSHD grammar (`Resources/Markdown.xshd`) that
  carries no colours of its own; every colour, weight and font is written onto it at load time from
  the user's preferences, for each of light/dark × source/WYSIWYG.

[`CLAUDE.md`](CLAUDE.md) documents the architecture in full, including the rationale behind the
decisions above and the ones that were rejected. [`Requirements.md`](Requirements.md) is the
product specification.

## Building from source

Requires the .NET 10 SDK on Windows.

```
dotnet build MDEdit.slnx
dotnet test MDEdit.slnx
dotnet run --project MDEdit/MDEdit.csproj
```

Note that **Release** builds are Authenticode-signed by an MSBuild target and will fail without the
signing certificate and network access. Debug builds are unaffected, so build and test normally
while developing.

## Licence

MDEdit is released under the [0BSD licence](LICENSE) — use, copy, modify and distribute it for any
purpose, with or without fee. No attribution required, no conditions attached.

That applies to MDEdit's own code only. Two third-party components are redistributed with it and
keep their own (MIT) terms, reproduced in full in [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md),
which also ships with the installed application:

- [AvalonEdit](https://github.com/icsharpcode/AvalonEdit) by the SharpDevelop team — the text editor
  control, redistributed as a DLL
- [Tabler Icons](https://tabler.io/icons) — the toolbar iconography, vendored as path geometry
