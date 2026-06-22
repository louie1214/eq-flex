using System.IO;
using System.Windows.Controls;
using System.Windows.Media;

namespace EqFlex.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        Loaded += (_, _) => PopulateChangelog();
    }

    private void PopulateChangelog()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "CHANGES.md");
        var markdown = File.Exists(path) ? File.ReadAllText(path) : "No changelog available.";
        ChangelogBox.Document = MarkdownDocumentBuilder.Build(
            markdown,
            (Brush)FindResource("TextSecondaryBrush"),
            (Brush)FindResource("AccentBrush"));
    }
}
