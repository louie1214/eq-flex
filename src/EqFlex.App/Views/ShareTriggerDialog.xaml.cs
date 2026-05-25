using System.Windows;

namespace EqFlex.App.Views;

public partial class ShareTriggerDialog : Window
{
    public ShareTriggerDialog() => InitializeComponent();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
