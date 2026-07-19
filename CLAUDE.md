# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

MDEdit is a WPF desktop Markdown editor (.NET, `net10.0-windows`) built around the AvalonEdit text editor control. See `Requirements.md` for the full product spec (document management, editing, Markdown formatting commands, syntax highlighting, keyboard shortcuts, toolbar, status bar). A WYSIWYG rendering mode is planned but not yet implemented — the View menu already has a disabled placeholder for it.

## Build & Run

```
dotnet build MDEdit.slnx
dotnet run --project MDEdit/MDEdit.csproj
```

There are no automated tests in this repo currently.

Release builds (`dotnet build -c Release` / `dotnet publish -c Release`) are Authenticode-signed automatically via an `AfterTargets="Build"` MSBuild target in `MDEdit.csproj` that runs `build/Sign.ps1`. It signs with whatever cert matches subject `CN=Maze Code Signing` in the CurrentUser store (matched by subject, not thumbprint, since the cert rotates yearly) and timestamps against `http://timestamp.digicert.com`. This requires network access and the cert being present — Release builds will fail without them. Debug builds are unaffected.

### ClickOnce deployment

`docs/` is a live GitHub Pages site (`https://cowwarrior.github.io/MDEdit/`) serving a ClickOnce deployment — this is why the repo is public rather than private. To publish a new version, run `build/Publish-ClickOnce.ps1`, then commit and push the changes under `docs/`.

- The script must be run standalone (not via `dotnet publish`): ClickOnce's `UpdateManifest` task only runs under the full-framework MSBuild bundled with Visual Studio (`MSBuild.exe`, located dynamically), not the cross-platform MSBuild `dotnet` uses (fails with MSB4803 otherwise).
- ClickOnce manifest signing (the separate XML-DSig signature over `MDEdit.application`/`*.manifest`, distinct from the Authenticode signing above) requires an RSA certificate — the same `CN=Maze Code Signing` cert is used, resolved by subject at publish time, never by thumbprint (it rotates).
- `ApplicationRevision` is derived from `git rev-list --count HEAD` rather than persisted anywhere, so it's always increasing as long as publishes happen after committing.
- The deploy is framework-dependent (`SelfContained=false`) — self-contained ClickOnce output is ~350MB, impractical to version in git. This means machines installing MDEdit need the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) already present; there's no bootstrapper to auto-install it.
- `MDEdit/Properties/PublishProfiles/ClickOnce.pubxml` holds the static settings (URLs, product/publisher name, `SelfContained=false`, etc). It deliberately omits `ManifestCertificateThumbprint` and `ApplicationRevision` — the script supplies both at publish time.
- `.gitattributes` marks everything under `docs/` as binary (`-text`) except `*.html` — git's CRLF normalization would otherwise corrupt the byte-exact hashes ClickOnce embeds in the manifest.

## Architecture

The app is a single-window WPF application; almost all logic lives directly in `MainWindow.xaml.cs` rather than being split across MVVM layers — there is no ViewModel.

- **`MainWindow.xaml` / `MainWindow.xaml.cs`** — the entire UI (menu, toolbar, status bar, editor) and its code-behind. Command routing uses standard WPF `RoutedUICommand`s (`ApplicationCommands.New/Open/Save/...`) plus custom ones declared as `static readonly RoutedUICommand` fields (`SaveAsCommand`, `BoldCommand`, `ItalicCommand`) for actions with no WPF built-in. Heading shortcuts (Ctrl+1/2/3) are wired via `InputBindings` + a local `RelayCommand` rather than `CommandBinding`, since there's no built-in command for them.
  - Formatting operations (bold/italic/strikethrough/code = `WrapSelection`; headings = `InsertHeading`; lists/blockquote = `InsertLinePrefix`; code block = `InsertCodeBlock`; links = `InsertLink`) all follow the same convention described in Requirements.md §3: wrap the selection if there is one, otherwise insert placeholder syntax and position the caret/selection for immediate typing.
  - Document dirty-state (`_isDirty`), title bar, and status bar text are kept in sync through `MarkDirty()` / `UpdateTitle()` / `UpdateStatusBar()`, called after edits, caret moves, and file operations.
  - `CheckUnsavedChanges()` is the single choke point for the "save/discard/cancel" prompt required before New, Open, and window Close.
- **`Services/FileService.cs`** — the only abstraction split out of the window: tracks `CurrentPath` and does the actual disk I/O (`LoadFile`, `Save`, `SaveAs`, `Reset`). Always reads/writes as UTF-8.
- **`MarkdownLineColorizer.cs`** — an AvalonEdit `DocumentColorizingTransformer` registered on `Editor.TextArea.TextView.LineTransformers`. Handles *line-level* styling that XSHD span/rule matching can't express well: heading color/weight by `#` count, blockquote italics, horizontal rules. This runs alongside (not instead of) the XSHD highlighting below.
- **`Resources/Markdown.xshd`** — AvalonEdit XSHD grammar for *inline* Markdown highlighting (bold/italic/strikethrough/inline code/links/images/fenced code blocks/HTML comments/list markers). Embedded as a resource (see `.csproj`) and loaded at runtime via `Assembly.GetManifestResourceStream("MDEdit.Resources.Markdown.xshd")` in `LoadSyntaxHighlighting()`. Comments in the file note deliberate regex constraints (e.g. "no wildcard quantifiers", "min 2 chars") — these were tuned to avoid catastrophic backtracking / crashes in AvalonEdit's regex engine, so preserve that style when editing rules (see commit "Fix syntax highlighting crashes and rearchitect line-level highlighting").
  - Highlighting is swapped based on file extension in `UpdateHighlighting(path)`: `.md`/`.markdown` get the Markdown XSHD, anything else (including `.txt`) gets no highlighting, per Requirements.md §4.

## Conventions

- Nullable reference types are enabled; keep new code nullable-aware.
- File-scoped namespaces (`namespace MDEdit;`) are used throughout.
- Keep formatting-insertion behavior consistent with the "wrap selection / insert-and-position-caret" pattern already established for existing commands when adding new ones.
