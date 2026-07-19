using System.Reflection;
using System.Windows;

namespace MDEdit;

public partial class AboutWindow : Window
{
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
}
