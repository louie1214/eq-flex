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
        NotesBox.Document = BuildNotesDocument(
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

    private static FlowDocument BuildNotesDocument(string markdown, Brush textBrush, Brush headingBrush)
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            Foreground = textBrush,
            Background = Brushes.Transparent,
            PagePadding = new Thickness(0),
        };

        DocList? currentList = null;
        foreach (var rawLine in markdown.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("## "))
            {
                FlushList(doc, ref currentList);
                doc.Blocks.Add(new Paragraph(new Run(line[3..]))
                {
                    FontSize = 15,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = headingBrush,
                    Margin = new Thickness(0, 8, 0, 2),
                });
            }
            else if (line.StartsWith("### "))
            {
                FlushList(doc, ref currentList);
                doc.Blocks.Add(new Paragraph(new Run(line[4..]))
                {
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 6, 0, 2),
                });
            }
            else if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                currentList ??= new DocList
                {
                    MarkerStyle = TextMarkerStyle.Disc,
                    Margin = new Thickness(16, 0, 0, 0),
                };
                currentList.ListItems.Add(new ListItem(new Paragraph(new Run(line[2..]))));
            }
            else
            {
                FlushList(doc, ref currentList);
                if (!string.IsNullOrWhiteSpace(line))
                    doc.Blocks.Add(new Paragraph(new Run(line)));
            }
        }
        FlushList(doc, ref currentList);
        return doc;

        static void FlushList(FlowDocument doc, ref DocList? list)
        {
            if (list is null) return;
            doc.Blocks.Add(list);
            list = null;
        }
    }

    // Alias avoids ambiguity with System.Collections.Generic.List<T>
    private sealed class DocList : System.Windows.Documents.List { }
}
