using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    private readonly LinkMarkerElementGenerator _linkMarkerGenerator = new();
    private readonly UnderlineMarkerElementGenerator _underlineMarkerGenerator = new();
    private readonly BlockquoteMarkerElementGenerator _blockquoteMarkerGenerator = new();
    private readonly BulletListMarkerElementGenerator _bulletListMarkerGenerator = new();
    private readonly NumberedListMarkerElementGenerator _numberedListMarkerGenerator = new();
    private readonly BlockquoteAccentBarRenderer _blockquoteAccentBarRenderer = new();
    private bool _isDirty;
    private int _lastCaretLine = -1;
    private int _lastCaretOffset = -1;
    private IHighlightingDefinition? _markdownLight;
    private IHighlightingDefinition? _markdownDark;

    // ── Constructor ───────────────────────────────────────────────────────
    public MainWindow()
    {
        InitializeComponent();
        LoadSyntaxHighlighting();
        Editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        Editor.TextArea.TextView.ElementGenerators.Add(_headingMarkerGenerator);
        Editor.TextArea.TextView.ElementGenerators.Add(_emphasisMarkerGenerator);
        Editor.TextArea.TextView.ElementGenerators.Add(_codeBlockFenceGenerator);
        Editor.TextArea.TextView.ElementGenerators.Add(_linkMarkerGenerator);
        Editor.TextArea.TextView.ElementGenerators.Add(_underlineMarkerGenerator);
        Editor.TextArea.TextView.ElementGenerators.Add(_blockquoteMarkerGenerator);
        Editor.TextArea.TextView.ElementGenerators.Add(_bulletListMarkerGenerator);
        Editor.TextArea.TextView.ElementGenerators.Add(_numberedListMarkerGenerator);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_blockquoteAccentBarRenderer);
        RegisterCommands();
        RegisterKeyBindings();
        SearchPanel.Install(Editor);
        ApplySettings();

        Editor.TextArea.Caret.PositionChanged += (_, _) => OnCaretPositionChanged();
        Editor.TextChanged += (_, _) => MarkDirty();

        // Follow OS light/dark switches live while the theme setting is System.
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        // args[0] is the exe path itself; a file path argument (from double-clicking an associated
        // file, or "Open with") is args[1], per the "MDEdit.exe" "%1" command FileAssociationService registers.
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1)
            OpenFile(args[1]);
    }

    // ── Syntax highlighting ───────────────────────────────────────────────

    // Dark replacements for the colors defined in Markdown.xshd (whose values are tuned
    // for a white background). Colors absent here (Bold/Italic/BoldItalic) are style-only.
    private static readonly Dictionary<string, (Color? Foreground, Color? Background)> DarkHighlightColors = new()
    {
        ["Strike"]     = (Color.FromRgb(0x8B, 0x94, 0x9E), null),
        // Muted amber rather than the light theme's pale yellow, which reads as a glaring block on a
        // dark page. Tuned against the #1E1E1E editor background and #DCDCDC text (Themes/Dark.xaml):
        // light enough to be an obvious band (~2.6:1 against the page — an earlier, darker #4A431F
        // managed only ~1.7:1 and was barely visible), while keeping body text at ~4.7:1 over it.
        // Foreground stays unset so highlighted text keeps the editor's standard text color.
        ["Highlight"]  = (null, Color.FromRgb(0x6A, 0x5E, 0x2E)),
        ["InlineCode"] = (Color.FromRgb(0xFF, 0x7B, 0x72), Color.FromRgb(0x30, 0x36, 0x3D)),
        ["CodeBlock"]  = (Color.FromRgb(0xFF, 0x7B, 0x72), Color.FromRgb(0x30, 0x36, 0x3D)),
        ["Link"]       = (Color.FromRgb(0x58, 0xA6, 0xFF), null),
        ["ListMarker"] = (Color.FromRgb(0x79, 0xC0, 0xFF), null),
        ["Comment"]    = (Color.FromRgb(0x8B, 0x94, 0x9E), null),
    };

    private void LoadSyntaxHighlighting()
    {
        _markdownLight = LoadDefinition(dark: false);
        _markdownDark  = LoadDefinition(dark: true);
        Editor.SyntaxHighlighting = _markdownLight; // ApplySettings/ApplyTheme picks the real one
    }

    // The dark variant is built by recoloring the parsed XSHD model before compiling it,
    // rather than mutating the loaded definition (whose colors may be frozen).
    private static IHighlightingDefinition LoadDefinition(bool dark)
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("MDEdit.Resources.Markdown.xshd")!;
        using var reader = new XmlTextReader(stream);
        var xshd = HighlightingLoader.LoadXshd(reader);

        if (dark)
        {
            foreach (var color in xshd.Elements.OfType<XshdColor>())
            {
                if (color.Name is not null && DarkHighlightColors.TryGetValue(color.Name, out var c))
                {
                    if (c.Foreground is Color fg) color.Foreground = new SimpleHighlightingBrush(fg);
                    if (c.Background is Color bg) color.Background = new SimpleHighlightingBrush(bg);
                }
            }
        }

        return HighlightingLoader.Load(xshd, HighlightingManager.Instance);
    }

    private void UpdateHighlighting(string? path)
    {
        var ext = path is null ? ".md" : Path.GetExtension(path).ToLowerInvariant();
        var dark = ThemeService.IsDarkEffective(ThemeService.Parse(_settings.Theme));
        Editor.SyntaxHighlighting = ext is ".md" or ".markdown"
            ? (dark ? _markdownDark : _markdownLight)
            : null;
    }

    // ── Command bindings ──────────────────────────────────────────────────
    private void RegisterCommands()
    {
        CommandBindings.Add(new CommandBinding(ApplicationCommands.New,
            (_, _) => NewDocument(), AlwaysCanExecute));
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

    private void OpenFile(string path)
    {
        try
        {
            Editor.Document.Text = _files.LoadFile(path);
            _isDirty = false;
            UpdateHighlighting(path);
            ResetLivePreviewCaretTracking();
            UpdateTitle();
            UpdateStatusBar();
            AddToRecentFiles(path); // only on success — a file that failed to open isn't "recent"
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open file:\n{ex.Message}", "MDEdit",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
        StatusFileName.Text = $"{name}{dirty}";
    }

    private void UpdateStatusBar()
    {
        var caret = Editor.TextArea.Caret;
        StatusPosition.Text = $"Ln {caret.Line}, Col {caret.Column}";
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
        _linkMarkerGenerator.CaretOffset     = offset;
        _underlineMarkerGenerator.CaretOffset = offset;
        _blockquoteMarkerGenerator.CaretLine = line;
        _bulletListMarkerGenerator.CaretLine = line;
        _numberedListMarkerGenerator.CaretLine = line;

        RedrawLine(previousLine);
        if (line != previousLine) RedrawLine(line);
        RedrawEnclosingFenceLines(previousLine);
        if (line != previousLine) RedrawEnclosingFenceLines(line);
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
        _linkMarkerGenerator.CaretOffset     = _lastCaretOffset;
        _underlineMarkerGenerator.CaretOffset = _lastCaretOffset;
        _blockquoteMarkerGenerator.CaretLine = _lastCaretLine;
        _bulletListMarkerGenerator.CaretLine = _lastCaretLine;
        _numberedListMarkerGenerator.CaretLine = _lastCaretLine;
    }

    private void UpdateLivePreviewState()
    {
        _colorizer.LivePreviewEnabled = _settings.LivePreview;
        _headingMarkerGenerator.Enabled    = _settings.LivePreview;
        _emphasisMarkerGenerator.Enabled   = _settings.LivePreview;
        _codeBlockFenceGenerator.Enabled   = _settings.LivePreview;
        _linkMarkerGenerator.Enabled       = _settings.LivePreview;
        _underlineMarkerGenerator.Enabled  = _settings.LivePreview;
        _blockquoteMarkerGenerator.Enabled = _settings.LivePreview;
        _bulletListMarkerGenerator.Enabled = _settings.LivePreview;
        _numberedListMarkerGenerator.Enabled = _settings.LivePreview;
        _blockquoteAccentBarRenderer.Enabled = _settings.LivePreview;
        ResetLivePreviewCaretTracking();
        MenuEditorModeSource.IsChecked   = !_settings.LivePreview;
        MenuEditorModeWysiwyg.IsChecked  = _settings.LivePreview;
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

    // ── Event handlers (toolbar / menu) ───────────────────────────────────
    private void BtnStrike_Click(object sender, RoutedEventArgs e)   => WrapSelection("~~", "~~");
    private void BtnHighlight_Click(object sender, RoutedEventArgs e) => WrapSelection("==", "==");
    private void BtnSuperscript_Click(object sender, RoutedEventArgs e) => WrapSelection("^", "^");
    private void BtnSubscript_Click(object sender, RoutedEventArgs e)  => WrapSelection("~", "~");
    // The only formatting command that emits HTML rather than Markdown — see Requirements.md §3.
    private void BtnUnderline_Click(object sender, RoutedEventArgs e)  => WrapSelection("<u>", "</u>");
    private void BtnH1_Click(object sender, RoutedEventArgs e)       => InsertHeading(1);
    private void BtnH2_Click(object sender, RoutedEventArgs e)       => InsertHeading(2);
    private void BtnH3_Click(object sender, RoutedEventArgs e)       => InsertHeading(3);
    private void BtnCode_Click(object sender, RoutedEventArgs e)     => WrapSelection("`", "`");
    private void BtnCodeBlock_Click(object sender, RoutedEventArgs e)=> InsertCodeBlock();
    private void BtnLink_Click(object sender, RoutedEventArgs e)     => InsertLink();
    private void BtnBulletList_Click(object sender, RoutedEventArgs e)  => InsertLinePrefix("- ");
    private void BtnNumberList_Click(object sender, RoutedEventArgs e)  => InsertLinePrefix("1. ");
    private void BtnBlockquote_Click(object sender, RoutedEventArgs e)  => InsertLinePrefix("> ");

    private void ApplySettings()
    {
        Editor.WordWrap = _settings.WordWrap;
        MenuWordWrap.IsChecked = _settings.WordWrap;
        Editor.ShowLineNumbers = _settings.ShowLineNumbers;
        MenuLineNumbers.IsChecked = _settings.ShowLineNumbers;
        // settings.json is user-editable, so the persisted list is re-checked rather than trusted.
        _settings.RecentFiles = RecentFiles.Sanitize(_settings.RecentFiles);
        RebuildRecentFilesMenu();
        UpdateLivePreviewState();
        ApplyTheme();
    }

    // ── Theme ─────────────────────────────────────────────────────────────
    private void ApplyTheme()
    {
        var theme = ThemeService.Parse(_settings.Theme);
        ThemeService.Apply(theme);

        var dark = ThemeService.IsDarkEffective(theme);
        _colorizer.IsDark = dark;
        _blockquoteAccentBarRenderer.IsDark = dark;
        Editor.TextArea.Caret.CaretBrush = dark ? Brushes.Gainsboro : null;
        UpdateHighlighting(_files.CurrentPath);
        Editor.TextArea.TextView.Redraw();

        MenuThemeLight.IsChecked  = theme == AppTheme.Light;
        MenuThemeDark.IsChecked   = theme == AppTheme.Dark;
        MenuThemeSystem.IsChecked = theme == AppTheme.System;
    }

    private void SetTheme(AppTheme theme)
    {
        _settings.Theme = theme.ToString();
        SettingsService.Save(_settings);
        ApplyTheme();
    }

    private void MenuThemeLight_Click(object sender, RoutedEventArgs e)  => SetTheme(AppTheme.Light);
    private void MenuThemeDark_Click(object sender, RoutedEventArgs e)   => SetTheme(AppTheme.Dark);
    private void MenuThemeSystem_Click(object sender, RoutedEventArgs e) => SetTheme(AppTheme.System);

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
        => new AboutWindow { Owner = this }.ShowDialog();

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
