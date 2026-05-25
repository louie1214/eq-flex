using System.Windows;
using EqFlex.App.ViewModels;

namespace EqFlex.App.Views;

public partial class ImportShareDialog : Window
{
    public ImportShareDialog()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ImportShareViewModel vm)
                vm.ImportCompleted += () => { DialogResult = true; Close(); };
        };
        Loaded += (_, _) => CodeBox.Focus();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
