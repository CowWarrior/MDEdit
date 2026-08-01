using System.IO;
using System.Reflection;
using System.Windows;

namespace MDEdit;

public partial class AboutWindow : Window
{
    /// <summary>
    /// The bundled document the user asked to read, or null if they simply closed the dialog.
    /// </summary>
    /// <remarks>
    /// Handed back for <c>MainWindow.MenuAbout_Click</c> to open after <c>ShowDialog</c> returns —
    /// the same read-back convention <see cref="EmojiPickerWindow.SelectedShortcode"/> uses, rather
    /// than this dialog opening the file itself. Two reasons it isn't done here: MainWindow owns
    /// <c>OpenFile</c> along with the unsaved-changes guard and error reporting, and shelling the
    /// file out to its default handler would launch a *second* MDEdit instance whenever MDEdit is
    /// registered for <c>.md</c> — which it is, for anyone who has used Register File Associations.
    /// </remarks>
    public string? RequestedDocumentPath { get; private set; }

    public AboutWindow()
    {
        InitializeComponent();

        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        var buildDate = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "BuildDate")?.Value;

        VersionText.Text = $"Version {version}";
        BuildDateText.Text = $"Built {buildDate}";
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e) => Close();

    // Both files sit beside the exe (see the Content items in MDEdit.csproj). Deliberately not
    // marked read-only the way ReleaseNotes.md is: that protection exists because ReleaseNotes.md
    // invites editing as a sample document, and it costs a build target (ClearStaleReleaseNotesReadOnly)
    // to undo afterwards. Nobody edits a licence, so there is nothing to protect and no reason to
    // create a second stale-attribute problem.
    private void LicenceLink_Click(object sender, RoutedEventArgs e) => RequestDocument("LICENSE.txt");

    private void ThirdPartyLink_Click(object sender, RoutedEventArgs e)
        => RequestDocument("THIRD-PARTY-NOTICES.md");

    private void RequestDocument(string fileName)
    {
        RequestedDocumentPath = Path.Combine(AppContext.BaseDirectory, fileName);
        Close();
    }
}
