using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Search;
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
    private bool _isDirty;
    private IHighlightingDefinition? _markdownHighlighting;

    // ── Constructor ───────────────────────────────────────────────────────
    public MainWindow()
    {
        InitializeComponent();
        LoadSyntaxHighlighting();
        Editor.TextArea.TextView.LineTransformers.Add(new MarkdownLineColorizer());
        RegisterCommands();
        RegisterHeadingKeyBindings();
        SearchPanel.Install(Editor);
        ApplySettings();

        Editor.TextArea.Caret.PositionChanged += (_, _) => UpdateStatusBar();
        Editor.TextChanged += (_, _) => MarkDirty();

        // args[0] is the exe path itself; a file path argument (from double-clicking an associated
        // file, or "Open with") is args[1], per the "MDEdit.exe" "%1" command FileAssociationService registers.
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1)
            OpenFile(args[1]);
    }

    // ── Syntax highlighting ───────────────────────────────────────────────
    private void LoadSyntaxHighlighting()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("MDEdit.Resources.Markdown.xshd")!;
        using var reader = new XmlTextReader(stream);
        _markdownHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        Editor.SyntaxHighlighting = _markdownHighlighting;
    }

    private void UpdateHighlighting(string? path)
    {
        var ext = path is null ? ".md" : Path.GetExtension(path).ToLowerInvariant();
        Editor.SyntaxHighlighting = ext is ".md" or ".markdown" ? _markdownHighlighting : null;
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

    private void RegisterHeadingKeyBindings()
    {
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
            UpdateTitle();
            UpdateStatusBar();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open file:\n{ex.Message}", "MDEdit",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
    private void WrapSelection(string prefix, string suffix)
    {
        var start  = Editor.SelectionStart;
        var length = Editor.SelectionLength;

        if (length > 0)
        {
            var inner = Editor.SelectedText;
            Editor.Document.Replace(start, length, prefix + inner + suffix);
            Editor.SelectionStart  = start + prefix.Length;
            Editor.SelectionLength = inner.Length;
        }
        else
        {
            Editor.Document.Insert(Editor.CaretOffset, prefix + suffix);
            Editor.CaretOffset -= suffix.Length;
        }
        Editor.Focus();
    }

    private void InsertHeading(int level)
    {
        var prefix = new string('#', level) + " ";
        var line   = Editor.Document.GetLineByOffset(Editor.CaretOffset);
        var text   = Editor.Document.GetText(line);
        var body   = Regex.Replace(text, @"^#{1,6}\s*", "");
        Editor.Document.Replace(line.Offset, line.Length, prefix + body);
        Editor.Focus();
    }

    private void InsertLinePrefix(string prefix)
    {
        var line = Editor.Document.GetLineByOffset(Editor.CaretOffset);
        var text = Editor.Document.GetText(line);
        if (text.StartsWith(prefix))
            Editor.Document.Remove(line.Offset, prefix.Length);
        else
            Editor.Document.Insert(line.Offset, prefix);
        Editor.Focus();
    }

    private void InsertCodeBlock()
    {
        var sel    = Editor.SelectedText;
        var start  = Editor.SelectionStart;
        var length = Editor.SelectionLength;

        if (length > 0)
        {
            Editor.Document.Replace(start, length, "```\n" + sel + "\n```");
        }
        else
        {
            var offset = Editor.CaretOffset;
            Editor.Document.Insert(offset, "```\n\n```");
            Editor.CaretOffset = offset + 4;
        }
        Editor.Focus();
    }

    private void InsertLink()
    {
        var sel    = Editor.SelectedText;
        var start  = Editor.SelectionStart;
        var length = Editor.SelectionLength;

        if (length > 0)
        {
            Editor.Document.Replace(start, length, $"[{sel}](url)");
            Editor.SelectionStart  = start + 1 + sel.Length + 2;
            Editor.SelectionLength = 3;
        }
        else
        {
            var offset = Editor.CaretOffset;
            Editor.Document.Insert(offset, "[link text](url)");
            Editor.SelectionStart  = offset + 1;
            Editor.SelectionLength = 9;
        }
        Editor.Focus();
    }

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

    // ── Event handlers (toolbar / menu) ───────────────────────────────────
    private void BtnStrike_Click(object sender, RoutedEventArgs e)   => WrapSelection("~~", "~~");
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
