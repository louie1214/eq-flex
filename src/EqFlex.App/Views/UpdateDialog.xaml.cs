using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using EqFlex.App.Services;
using Velopack;

namespace EqFlex.App.Views;

public partial class UpdateDialog : Window
{
    private readonly UpdateService _svc;
    private readonly UpdateInfo _update;

    public UpdateDialog(UpdateService svc, UpdateInfo update)
    {
        InitializeComponent();
        _svc = svc;
        _update = update;

        VersionHeader.Text = $"EQ Flex {update.TargetFullRelease.Version} is available";

        var markdown = string.IsNullOrWhiteSpace(update.TargetFullRelease.NotesMarkdown)
            ? "No release notes provided."
            : update.TargetFullRelease.NotesMarkdown;
        NotesBox.Document = MarkdownDocumentBuilder.Build(
            markdown,
            (Brush)FindResource("TextSecondaryBrush"),
            (Brush)FindResource("AccentBrush"));
    }

    private void Later_Click(object sender, RoutedEventArgs e) => Close();

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        UpdateBtn.IsEnabled = false;
        LaterBtn.IsEnabled = false;
        DownloadProgress.Visibility = Visibility.Visible;

        await _svc.DownloadAndInstallAsync(_update, pct =>
            Dispatcher.InvokeAsync(() => DownloadProgress.Value = pct));
    }

}
