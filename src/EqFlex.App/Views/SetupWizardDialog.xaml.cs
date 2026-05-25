using System.Windows;
using EqFlex.App.ViewModels;

namespace EqFlex.App.Views;

public partial class SetupWizardDialog : Window
{
    private SetupWizardViewModel Vm => (SetupWizardViewModel)DataContext;

    public SetupWizardDialog()
    {
        InitializeComponent();
    }

    private void GetStarted_Click(object sender, RoutedEventArgs e) =>
        Vm.StartCommand.Execute(null);

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        Vm.MarkComplete();
        DialogResult = false;
        Close();
    }

    private void Finish_Click(object sender, RoutedEventArgs e)
    {
        Vm.MarkComplete();
        DialogResult = true;
        Close();
    }
}
