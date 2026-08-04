using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Search;
using MDEdit.Editing;
using MDEdit.Services;
using Microsoft.Win32;

namespace MDEdit;

public partial class MainWindow : Window
{
    // ── Custom routed commands ────────────────────────────────────────────
    // Requirements.md §1/§7. Ctrl+Shift+N follows VS Code's New Window. Nothing needs releasing for
    // it: AvalonEdit's own gestures are Ctrl+D/I/U, Ctrl+Shift+U and Insert (see
    // ReleaseConflictingEditorGestures), and while WPF's EditingCommands.ToggleNumbering does carry
    // Ctrl+Shift+N, its gesture is registered as a class input binding on TextBoxBase — which
    // AvalonEdit's TextArea is not — so it never resolves against this editor.
    public static readonly RoutedUICommand NewWindowCommand = new(
        "New Window", "NewWindow", typeof(MainWindow),
        [new KeyGesture(Key.N, ModifierKeys.Control | ModifierKeys.Shift)]);

    public static readonly RoutedUICommand SaveAsCommand = new(
        "Save As", "SaveAs", typeof(MainWindow),
        [new KeyGesture(Key.S, ModifierKeys.Control | ModifierKeys.Shift)]);

    public static readonly RoutedUICommand BoldCommand = new(
        "Bold", "Bold", typeof(MainWindow),
        [new KeyGesture(Key.B, ModifierKeys.Control)]);

    public static readonly RoutedUICommand ItalicCommand = new(
        "Italic", "Italic", typeof(MainWindow),
        [new KeyGesture(Key.I, ModifierKeys.Control)]);

    // ── State ─────────────────────────────────────────────────────────────
    private readonly FileService _files = new();
    private readonly AppSettings _settings = SettingsService.Load();
    private readonly MarkdownLineColorizer _colorizer = new();
    private readonly HeadingMarkerElementGenerator _headingMarkerGenerator = new();
    private readonly EmphasisMarkerElementGenerator _emphasisMarkerGenerator = new();
    private readonly CodeBlockFenceElementGenerator _codeBlockFenceGenerator = new();
    private readonly ImageElementGenerator _imageGenerator = new();
    private readonly LinkMarkerElementGenerator _linkMarkerGenerator = new();
    private readonly UnderlineMarkerElementGenerator _underlineMarkerGenerator = new();
    private readonly EmojiElementGenerator _emojiGenerator = new();
    private readonly BlockquoteMarkerElementGenerator _blockquoteMarkerGenerator = new();
    private readonly BulletListMarkerElementGenerator _bulletListMarkerGenerator = new();
    private readonly TaskListMarkerElementGenerator _taskListMarkerGenerator = new();
    private readonly NumberedListMarkerElementGenerator _numberedListMarkerGenerator = new();
    private readonly TableRowElementGenerator _tableRowGenerator = new();
    private readonly HorizontalRuleElementGenerator _horizontalRuleGenerator = new();
    private readonly BlockquoteAccentBarRenderer _blockquoteAccentBarRenderer; // needs _colorizer, so built in the ctor
    private readonly TableGridRenderer _tableGridRenderer; // needs _tableRowGenerator, so built in the ctor
    private readonly HorizontalRuleRenderer _horizontalRuleRenderer; // needs _horizontalRuleGenerator, so built in the ctor
    // WYSIWYG renders prose in a document font; source mode (and the XSHD code colors) use the
    // code font — both come from AppSettings.EditorPreferences (Requirements.md §6) and are
    // (re)derived by ApplyEditorPreferences, not compile-time constants, so Preferences can change
    // them live. The XAML FontFamily on the editor is only the pre-ApplyEditorPreferences fallback.
    private FontFamily _wysiwygFontFamily = new("Arial");
    private FontFamily _sourceFontFamily = new("Cascadia Code, Consolas, Courier New");
    private bool _isDirty;
    private int _lastCaretLine = -1;
    private int _lastCaretOffset = -1;
    // Ctrl+wheel zoom coalescing — see Editor_PreviewMouseWheel. The pending level is what the
    // readout already shows; the flag keeps a burst of notches to one queued apply.
    private double? _pendingZoomLevel;
    private bool _zoomApplyQueued;
    // Indexed [dark ? 1 : 0, wysiwyg ? 1 : 0]. Four rather than the previous two, because
    // per-element styling (Requirements.md §6) differs by editor mode as well as by theme — a
    // heading may be large and proportional in WYSIWYG and plain mono in source. Rebuilt whole by
    // ApplyEditorPreferences; see MarkdownHighlighting.
    private readonly IHighlightingDefinition?[,] _markdown = new IHighlightingDefinition?[2, 2];
    private SearchPanel? _searchPanel;

    // ── Constructor ───────────────────────────────────────────────────────
    public MainWindow()
    {
        _tableGridRenderer = new TableGridRenderer(_tableRowGenerator);
        _horizontalRuleRenderer = new HorizontalRuleRenderer(_horizontalRuleGenerator, _colorizer);
        _blockquoteAccentBarRenderer = new BlockquoteAccentBarRenderer(_colorizer);
        InitializeComponent();
        ApplyEditorPreferences(); // fonts, span colors, and the compiled highlighting definitions
        Editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        Editor.TextArea.TextView.ElementGenerators.Add(_headingMarkerGenerator);
        Editor.TextArea.TextView.ElementGenerators.Add(_emphasisMarkerGenerator);
        Editor.TextArea.TextView.ElementGenerators.Add(_codeBlockFenceGenerator);
        // The image generator must precede the link generator: both advertise an image span's
        // Start offset, and registration order breaks the tie — a rendered image consumes the
        // whole span, while a declined one falls through to the link generator's marker hiding.
        Editor.TextArea.TextView.ElementGenerators.Add(_imageGenerator);
        Editor.TextArea.TextView.ElementGenerators.Add(_linkMarkerGenerator);
        Editor.TextArea.TextView.ElementGenerators.Add(_underlineMarkerGenerator);
        Editor.TextArea.TextView.ElementGenerators.Add(_emojiGenerator);
        Editor.TextArea.TextView.ElementGenerators.Add(_blockquoteMarkerGenerator);
        Editor.TextArea.TextView.ElementGenerators.Add(_taskListMarkerGenerator);
        Editor.TextArea.TextView.ElementGenerators.Add(_bulletListMarkerGenerator);
        Editor.TextArea.TextView.ElementGenerators.Add(_numberedListMarkerGenerator);
        Editor.TextArea.TextView.ElementGenerators.Add(_tableRowGenerator);
        Editor.TextArea.TextView.ElementGenerators.Add(_horizontalRuleGenerator);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_blockquoteAccentBarRenderer);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_tableGridRenderer);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_horizontalRuleRenderer);
        // A remote image's bitmap arrives long after ConstructElement returned, and AvalonEdit
        // captures an inline object's DesiredSize at construction time — so a late arrival can't
        // resize its own line. The loader asks for a redraw instead, which re-runs
        // ConstructElement against the now-populated cache. Pushed from here rather than captured
        // from CurrentContext.TextView: that's the imperative-property-push convention every
        // other generator already follows, and CurrentContext is non-null only during a
        // construction pass. Completions are coalesced inside the loader, so a burst of finished
        // images costs one full redraw, not one each.
        _imageGenerator.RequestRedraw = () => Editor.TextArea.TextView.Redraw();
        RegisterCommands();
        RegisterKeyBindings();
        // SearchPanel.AttachInternal already registers CommandBindings for FindNext/FindPrevious/
        // CloseSearchPanel directly on the panel itself, so its own buttons work without this —
        // RegisterCommands additionally exposes those same commands on the window's own
        // CommandBindings, for if MDEdit ever wants to trigger Find from outside the panel (e.g. a
        // future Edit menu entry).
        _searchPanel = SearchPanel.Install(Editor);
        _searchPanel.RegisterCommands(CommandBindings);
        // IsVisibleChanged, not Loaded/ContentRendered: SearchPanel.Open()/Close() (AvalonEdit's own
        // source) add/remove the panel's adorner from the TextArea's AdornerLayer on demand, and it
        // starts closed — the adorner isn't connected to a live visual tree until the user's first
        // Ctrl+F, so anything pushed before that doesn't survive the later, first real connection.
        // See ApplySearchPanelColors and CLAUDE.md's SearchPanelStyle.xaml entry for the full story.
        _searchPanel.IsVisibleChanged += (_, _) =>
        {
            if (_searchPanel.IsVisible) ApplySearchPanelColors();
        };
        ApplySettings();

        Editor.TextArea.Caret.PositionChanged += (_, _) => OnCaretPositionChanged();
        // Selection size is shown in the status bar, and a selection can change without the caret
        // moving (Select All from an already-current caret, or a selection being cleared), so this
        // is hooked in addition to PositionChanged rather than relying on it.
        Editor.TextArea.SelectionChanged += (_, _) => UpdateStatusBar();
        Editor.TextChanged += (_, _) =>
        {
            MarkDirty();
            UpdateDocumentStats();
        };
        UpdateDocumentStats();

        // Follow OS light/dark switches live while the theme setting is System.
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        // args[0] is the exe path itself; a file path argument (from double-clicking an associated
        // file, or "Open with") is args[1], per the "MDEdit.exe" "%1" command FileAssociationService registers.
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1)
            OpenFile(args[1]);
        else
            MaybeShowReleaseNotesOnFirstRun();
    }

    // ── Syntax highlighting ───────────────────────────────────────────────

    private void LoadSyntaxHighlighting()
    {
        var p = _settings.EditorPreferences;
        foreach (bool wysiwyg in new[] { false, true })
        {
            // Scaled, so zoom reaches the compiled definitions too — see ActiveModeStyles.
            var mode = (wysiwyg ? p.Wysiwyg : p.Source).Scaled(_settings.ZoomLevel);
            foreach (bool dark in new[] { false, true })
                _markdown[dark ? 1 : 0, wysiwyg ? 1 : 0] = MarkdownHighlighting.Build(mode, dark);
        }

        // UpdateLivePreviewState/ApplyTheme pick the real one; this is only so the editor is never
        // left holding a definition from a previous, now-discarded build.
        Editor.SyntaxHighlighting = Definition(dark: false, wysiwyg: false);
    }

    private IHighlightingDefinition Definition(bool dark, bool wysiwyg)
        => _markdown[dark ? 1 : 0, wysiwyg ? 1 : 0]!;

    // The single entry point for AppSettings.EditorPreferences (Requirements.md §6): rebuilds the
    // WYSIWYG/code FontFamily objects, pushes every span color into the colorizer/renderer
    // instances (the same "MainWindow fans state out" pattern ApplyTheme already uses for IsDark),
    // and recompiles the highlighting definitions (LoadSyntaxHighlighting reads the same settings
    // through LoadDefinition). Self-contained — also reapplies the theme/live-preview font
    // selection at the end — so every caller (the constructor, and later the Preferences window's
    // live-apply callback) gets a fully correct redraw without having to remember the right order.
    private void ApplyEditorPreferences()
    {
        var p = _settings.EditorPreferences;

        _wysiwygFontFamily = new FontFamily(p.Wysiwyg.BaseFontFamily);
        _sourceFontFamily  = new FontFamily(p.Source.BaseFontFamily);
        _colorizer.SourceFontFamily = _sourceFontFamily;

        LoadSyntaxHighlighting();
        UpdateLivePreviewState();
        ApplyTheme();
    }

    /// <summary>
    /// The active editor mode's styling <b>as rendered</b> — the stored preferences with zoom
    /// applied (Requirements.md §6).
    /// </summary>
    /// <remarks>
    /// Zoom lives here, at the single point every rendering path already reads its styles from, so
    /// scaling one number reaches all of them: <c>Editor.FontSize</c>, the colorizer, the list-marker
    /// styles and (via <see cref="LoadSyntaxHighlighting"/>) the four compiled definitions.
    /// <para>
    /// <b>PreferencesWindow deliberately does not come through here</b> — it reads
    /// <c>_settings.EditorPreferences</c> directly, so it keeps editing and displaying the
    /// <i>configured</i> sizes rather than zoomed ones, which is what makes zoom non-destructive.
    /// </para>
    /// </remarks>
    private ModeStyles ActiveModeStyles()
        => (_settings.LivePreview ? _settings.EditorPreferences.Wysiwyg : _settings.EditorPreferences.Source)
            .Scaled(_settings.ZoomLevel);

    /// <summary>
    /// Points everything that styles text from code at the editor mode currently in effect
    /// (Requirements.md §6). Called whenever either input can have changed — the preferences
    /// themselves, the editor mode, or the theme.
    /// </summary>
    /// <remarks>
    /// The list generators need this pushed in because they <i>replace</i> the marker with a drawn
    /// element, so the XSHD colour that styles the raw "-" never reaches them — the same
    /// imperative-property-push convention every other generator follows. Theme is read here rather
    /// than taken from <c>_colorizer.IsDark</c> so this doesn't depend on running after ApplyTheme.
    /// </remarks>
    private void ApplyActiveModeStyles()
    {
        var mode = ActiveModeStyles();
        var dark = ThemeService.IsDarkEffective(ThemeService.Parse(_settings.Theme));

        _colorizer.Styles = mode;

        var marker = StyleResolver.Resolve(StyledElements.ListMarker, mode, dark);
        _bulletListMarkerGenerator.MarkerStyle = marker;
        _numberedListMarkerGenerator.MarkerStyle = marker;

        ApplyNormalTextStyle(mode, dark);
    }

    /// <summary>
    /// Applies the <c>normal</c> element — default body text — to the editor control itself.
    /// </summary>
    /// <remarks>
    /// The one element that isn't a Markdown construct, so it goes through neither construct-styling
    /// path: it is simply the editor's own font properties. Family and size are set by
    /// <see cref="UpdateLivePreviewState"/> from the mode's base, since a size expressed as a
    /// multiplier of itself would be circular; only weight, italic and foreground come from the
    /// element's style.
    /// <para>
    /// Decoration and background are deliberately not offered for it. Underlining or striking every
    /// line of body text would need a whole-document colorizer pass, which would fight the per-line
    /// construct styling that returns early; and the background here is the editor's own surface,
    /// already owned by View → Theme.
    /// </para>
    /// <para>
    /// An unset foreground restores the theme's <c>DynamicResource</c> rather than clearing the
    /// property: <c>MainWindow.xaml</c> sets it as a local dynamic reference, so assigning a plain
    /// brush would otherwise pin the colour and silently stop it tracking the theme for good.
    /// </para>
    /// </remarks>
    private void ApplyNormalTextStyle(ModeStyles mode, bool dark)
    {
        var normal = StyleResolver.Resolve(StyledElements.Normal, mode, dark);

        Editor.FontWeight = normal.Weight ?? FontWeights.Normal;
        Editor.FontStyle = normal.Style ?? FontStyles.Normal;

        if (normal.Foreground is SolidColorBrush foreground)
            Editor.Foreground = foreground;
        else
            Editor.SetResourceReference(ForegroundProperty, "EditorForegroundBrush");
    }

    private void UpdateHighlighting(string? path)
    {
        var ext = path is null ? ".md" : Path.GetExtension(path).ToLowerInvariant();
        var dark = ThemeService.IsDarkEffective(ThemeService.Parse(_settings.Theme));
        Editor.SyntaxHighlighting = ext is ".md" or ".markdown"
            ? Definition(dark, _settings.LivePreview)
            : null;

        // Images resolve relative to the document's own directory — the one generator fed
        // anything from FileService. Set here because this method is already the single funnel
        // for every path change (New/Open/SaveAs; ApplyTheme re-passes the unchanged path,
        // harmlessly under the guard). The redraw matters for Save As: giving an unsaved
        // document a directory makes its relative images resolvable, and nothing else
        // repaints then.
        var documentDirectory = path is null ? null : Path.GetDirectoryName(path);
        if (!string.Equals(_imageGenerator.DocumentDirectory, documentDirectory, StringComparison.OrdinalIgnoreCase))
        {
            _imageGenerator.DocumentDirectory = documentDirectory; // setter clears the bitmap cache
            Editor.TextArea.TextView.Redraw();
        }
    }

    // ── Command bindings ──────────────────────────────────────────────────
    private void RegisterCommands()
    {
        CommandBindings.Add(new CommandBinding(ApplicationCommands.New,
            (_, _) => NewDocument(), AlwaysCanExecute));
        CommandBindings.Add(new CommandBinding(NewWindowCommand,
            (_, _) => NewWindow(), AlwaysCanExecute));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Open,
            (_, _) => OpenDocument(), AlwaysCanExecute));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save,
            (_, _) => ExecuteSave(), AlwaysCanExecute));
        CommandBindings.Add(new CommandBinding(SaveAsCommand,
            (_, _) => ExecuteSaveAs(), AlwaysCanExecute));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Close,
            (_, _) => Close(), AlwaysCanExecute));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo,
            (_, _) => Editor.Undo(),
            (_, e) => e.CanExecute = Editor.CanUndo));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Redo,
            (_, _) => Editor.Redo(),
            (_, e) => e.CanExecute = Editor.CanRedo));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Cut,
            (_, _) => Editor.Cut(), AlwaysCanExecute));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy,
            (_, _) => Editor.Copy(), AlwaysCanExecute));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste,
            (_, _) => Editor.Paste(), AlwaysCanExecute));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.SelectAll,
            (_, _) => Editor.SelectAll(), AlwaysCanExecute));
        CommandBindings.Add(new CommandBinding(BoldCommand,
            (_, _) => WrapSelection("**", "**"), AlwaysCanExecute));
        CommandBindings.Add(new CommandBinding(ItalicCommand,
            (_, _) => WrapSelection("_", "_"), AlwaysCanExecute));
    }

    // AvalonEdit declares gestures on its own editing commands: Ctrl+I runs IndentSelection and
    // Ctrl+U runs ConvertToLowercase. Those resolve against the TextArea, which is nearer the
    // keyboard focus than this window, so they consumed the keystroke and MDEdit's Italic and
    // Underline never fired — Bold (Ctrl+B) worked only because AvalonEdit claims no Ctrl+B.
    // Neither AvalonEdit command is exposed anywhere in MDEdit's UI, so dropping their gestures
    // costs nothing. Do this before the bindings below are registered.
    private static void ReleaseConflictingEditorGestures()
    {
        AvalonEditCommands.IndentSelection.InputGestures.Clear();
        AvalonEditCommands.ConvertToLowercase.InputGestures.Clear();
    }

    // Shortcuts for commands with no WPF built-in and no RoutedUICommand of their own.
    private void RegisterKeyBindings()
    {
        ReleaseConflictingEditorGestures();

        // Underline follows Word's Ctrl+U, freed up just above.
        InputBindings.Add(new KeyBinding(
            new RelayCommand(() => WrapSelection("<u>", "</u>")), Key.U, ModifierKeys.Control));

        // Subscript/superscript follow Word: Ctrl+Shift+_ and Ctrl+Shift++. Both of those
        // characters are Shift-ed already, so the underlying keys are OemMinus and OemPlus.
        // Menu InputGestureText is set by hand in XAML — WPF only fills that in automatically for
        // a RoutedUICommand's own gestures, not for InputBindings like these.
        InputBindings.Add(new KeyBinding(
            new RelayCommand(() => WrapSelection("~", "~")), Key.OemMinus, ModifierKeys.Control | ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(
            new RelayCommand(() => WrapSelection("^", "^")), Key.OemPlus, ModifierKeys.Control | ModifierKeys.Shift));

        InputBindings.Add(new KeyBinding(
            new RelayCommand(() => InsertHeading(1)), Key.D1, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(
            new RelayCommand(() => InsertHeading(2)), Key.D2, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(
            new RelayCommand(() => InsertHeading(3)), Key.D3, ModifierKeys.Control));

        // Zoom (Requirements.md §6). Nothing needs releasing for these — AvalonEdit's own gestures
        // are Ctrl+D/I/U, Ctrl+Shift+U and Insert. Note these are Ctrl WITHOUT Shift: the
        // Ctrl+Shift+OemMinus/OemPlus pair just above is subscript/superscript, which is a different
        // gesture and must keep working. The numpad duplicates are registered because a keyboard's
        // +/-/0 there are distinct keys, and users reach for whichever is nearer.
        foreach (var key in new[] { Key.OemPlus, Key.Add })
            InputBindings.Add(new KeyBinding(new RelayCommand(ZoomIn), key, ModifierKeys.Control));
        foreach (var key in new[] { Key.OemMinus, Key.Subtract })
            InputBindings.Add(new KeyBinding(new RelayCommand(ZoomOut), key, ModifierKeys.Control));
        foreach (var key in new[] { Key.D0, Key.NumPad0 })
            InputBindings.Add(new KeyBinding(new RelayCommand(ZoomReset), key, ModifierKeys.Control));
    }

    private static void AlwaysCanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = true;

    // ── File operations ───────────────────────────────────────────────────
    private void NewDocument()
    {
        if (!CheckUnsavedChanges()) return;
        Editor.Document.Text = string.Empty;
        _files.Reset();
        _isDirty = false;
        UpdateHighlighting(null);
        ResetLivePreviewCaretTracking();
        UpdateTitle();
        UpdateStatusBar();
    }

    // Requirements.md §1: a second editor window, deliberately a second *process* rather than
    // another Window inside this one. That is the mechanism MDEdit already ships — double-clicking
    // an associated file runs "MDEdit.exe" "%1" (see FileAssociationService) — so it is proven here,
    // and it makes each window independent for free: its own document, its own unsaved-changes
    // prompt, its own AppSettings, and closing one leaves the others alone.
    //
    // An in-process Window was rejected because it would split the theme in half. ThemeService.Apply
    // sets Application.ThemeMode and swaps the app's merged dictionaries *process-wide*, while the
    // rest of ApplyTheme — the colorizer's IsDark, the active highlighting definition, the caret
    // brush, the Highlight swatch — is per-window. Switching theme in one window would therefore
    // re-chrome the other's menus and toolbar while leaving its document text on the old palette.
    // Fixing that properly means one shared AppSettings plus a changed-broadcast every window
    // subscribes to, which is a settings refactor rather than this feature.
    //
    // The accepted cost of separate processes: settings.json stays last-writer-wins between
    // windows, so a View or Preferences change in one window can be overwritten by the other's next
    // save and does not reach it until reopened. That is not a regression — it is exactly how two
    // MDEdits already behave today when a second one is launched from Explorer.
    private void NewWindow()
    {
        // The same path source FileAssociationService registers. Under ClickOnce this resolves to
        // the real exe inside the current version's folder, so launching it directly is what
        // Explorer already does; it skips ClickOnce's update check, which is right for a second
        // window — this process made that check when it started.
        if (Environment.ProcessPath is not string exePath)
        {
            MessageBox.Show("Could not determine where MDEdit is running from, so a new window could not be opened.",
                "MDEdit", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        try
        {
            // UseShellExecute = false runs the executable directly instead of routing it through the
            // shell. Disposing the returned Process only releases this process's handle to it — the
            // new window goes on running independently, and outlives this one.
            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = false })?.Dispose();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open a new window:\n\n{ex.Message}",
                "MDEdit", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenDocument()
    {
        if (!CheckUnsavedChanges()) return;

        var dlg = new OpenFileDialog
        {
            Filter = "All supported files (*.md;*.markdown;*.txt)|*.md;*.markdown;*.txt|Markdown files (*.md;*.markdown)|*.md;*.markdown|Text files (*.txt)|*.txt|All files (*.*)|*.*",
            DefaultExt = ".md"
        };
        if (dlg.ShowDialog() != true) return;

        OpenFile(dlg.FileName);
    }

    // addToRecentFiles/showErrorOnFailure default to normal Open semantics; MaybeShowReleaseNotesOnFirstRun
    // passes both false, since an automatic first-run open is not a user-initiated action — it must
    // fail silently and shouldn't clutter the MRU list the way a deliberate Open does.
    private void OpenFile(string path, bool addToRecentFiles = true, bool showErrorOnFailure = true)
    {
        try
        {
            Editor.Document.Text = _files.LoadFile(path);
            _isDirty = false;
            UpdateHighlighting(path);
            ResetLivePreviewCaretTracking();
            UpdateTitle();
            UpdateStatusBar();
            if (addToRecentFiles)
                AddToRecentFiles(path); // only on success — a file that failed to open isn't "recent"
        }
        catch (Exception ex)
        {
            if (showErrorOnFailure)
                MessageBox.Show($"Could not open file:\n{ex.Message}", "MDEdit",
                    MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Release notes (Requirements.md §10) ──────────────────────────────────
    // MDEdit.csproj ships samples\ReleaseNotes.md as Content beside the exe, and ClickOnce
    // preserves that relative layout in its deployed version folder, so this path is correct in
    // both a normal build and an installed one.
    private static string GetReleaseNotesPath() => Path.Combine(AppContext.BaseDirectory, "samples", "ReleaseNotes.md");

    // Applied at open time rather than baked in at publish time: whether ClickOnce's deploy
    // mechanism (which renames payload files to *.deploy and reconstructs them on the client)
    // preserves OS file attributes is unverified and not worth depending on. Doing it here is
    // idempotent and self-heals after every update, since each new version lands in a fresh
    // folder with default attributes. Best-effort — a failure to mark it read-only shouldn't
    // block opening the file for reading.
    private static void EnsureReleaseNotesAreReadOnly(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) == 0)
                File.SetAttributes(path, attributes | FileAttributes.ReadOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // Called once from the constructor when there's no file-association/command-line argument
    // to open instead (that always takes precedence — Requirements.md §10). Shows the page once
    // per released version: the "last shown" value is recorded BEFORE attempting to open, so a
    // missing or unreadable file fails silently exactly once per release rather than retrying on
    // every launch. ReleaseNotesGate compares only Major.Minor.Build (the release), never the
    // full version or Revision alone — see its comments for why.
    private void MaybeShowReleaseNotesOnFirstRun()
    {
        var release = ReleaseNotesGate.GetReleaseVersion(Assembly.GetExecutingAssembly().GetName().Version);
        if (release is null || !ReleaseNotesGate.ShouldShow(_settings.LastReleaseNotesVersionShown, release)) return;

        _settings.LastReleaseNotesVersionShown = release;
        SettingsService.Save(_settings);

        var path = GetReleaseNotesPath();
        if (!File.Exists(path)) return;

        EnsureReleaseNotesAreReadOnly(path);
        // Not a user-initiated Open: no error dialog on failure, and it doesn't join Recent Files.
        OpenFile(path, addToRecentFiles: false, showErrorOnFailure: false);
    }

    private void MenuOpenReleaseNotes_Click(object sender, RoutedEventArgs e)
    {
        if (!CheckUnsavedChanges()) return;

        var path = GetReleaseNotesPath();
        if (!File.Exists(path))
        {
            MessageBox.Show("The release notes document could not be found.", "MDEdit",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        EnsureReleaseNotesAreReadOnly(path);
        OpenFile(path); // a deliberate Open: joins Recent Files and shows errors normally
    }

    // ── Recent files (MRU) ────────────────────────────────────────────────
    // The list itself lives in AppSettings (persisted); Editing/RecentFiles owns the list logic.

    private void AddToRecentFiles(string path)
    {
        _settings.RecentFiles = RecentFiles.Add(_settings.RecentFiles, path);
        SettingsService.Save(_settings);
        RebuildRecentFilesMenu();
    }

    private void RebuildRecentFilesMenu()
    {
        MenuRecentFiles.Items.Clear();

        var paths = _settings.RecentFiles;
        if (paths.Count == 0)
        {
            MenuRecentFiles.Items.Add(new MenuItem { Header = "(No recent files)", IsEnabled = false });
            return;
        }

        for (var i = 0; i < paths.Count; i++)
        {
            var item = new MenuItem
            {
                // Access keys 1-9 then 0 for a tenth entry. Doubling underscores escapes them:
                // WPF consumes a single "_" in a header as an access-key marker, which would
                // otherwise mangle the display of any file named like "my_notes.md".
                Header = $"_{(i + 1) % 10} {Path.GetFileName(paths[i]).Replace("_", "__")}",
                ToolTip = paths[i],   // full path — file names alone are often ambiguous
                Tag = paths[i]
            };
            item.Click += RecentFile_Click;
            MenuRecentFiles.Items.Add(item);
        }

        MenuRecentFiles.Items.Add(new Separator());
        var clear = new MenuItem { Header = "_Clear Recent Files" };
        clear.Click += ClearRecentFiles_Click;
        MenuRecentFiles.Items.Add(clear);
    }

    private void RecentFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string path })
            OpenRecentFile(path);
    }

    private void OpenRecentFile(string path)
    {
        // Checked before CheckUnsavedChanges deliberately: a stale entry shouldn't make the user
        // answer a save prompt for a file that turns out not to be there.
        if (!File.Exists(path))
        {
            var result = MessageBox.Show(
                $"'{path}' could not be found. It may have been moved, renamed, or deleted.\n\n" +
                "Remove it from the recent files list?", "MDEdit",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                _settings.RecentFiles = RecentFiles.Remove(_settings.RecentFiles, path);
                SettingsService.Save(_settings);
                RebuildRecentFilesMenu();
            }
            return;
        }

        if (!CheckUnsavedChanges()) return;
        OpenFile(path);
    }

    private void ClearRecentFiles_Click(object sender, RoutedEventArgs e)
    {
        _settings.RecentFiles = [];
        SettingsService.Save(_settings);
        RebuildRecentFilesMenu();
    }

    private bool ExecuteSave()
    {
        if (_files.CurrentPath is null) return ExecuteSaveAs();

        try
        {
            _files.Save(Editor.Document.Text);
            _isDirty = false;
            UpdateTitle();
            return true;
        }
        // Special-cased so a save that fails only because the file is read-only (e.g. the
        // installed ReleaseNotes.md — see EnsureReleaseNotesAreReadOnly) points at the actual
        // way out instead of surfacing a raw "Access to the path is denied" OS message.
        catch (UnauthorizedAccessException)
        {
            MessageBox.Show("This file is read-only. Use Save As to save your changes to a new location.",
                "MDEdit", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save file:\n{ex.Message}", "MDEdit",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private bool ExecuteSaveAs()
    {
        var currentExt = Path.GetExtension(_files.CurrentPath ?? "").ToLowerInvariant();
        var isTxt = currentExt == ".txt";
        var dlg = new SaveFileDialog
        {
            Filter = "Markdown files (*.md)|*.md|Text files (*.txt)|*.txt|All files (*.*)|*.*",
            FilterIndex = isTxt ? 2 : 1,
            DefaultExt = isTxt ? ".txt" : ".md",
            FileName = _files.CurrentPath is null ? "Untitled.md" : Path.GetFileName(_files.CurrentPath)
        };
        if (dlg.ShowDialog() != true) return false;

        try
        {
            _files.SaveAs(dlg.FileName, Editor.Document.Text);
            _isDirty = false;
            UpdateHighlighting(dlg.FileName);
            UpdateTitle();
            UpdateStatusBar();
            AddToRecentFiles(dlg.FileName);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save file:\n{ex.Message}", "MDEdit",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private bool CheckUnsavedChanges()
    {
        if (!_isDirty) return true;

        var name = _files.CurrentPath is string p ? Path.GetFileName(p) : "Untitled";
        var result = MessageBox.Show(
            $"Save changes to '{name}'?", "MDEdit",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

        return result switch
        {
            MessageBoxResult.Yes    => ExecuteSave(),
            MessageBoxResult.No     => true,
            _                       => false
        };
    }

    // ── Formatting helpers ────────────────────────────────────────────────
    // The actual edit logic lives in MarkdownFormatter (unit-testable, no UI dependency);
    // these thin wrappers feed it the editor's selection and apply the returned caret placement.

    // SelectionStart equals the caret offset when the selection is empty, so it serves both cases.
    private SelectionRange CurrentSelection => new(Editor.SelectionStart, Editor.SelectionLength);

    // Editor.Select(start, length) sets both atomically, unlike setting SelectionStart then
    // SelectionLength separately: TextEditor.SelectionStart's setter internally reuses the
    // CURRENT SelectionLength (Select(value, SelectionLength)), and that value is whatever the
    // AvalonEdit selection's TextAnchors resolved to after MarkdownFormatter's doc.Replace —
    // not necessarily s.Length. When the replaced span reached the end of the document, that
    // stale length could exceed the new document's length at the new start, throwing
    // ArgumentOutOfRangeException ("Value must be between 0 and N") — reproducible by wrapping
    // a selection that ends at EOF (e.g. Ctrl+B over the last word in the file).
    private void ApplyFormat(SelectionRange? sel)
    {
        if (sel is { } s)
            Editor.Select(s.Start, s.Length);
        Editor.Focus();
    }

    private void WrapSelection(string prefix, string suffix)
        => ApplyFormat(MarkdownFormatter.Wrap(Editor.Document, CurrentSelection, prefix, suffix));

    private void InsertHeading(int level)
        => ApplyFormat(MarkdownFormatter.Heading(Editor.Document, CurrentSelection, level));

    private void InsertLinePrefix(string prefix)
        => ApplyFormat(MarkdownFormatter.ToggleLinePrefix(Editor.Document, CurrentSelection, prefix));

    private void InsertCodeBlock()
        => ApplyFormat(MarkdownFormatter.CodeBlock(Editor.Document, CurrentSelection));

    private void InsertLink()
        => ApplyFormat(MarkdownFormatter.Link(Editor.Document, CurrentSelection));

    private void InsertTable()
        => ApplyFormat(MarkdownFormatter.Table(Editor.Document, CurrentSelection));

    // ── Dirty / title / status ────────────────────────────────────────────
    private void MarkDirty()
    {
        if (_isDirty) return;
        _isDirty = true;
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        var name  = _files.CurrentPath is string p ? Path.GetFileName(p) : "Untitled";
        var dirty = _isDirty ? "*" : "";
        Title = $"MDEdit - {name}{dirty}";
    }

    // Caret position and selection size — cheap, and called on every caret move.
    private void UpdateStatusBar()
    {
        var caret = Editor.TextArea.Caret;
        StatusPosition.Text = $"Ln {caret.Line}, Col {caret.Column}";

        var selected = Editor.SelectionLength;
        var visibility = selected > 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusSelection.Visibility = visibility;
        StatusSelectionSeparator.Visibility = visibility;
        if (selected > 0)
            StatusSelection.Text = StatusFormatter.FormatSelectionCount(selected);
    }

    // Size and character count — separated from UpdateStatusBar because computing the byte count
    // means walking the whole document, which must not happen on every caret move. Called on text
    // change and after a document is loaded or reset.
    private void UpdateDocumentStats()
    {
        // The size a save would produce, not the size on disk: it stays correct while editing, and
        // for an unmodified saved file the two agree. FileService always writes UTF-8 via
        // File.WriteAllText, which emits the BOM preamble, so that is counted too — otherwise this
        // would disagree with Explorer by exactly the 3 preamble bytes.
        var text = Editor.Document.Text;
        long bytes = Encoding.UTF8.GetPreamble().Length + Encoding.UTF8.GetByteCount(text);

        StatusFileSize.Text  = StatusFormatter.FormatFileSize(bytes);
        // The user's chosen weight (View > Line Breaks Count) — 0, 1, or 2 characters per
        // line break, regardless of what it actually is in the raw text. See CharacterCounter.
        var charCount = CharacterCounter.Count(Editor.Document, _settings.LineBreakCharWeight);
        StatusCharCount.Text = StatusFormatter.FormatCharacterCount(charCount);
    }

    // ── Live preview (WYSIWYG) ────────────────────────────────────────────
    // Heading, blockquote, and list markers reveal per *line* (caret anywhere on the line); emphasis
    // and link markers reveal per *span* (caret inside that specific run), so unlike the purely
    // line-scoped generators, any caret offset change — not just a line change — can affect
    // what's hidden and must trigger a redraw of the affected line(s). Generator state is
    // updated before the redraws so both the line the caret left (re-hide) and the line/span it
    // entered (reveal) render against the new caret position. Code-block fences are line-scoped
    // too, but the fence pair bracketing the caret's line can sit far away from it (the only
    // multi-line construct here), so those need their own redraw beyond the old/new caret line.
    private void OnCaretPositionChanged()
    {
        UpdateStatusBar();
        if (!_settings.LivePreview) return;

        var caret  = Editor.TextArea.Caret;
        var line   = caret.Line;
        var offset = caret.Offset;
        if (offset == _lastCaretOffset) return;

        var previousLine = _lastCaretLine;
        _lastCaretLine   = line;
        _lastCaretOffset = offset;
        _headingMarkerGenerator.CaretLine    = line;
        _emphasisMarkerGenerator.CaretOffset = offset;
        _codeBlockFenceGenerator.CaretLine   = line;
        _imageGenerator.CaretOffset          = offset;
        _linkMarkerGenerator.CaretOffset     = offset;
        _underlineMarkerGenerator.CaretOffset = offset;
        _emojiGenerator.CaretOffset          = offset;
        _blockquoteMarkerGenerator.CaretLine = line;
        _bulletListMarkerGenerator.CaretLine = line;
        _taskListMarkerGenerator.CaretLine   = line;
        _numberedListMarkerGenerator.CaretLine = line;
        _tableRowGenerator.CaretLine         = line;
        _horizontalRuleGenerator.CaretLine   = line;
        _colorizer.CaretLine                 = line;
        _colorizer.CaretOffset               = offset;

        RedrawLine(previousLine);
        if (line != previousLine) RedrawLine(line);
        RedrawEnclosingFenceLines(previousLine);
        if (line != previousLine) RedrawEnclosingFenceLines(line);
        RedrawEnclosingTableLines(previousLine);
        if (line != previousLine) RedrawEnclosingTableLines(line);
    }

    private void RedrawLine(int lineNumber)
    {
        if (lineNumber < 1 || lineNumber > Editor.Document.LineCount) return;
        Editor.TextArea.TextView.Redraw(Editor.Document.GetLineByNumber(lineNumber));
    }

    private void RedrawEnclosingFenceLines(int lineNumber)
    {
        if (!MarkdownSyntax.TryGetEnclosingFenceBlock(Editor.Document, lineNumber, out int start, out int end)) return;
        RedrawLine(start);
        if (end != start) RedrawLine(end);
    }

    // Tables reveal per block like code fences, but crossing the boundary changes the
    // appearance of every line in the block (rows flip between grid and raw source), not just
    // the two delimiters — so the whole block is redrawn. Tables are small; this is bounded.
    private void RedrawEnclosingTableLines(int lineNumber)
    {
        if (!MarkdownSyntax.TryGetTableBlock(Editor.Document, lineNumber, out int start, out int end)) return;
        for (int n = start; n <= end; n++) RedrawLine(n);
    }

    // Called after loading a new/opened document, whose caret always resets to line 1 —
    // without this, stale tracking state from the previous document could suppress the
    // redraw that shows/hides markers correctly on first caret move in the new document.
    private void ResetLivePreviewCaretTracking()
    {
        _lastCaretLine   = Editor.TextArea.Caret.Line;
        _lastCaretOffset = Editor.TextArea.Caret.Offset;
        _headingMarkerGenerator.CaretLine    = _lastCaretLine;
        _emphasisMarkerGenerator.CaretOffset = _lastCaretOffset;
        _codeBlockFenceGenerator.CaretLine   = _lastCaretLine;
        _imageGenerator.CaretOffset          = _lastCaretOffset;
        _linkMarkerGenerator.CaretOffset     = _lastCaretOffset;
        _underlineMarkerGenerator.CaretOffset = _lastCaretOffset;
        _emojiGenerator.CaretOffset          = _lastCaretOffset;
        _blockquoteMarkerGenerator.CaretLine = _lastCaretLine;
        _bulletListMarkerGenerator.CaretLine = _lastCaretLine;
        _taskListMarkerGenerator.CaretLine   = _lastCaretLine;
        _numberedListMarkerGenerator.CaretLine = _lastCaretLine;
        _tableRowGenerator.CaretLine         = _lastCaretLine;
        _horizontalRuleGenerator.CaretLine   = _lastCaretLine;
        _colorizer.CaretLine                 = _lastCaretLine;
        _colorizer.CaretOffset               = _lastCaretOffset;
    }

    private void UpdateLivePreviewState()
    {
        // WYSIWYG reads as a document: prose in a proportional font, source mode all-mono. Code
        // spans/blocks stay mono in WYSIWYG because that mode's code elements pin the family
        // (Requirements.md §6), and revealed construct lines swap back via MarkdownLineColorizer.
        var baseStyles = ActiveModeStyles();
        Editor.FontFamily = _settings.LivePreview ? _wysiwygFontFamily : _sourceFontFamily;
        Editor.FontSize = baseStyles.BaseFontSize;

        _colorizer.LivePreviewEnabled = _settings.LivePreview;
        _headingMarkerGenerator.Enabled    = _settings.LivePreview;
        _emphasisMarkerGenerator.Enabled   = _settings.LivePreview;
        _codeBlockFenceGenerator.Enabled   = _settings.LivePreview;
        _imageGenerator.Enabled            = _settings.LivePreview;
        _linkMarkerGenerator.Enabled       = _settings.LivePreview;
        _underlineMarkerGenerator.Enabled  = _settings.LivePreview;
        _emojiGenerator.Enabled            = _settings.LivePreview;
        _blockquoteMarkerGenerator.Enabled = _settings.LivePreview;
        _bulletListMarkerGenerator.Enabled = _settings.LivePreview;
        _taskListMarkerGenerator.Enabled   = _settings.LivePreview;
        _numberedListMarkerGenerator.Enabled = _settings.LivePreview;
        _tableRowGenerator.Enabled           = _settings.LivePreview;
        _horizontalRuleGenerator.Enabled     = _settings.LivePreview;
        _blockquoteAccentBarRenderer.Enabled = _settings.LivePreview;
        _tableGridRenderer.Enabled           = _settings.LivePreview;
        _horizontalRuleRenderer.Enabled      = _settings.LivePreview;

        // Zoom (Requirements.md §6). Text sizes follow zoom on their own, since everything resolves
        // from the scaled ModeStyles ActiveModeStyles hands out — but these classes lay out against
        // fixed pixel constants (the shared blockquote indent, and the image height clamp), so they
        // have to be told. Pushed here because this method already fans state out to every one of
        // them. TableGridRenderer is absent on purpose: it reads zoom back off _tableRowGenerator so
        // the drawn grid and the rows can never disagree.
        double zoom = _settings.ZoomLevel;
        _blockquoteMarkerGenerator.Zoom   = zoom;
        _blockquoteAccentBarRenderer.Zoom = zoom;
        _bulletListMarkerGenerator.Zoom   = zoom;
        _numberedListMarkerGenerator.Zoom = zoom;
        _taskListMarkerGenerator.Zoom     = zoom;
        _tableRowGenerator.Zoom           = zoom;
        _imageGenerator.Zoom              = zoom;

        ResetLivePreviewCaretTracking();
        MenuEditorModeSource.IsChecked   = !_settings.LivePreview;
        MenuEditorModeWysiwyg.IsChecked  = _settings.LivePreview;
        BtnEditorModeToggle.IsChecked    = _settings.LivePreview;

        // Icon/tooltip show the mode a click switches TO, not the current one.
        if (_settings.LivePreview)
        {
            BtnEditorModeToggleIcon.Data = (Geometry)FindResource("IconEyeClosed");
            BtnEditorModeToggle.ToolTip = "Toggle to code view";
        }
        else
        {
            BtnEditorModeToggleIcon.Data = (Geometry)FindResource("IconEye");
            BtnEditorModeToggle.ToolTip = "Toggle to WYSIWYG view";
        }

        // Per-element styling is per-mode (Requirements.md §6), so the editor mode selects which of
        // the four compiled definitions is live — not just the theme, as it did before. Setting
        // SyntaxHighlighting re-inserts AvalonEdit's colorizer at index 0, keeping _colorizer after
        // it, which is what lets MarkdownLineColorizer still override the XSHD styling.
        ApplyActiveModeStyles();
        UpdateHighlighting(_files.CurrentPath);
    }

    private void SetLivePreview(bool enabled)
    {
        _settings.LivePreview = enabled;
        SettingsService.Save(_settings);
        UpdateLivePreviewState();
        Editor.TextArea.TextView.Redraw();
    }

    private void MenuEditorModeSource_Click(object sender, RoutedEventArgs e)  => SetLivePreview(false);
    private void MenuEditorModeWysiwyg_Click(object sender, RoutedEventArgs e) => SetLivePreview(true);
    // The ToggleButton flips its own IsChecked before Click fires, so it already holds the state
    // the user just asked for — just apply it, the same way the two menu handlers apply a fixed one.
    private void BtnEditorModeToggle_Click(object sender, RoutedEventArgs e) => SetLivePreview(BtnEditorModeToggle.IsChecked == true);

    // ── Event handlers (toolbar / menu) ───────────────────────────────────
    private void BtnStrike_Click(object sender, RoutedEventArgs e)   => WrapSelection("~~", "~~");
    private void BtnHighlight_Click(object sender, RoutedEventArgs e) => WrapSelection("==", "==");
    private void BtnSuperscript_Click(object sender, RoutedEventArgs e) => WrapSelection("^", "^");
    private void BtnSubscript_Click(object sender, RoutedEventArgs e)  => WrapSelection("~", "~");
    // The only formatting command that emits HTML rather than Markdown — see Requirements.md §3.
    private void BtnUnderline_Click(object sender, RoutedEventArgs e)  => WrapSelection("<u>", "</u>");
    // Opens the browsable/searchable picker (Requirements.md §3) rather than wrapping the selection
    // in bare colons — picking an entry inserts its shortcode text, not the raw emoji character, so
    // a picked emoji is indistinguishable from one typed by hand.
    private void BtnEmoji_Click(object sender, RoutedEventArgs e)
    {
        var picker = new EmojiPickerWindow { Owner = this };
        if (picker.ShowDialog() == true)
            ApplyFormat(MarkdownFormatter.InsertEmoji(Editor.Document, CurrentSelection, picker.SelectedShortcode!));
    }
    private void BtnTable_Click(object sender, RoutedEventArgs e)      => InsertTable();
    private void BtnH1_Click(object sender, RoutedEventArgs e)       => InsertHeading(1);
    private void BtnH2_Click(object sender, RoutedEventArgs e)       => InsertHeading(2);
    private void BtnH3_Click(object sender, RoutedEventArgs e)       => InsertHeading(3);
    private void BtnCode_Click(object sender, RoutedEventArgs e)     => WrapSelection("`", "`");
    private void BtnCodeBlock_Click(object sender, RoutedEventArgs e)=> InsertCodeBlock();
    private void BtnLink_Click(object sender, RoutedEventArgs e)     => InsertLink();
    private void BtnBulletList_Click(object sender, RoutedEventArgs e)  => InsertLinePrefix("- ");
    // Not InsertLinePrefix("1. ") — the marker has to count across a multi-line selection.
    private void BtnNumberList_Click(object sender, RoutedEventArgs e)
        => ApplyFormat(MarkdownFormatter.NumberedList(Editor.Document, CurrentSelection));
    private void BtnBlockquote_Click(object sender, RoutedEventArgs e)  => InsertLinePrefix("> ");
    private void BtnTaskList_Click(object sender, RoutedEventArgs e)
        => ApplyFormat(MarkdownFormatter.TaskListItem(Editor.Document, CurrentSelection));

    private void ApplySettings()
    {
        Editor.WordWrap = _settings.WordWrap;
        MenuWordWrap.IsChecked = _settings.WordWrap;
        Editor.ShowLineNumbers = _settings.ShowLineNumbers;
        MenuLineNumbers.IsChecked = _settings.ShowLineNumbers;
        // settings.json is user-editable, so the persisted list is re-checked rather than trusted.
        _settings.RecentFiles = RecentFiles.Sanitize(_settings.RecentFiles);
        RebuildRecentFilesMenu();
        // Likewise for the zoom level, and it must be sanitized BEFORE the calls below: they read it
        // through ActiveModeStyles to size everything they apply.
        _settings.ZoomLevel = ZoomLevels.Sanitize(_settings.ZoomLevel);
        UpdateLivePreviewState();
        ApplyTheme();
        ApplyLineBreakCharWeight();
        ApplyLoadRemoteImages();
        ApplyZoom();
    }

    // ── Theme ─────────────────────────────────────────────────────────────
    private void ApplyTheme()
    {
        var theme = ThemeService.Parse(_settings.Theme);
        ThemeService.Apply(theme);

        var dark = ThemeService.IsDarkEffective(theme);
        _colorizer.IsDark = dark;
        // Re-resolves the list markers against the new theme; the colorizer drops its own cache
        // from the IsDark setter above, and the two renderers read through it.
        ApplyActiveModeStyles();
        _tableGridRenderer.IsDark = dark;
        _horizontalRuleRenderer.IsDark = dark;
        Editor.TextArea.Caret.CaretBrush = dark ? Brushes.Gainsboro : null;
        UpdateHighlighting(_files.CurrentPath);
        Editor.TextArea.TextView.Redraw();

        // Toolbar Highlight swatch (Requirements.md §6/§8): resolved live from the active mode's
        // highlight element rather than a fixed color, so a theme switch, an editor-mode switch and
        // a Preferences change (which routes through ApplyEditorPreferences -> ApplyTheme) all keep
        // it in sync automatically. Null when the element inherits its background, which leaves the
        // button's own chrome showing — the honest rendering of "no highlight colour of its own".
        BtnHighlightSwatch.Background =
            StyleResolver.Resolve(StyledElements.Highlight, ActiveModeStyles(), dark).Background;

        ApplySearchPanelColors();

        MenuThemeLight.IsChecked  = theme == AppTheme.Light;
        MenuThemeDark.IsChecked   = theme == AppTheme.Dark;
        MenuThemeSystem.IsChecked = theme == AppTheme.System;
    }

    // Pushes colors onto Resources/SearchPanelStyle.xaml's named parts explicitly rather than
    // trusting its DynamicResource references, the same "resolve once from a properly-connected
    // element (MainWindow), then assign as a local value" pattern BtnHighlightSwatch/_colorizer's
    // brushes already use. Called from the IsVisibleChanged handler above, so this runs against a
    // panel that's actually connected to a live visual tree, not one that hasn't been opened yet.
    // ApplyTemplate() is required first — Template.FindName returns null until it has run at least
    // once. Full history (three separate causes behind the illegibility bug this fixes, not one) is
    // in CLAUDE.md's SearchPanelStyle.xaml entry.
    private void ApplySearchPanelColors()
    {
        if (_searchPanel is null) return;
        _searchPanel.ApplyTemplate();

        var chromeBackground = (Brush)FindResource("AppChromeBackgroundBrush");
        var chromeForeground = (Brush)FindResource("AppChromeForegroundBrush");
        var chromeDivider    = (Brush)FindResource("AppChromeDividerBrush");
        var editorBackground = (Brush)FindResource("EditorBackgroundBrush");
        var editorForeground = (Brush)FindResource("EditorForegroundBrush");

        if (FindSearchPanelPart<Border>("PART_outerBorder") is { } outerBorder)
        {
            outerBorder.Background = chromeBackground;
            outerBorder.BorderBrush = chromeDivider;
        }
        if (FindSearchPanelPart<TextBox>("PART_searchTextBox") is { } textBox)
        {
            textBox.Background = editorBackground;
            textBox.Foreground = editorForeground;
            textBox.BorderBrush = chromeDivider;
        }
        if (FindSearchPanelPart<Border>("PART_dropdownBorder") is { } dropdownBorder)
        {
            dropdownBorder.Background = chromeBackground;
            dropdownBorder.BorderBrush = chromeDivider;
        }
        foreach (var name in new[] { "PART_matchCaseCheckBox", "PART_wholeWordsCheckBox", "PART_useRegexCheckBox" })
        {
            if (FindSearchPanelPart<CheckBox>(name) is { } checkBox)
                checkBox.Foreground = chromeForeground;
        }
        foreach (var name in new[] { "PART_prevIcon", "PART_nextIcon", "PART_closeIcon" })
        {
            if (FindSearchPanelPart<System.Windows.Shapes.Path>(name) is { } icon)
                icon.Stroke = chromeForeground;
        }
        // ChromeFlatButtonStyle's custom template (see SearchPanelStyle.xaml — Fluent's own
        // default Button template doesn't paint content in the resting state at all, which is why
        // these three need a custom template) reads its chrome from TemplateBinding Background, so
        // it needs a real value pushed here the same way every other part above does.
        foreach (var name in new[] { "PART_prevButton", "PART_nextButton", "PART_closeButton" })
        {
            if (FindSearchPanelPart<Button>(name) is { } button)
            {
                button.Background = chromeBackground;
                button.BorderBrush = Brushes.Transparent;
            }
        }
    }

    private T? FindSearchPanelPart<T>(string name) where T : class
        => _searchPanel!.Template.FindName(name, _searchPanel) as T;

    private void SetTheme(AppTheme theme)
    {
        _settings.Theme = theme.ToString();
        SettingsService.Save(_settings);
        ApplyTheme();
    }

    private void MenuThemeLight_Click(object sender, RoutedEventArgs e)  => SetTheme(AppTheme.Light);
    private void MenuThemeDark_Click(object sender, RoutedEventArgs e)   => SetTheme(AppTheme.Dark);
    private void MenuThemeSystem_Click(object sender, RoutedEventArgs e) => SetTheme(AppTheme.System);

    // ── Line break character count (Requirements.md §9) ──────────────────────
    private void ApplyLineBreakCharWeight()
    {
        MenuLineBreakZero.IsChecked = _settings.LineBreakCharWeight == 0;
        MenuLineBreakOne.IsChecked  = _settings.LineBreakCharWeight == 1;
        MenuLineBreakTwo.IsChecked  = _settings.LineBreakCharWeight == 2;
    }

    private void SetLineBreakCharWeight(int weight)
    {
        _settings.LineBreakCharWeight = weight;
        SettingsService.Save(_settings);
        ApplyLineBreakCharWeight();
        UpdateDocumentStats(); // recompute the displayed count against the new weight immediately
    }

    private void MenuLineBreakZero_Click(object sender, RoutedEventArgs e) => SetLineBreakCharWeight(0);
    private void MenuLineBreakOne_Click(object sender, RoutedEventArgs e)  => SetLineBreakCharWeight(1);
    private void MenuLineBreakTwo_Click(object sender, RoutedEventArgs e)  => SetLineBreakCharWeight(2);

    // ── Zoom (Requirements.md §6) ─────────────────────────────────────────
    // Zoom scales the editor's rendered text without touching the sizes Preferences stores — see
    // ActiveModeStyles and ModeStyles.Scaled for how, and AppSettings.ZoomLevel for why the level
    // lives at the top of the settings rather than inside EditorPreferences.

    /// <summary>Pushes the current level onto the UI. The Set/Apply split matches
    /// <see cref="SetLineBreakCharWeight"/>/<see cref="ApplyLineBreakCharWeight"/>.</summary>
    private void ApplyZoom()
    {
        StatusZoom.Text = StatusFormatter.FormatZoom(_settings.ZoomLevel);
        foreach (var item in MenuZoomPresets.Items.OfType<MenuItem>())
            item.IsChecked = item.Tag is string tag
                && double.TryParse(tag, NumberStyles.Float, CultureInfo.InvariantCulture, out double preset)
                && Math.Abs(preset - _settings.ZoomLevel) < 0.0005;
    }

    private void SetZoom(double level)
    {
        level = ZoomLevels.Sanitize(level);
        if (Math.Abs(level - _settings.ZoomLevel) < 0.0005) return; // nothing changed; skip the rebuild

        _settings.ZoomLevel = level;
        SettingsService.Save(_settings);
        // ApplyEditorPreferences already recompiles the four highlighting definitions, resets
        // Editor.FontSize from the (now scaled) base, re-pushes the colorizer and marker styles and
        // redraws — so zoom needs no redraw path of its own.
        ApplyEditorPreferences();
        ApplyZoom();
    }

    private void ZoomIn()    => SetZoom(ZoomLevels.In(_settings.ZoomLevel));
    private void ZoomOut()   => SetZoom(ZoomLevels.Out(_settings.ZoomLevel));
    private void ZoomReset() => SetZoom(ZoomLevels.Default);

    private void MenuZoomIn_Click(object sender, RoutedEventArgs e)    => ZoomIn();
    private void MenuZoomOut_Click(object sender, RoutedEventArgs e)   => ZoomOut();
    private void MenuZoomReset_Click(object sender, RoutedEventArgs e) => ZoomReset();

    private void BtnZoomIn_Click(object sender, RoutedEventArgs e)  => ZoomIn();
    private void BtnZoomOut_Click(object sender, RoutedEventArgs e) => ZoomOut();

    // Opening the preset list on a plain left click, which a ContextMenu doesn't do by itself.
    // PlacementTarget must be set explicitly: a menu opened from code rather than by right-click
    // has no target of its own, and Placement="Top" would otherwise resolve against the mouse.
    private void BtnZoomLevel_Click(object sender, RoutedEventArgs e)
    {
        MenuZoomPresets.PlacementTarget = BtnZoomLevel;
        MenuZoomPresets.IsOpen = true;
    }

    // Tag carries the level as an invariant string — the levels are XAML literals, so they must not
    // be read back with the current culture's decimal separator.
    private void MenuZoomPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag }
            && double.TryParse(tag, NumberStyles.Float, CultureInfo.InvariantCulture, out double level))
            SetZoom(level);

        // The checkmarks are owned by ApplyZoom, which SetZoom calls. Re-sync unconditionally so a
        // click on the already-current level doesn't leave the item toggled off by IsCheckable.
        ApplyZoom();
    }

    /// <summary>
    /// Ctrl+wheel zoom, coalesced so a fast scroll doesn't recompile the highlighting on every notch.
    /// </summary>
    /// <remarks>
    /// At 10% steps a 100%→500% sweep is around forty notches, and each apply rebuilds four
    /// definitions — so the level is updated immediately but the expensive apply is posted once at
    /// <see cref="DispatcherPriority.Background"/> behind a queued flag. Every Normal-priority wheel
    /// event drains before any Background item runs, so a burst costs exactly one rebuild. Same
    /// mechanism <c>RemoteImageLoader</c> uses to collapse a burst of image completions into one
    /// redraw: deterministic, with no timer interval to tune, and Background cannot re-enter an
    /// in-progress layout pass.
    /// <para>
    /// Preview, not the bubbling event, because AvalonEdit's own ScrollViewer would otherwise scroll
    /// the document first.
    /// </para>
    /// </remarks>
    private void Editor_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control || e.Delta == 0) return;
        e.Handled = true;

        double level = e.Delta > 0
            ? ZoomLevels.In(_pendingZoomLevel ?? _settings.ZoomLevel)
            : ZoomLevels.Out(_pendingZoomLevel ?? _settings.ZoomLevel);
        _pendingZoomLevel = level;
        StatusZoom.Text = StatusFormatter.FormatZoom(level); // the readout still tracks every notch

        if (_zoomApplyQueued) return;
        _zoomApplyQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _zoomApplyQueued = false;
            if (_pendingZoomLevel is not double pending) return;
            _pendingZoomLevel = null;
            SetZoom(pending);
        });
    }

    // ── Remote images (Requirements.md §5) ────────────────────────────────
    private void ApplyLoadRemoteImages()
    {
        MenuLoadRemoteImages.IsChecked = _settings.LoadRemoteImages;
        // The generator's setter invalidates the remote loader on a real change, so turning this
        // off cancels in-flight fetches and drops cached bitmaps immediately rather than at the
        // next document open.
        _imageGenerator.LoadRemoteImages = _settings.LoadRemoteImages;
    }

    // IsCheckable flips IsChecked before Click fires, so the handler reads the new state back
    // (MenuWordWrap_Click's shape); the save/apply/redraw tail is SetLivePreview's. Deliberately
    // not routed through UpdateLivePreviewState — this isn't a live-preview mode, it's a separate
    // opt-in that only has any effect while live preview is on.
    private void MenuLoadRemoteImages_Click(object sender, RoutedEventArgs e)
    {
        _settings.LoadRemoteImages = MenuLoadRemoteImages.IsChecked;
        SettingsService.Save(_settings);
        ApplyLoadRemoteImages();
        Editor.TextArea.TextView.Redraw();
    }

    // View > Preferences… (Requirements.md §6). The window owns its own Cancel/Reset-to-Default
    // snapshot logic against the live AppSettings.EditorPreferences instance and applies every
    // change immediately via the ApplyEditorPreferences callback — this handler just shows it and
    // persists whatever's left once it closes, the same "save after the dialog returns" shape as
    // every other settings write in this codebase.
    private void MenuPreferences_Click(object sender, RoutedEventArgs e)
    {
        new PreferencesWindow(_settings, ApplyEditorPreferences) { Owner = this }.ShowDialog();
        SettingsService.Save(_settings);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // Raised on a broadcast thread; General is the category OS theme switches arrive under.
        if (e.Category != UserPreferenceCategory.General) return;
        if (ThemeService.Parse(_settings.Theme) != AppTheme.System) return;
        Dispatcher.BeginInvoke(ApplyTheme);
    }

    protected override void OnClosed(EventArgs e)
    {
        // SystemEvents holds a static reference; unhook so the window can be collected.
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        base.OnClosed(e);
    }

    private void MenuWordWrap_Click(object sender, RoutedEventArgs e)
    {
        Editor.WordWrap = MenuWordWrap.IsChecked;
        _settings.WordWrap = MenuWordWrap.IsChecked;
        SettingsService.Save(_settings);
    }

    private void MenuLineNumbers_Click(object sender, RoutedEventArgs e)
    {
        Editor.ShowLineNumbers = MenuLineNumbers.IsChecked;
        _settings.ShowLineNumbers = MenuLineNumbers.IsChecked;
        SettingsService.Save(_settings);
    }

    private void MenuAbout_Click(object sender, RoutedEventArgs e)
    {
        var about = new AboutWindow { Owner = this };
        about.ShowDialog();

        // The dialog hands back a path instead of opening it — see AboutWindow.RequestedDocumentPath.
        // From here on this is an ordinary user-initiated Open, matching MenuOpenReleaseNotes_Click:
        // the unsaved-changes guard applies, it joins Recent Files, and errors surface normally.
        if (about.RequestedDocumentPath is not string path) return;
        if (!CheckUnsavedChanges()) return;

        if (!File.Exists(path))
        {
            MessageBox.Show("That document could not be found.", "MDEdit",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        OpenFile(path);
    }

    private void MenuRegisterFileAssociations_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            FileAssociationService.Register();
            MessageBox.Show(
                "MDEdit is now the default app for .md and .markdown files.\n\n" +
                "For .txt, MDEdit is listed under \"Open with\" but the existing default app is left " +
                "alone — to make MDEdit the default there too, right-click a .txt file, choose " +
                "\"Open with\" > \"Choose another app\", select MDEdit, and check \"Always use this app\".",
                "Register File Associations", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not register file associations:\n\n{ex.Message}",
                "Register File Associations", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!CheckUnsavedChanges())
            e.Cancel = true;
    }
}

// ── Minimal relay command for key bindings ────────────────────────────────
file sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? _) => true;
    public void Execute(object? _) => execute();
}
