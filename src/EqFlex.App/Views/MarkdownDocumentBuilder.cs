using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace EqFlex.App.Views;

/// <summary>
/// Converts a small subset of Markdown (##, ###, bullet lists, plain paragraphs)
/// into a WPF FlowDocument. Used by UpdateDialog and the Settings changelog viewer.
/// </summary>
internal static class MarkdownDocumentBuilder
{
    public static FlowDocument Build(string markdown, Brush textBrush, Brush headingBrush)
    {
        var doc = new FlowDocument
        {
            FontFamily  = new FontFamily("Segoe UI"),
            FontSize    = 12,
            Foreground  = textBrush,
            Background  = Brushes.Transparent,
            PagePadding = new Thickness(0),
        };

        List? currentList = null;
        foreach (var rawLine in markdown.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("## "))
            {
                FlushList(doc, ref currentList);
                doc.Blocks.Add(new Paragraph(new Run(line[3..]))
                {
                    FontSize   = 15,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = headingBrush,
                    Margin     = new Thickness(0, 8, 0, 2),
                });
            }
            else if (line.StartsWith("### "))
            {
                FlushList(doc, ref currentList);
                doc.Blocks.Add(new Paragraph(new Run(line[4..]))
                {
                    FontSize   = 13,
                    FontWeight = FontWeights.SemiBold,
                    Margin     = new Thickness(0, 6, 0, 2),
                });
            }
            else if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                currentList ??= new List
                {
                    MarkerStyle = TextMarkerStyle.Disc,
                    Margin      = new Thickness(16, 0, 0, 0),
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
    }

    private static void FlushList(FlowDocument doc, ref List? list)
    {
        if (list is null) return;
        doc.Blocks.Add(list);
        list = null;
    }
}
