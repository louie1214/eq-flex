using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EqFlex.App.ViewModels;
using Microsoft.Win32;

namespace EqFlex.App.Views;

public partial class TriggersView : UserControl
{
    public TriggersView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not TriggersViewModel vm) return;
            vm.ItemSelectRequested    += SelectTreeItem;
            vm.ShareFolderRequested   += OnShareFolder;
            vm.ImportFromCodeRequested += OnImportFromCode;
        };
    }

    private void OnShareFolder(ViewModels.TriggerFolderNode folder)
    {
        if (DataContext is not TriggersViewModel vm) return;
        var dlgVm = new ViewModels.ShareTriggerViewModel(folder, vm.ShareService);
        var dlg   = new ShareTriggerDialog { DataContext = dlgVm, Owner = Window.GetWindow(this) };
        dlg.ShowDialog();
    }

    private void OnImportFromCode()
    {
        if (DataContext is not TriggersViewModel vm) return;
        OpenImportDialog(vm, prefilledCode: string.Empty);
    }

    internal void OpenImportDialog(TriggersViewModel vm, string prefilledCode)
    {
        var dlgVm = new ViewModels.ImportShareViewModel(vm.ShareService, prefilledCode);
        var dlg   = new ImportShareDialog { DataContext = dlgVm, Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            vm.NotifyImportComplete();
    }

    // ── Tree selection ────────────────────────────────────────────────────────

    private void TriggerTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is TriggersViewModel vm)
            vm.SetSelection(e.NewValue);
    }

    // ── Programmatic tree selection (after add/save) ──────────────────────────

    private void SelectTreeItem(object item)
    {
        // Walk the tree and set IsSelected on the matching TreeViewItem.
        if (SelectInContainer(TriggerTree, item)) return;

        // If the container isn't generated yet, force layout and retry.
        TriggerTree.UpdateLayout();
        SelectInContainer(TriggerTree, item);
    }

    private static bool SelectInContainer(ItemsControl container, object target)
    {
        foreach (var obj in container.Items)
        {
            if (container.ItemContainerGenerator.ContainerFromItem(obj) is not TreeViewItem tvi) continue;

            if (obj == target)
            {
                tvi.IsSelected = true;
                tvi.BringIntoView();
                return true;
            }

            // Expand and recurse into folders.
            if (obj is TriggerFolderNode)
            {
                tvi.IsExpanded = true;
                tvi.UpdateLayout();
                if (SelectInContainer(tvi, target)) return true;
            }
        }
        return false;
    }

    // ── Folder inline rename ──────────────────────────────────────────────────

    private void FolderName_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2) return;
        if (sender is TextBlock { DataContext: TriggerFolderNode node })
        {
            node.IsRenaming = true;
            // Focus the TextBox after it becomes visible.
            Dispatcher.BeginInvoke(() =>
            {
                if (sender is TextBlock { Parent: StackPanel sp })
                {
                    var tb = sp.Children.OfType<TextBox>().FirstOrDefault();
                    tb?.Focus();
                    tb?.SelectAll();
                }
            });
        }
        e.Handled = true;
    }

    private void FolderNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape)
            CommitFolderRename(sender);
    }

    private void FolderNameBox_LostFocus(object sender, RoutedEventArgs e) =>
        CommitFolderRename(sender);

    private static void CommitFolderRename(object sender)
    {
        if (sender is TextBox { DataContext: TriggerFolderNode node })
            node.IsRenaming = false;
    }

    // ── Audio browse ──────────────────────────────────────────────────────────

    private void ToggleSelectMode_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is TriggersViewModel vm) vm.IsSelectMode = !vm.IsSelectMode;
    }

    private void DeleteMarked_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TriggersViewModel vm) return;
        var count = vm.CountMarked();
        if (count == 0)
        {
            MessageBox.Show("No triggers are checked. Check the boxes next to triggers you want to delete.",
                "Nothing Selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var confirm = MessageBox.Show(
            $"Permanently delete {count} trigger{(count == 1 ? "" : "s")}?\n\n" +
            "Marked folders will be removed along with all their contents.",
            "Confirm Delete", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (confirm == MessageBoxResult.OK) vm.DeleteMarked();
    }

    private void ImportNag_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TriggersViewModel vm) return;

        var dlg = new OpenFileDialog
        {
            Title  = "Select NAG trigger-database.json",
            Filter = "NAG trigger database (trigger-database.json)|trigger-database.json|JSON files (*.json)|*.json|All files (*.*)|*.*",
        };

        // Default to the known NAG data folder if it exists
        var nagDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "electron-angular-eq-parse");
        if (Directory.Exists(nagDataDir)) dlg.InitialDirectory = nagDataDir;

        if (dlg.ShowDialog() != true) return;

        var result = vm.ImportFromNag(dlg.FileName);

        if (result.Error is not null)
        {
            MessageBox.Show($"Import failed:\n\n{result.Error}",
                "NAG Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        MessageBox.Show(
            $"Import complete.\n\n" +
            $"Triggers imported:  {result.Imported}\n" +
            $"Triggers skipped:   {result.Skipped}  (no phrases or only unsupported actions)\n" +
            $"Folders created:    {result.FoldersCreated}\n" +
            $"Overlays created:   {result.OverlaysCreated}" +
            (result.OverlaysCreated > 0
                ? "\n\nNew overlay windows were created to match your NAG overlays. " +
                  "Visit the Overlays page to position them."
                : ""),
            "NAG Import", MessageBoxButton.OK, MessageBoxImage.Information);
    }

}
