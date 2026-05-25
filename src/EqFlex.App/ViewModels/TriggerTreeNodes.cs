using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using EqFlex.Core.Models;

namespace EqFlex.App.ViewModels;

/// <summary>
/// Tree node wrapping a TriggerFolder.
/// Children contains sub-folders (TriggerFolderNode) followed by triggers (TriggerNode).
/// Name/enabled changes auto-save via callback.
/// IsMarked propagates to all children for bulk-delete select mode.
/// </summary>
public sealed partial class TriggerFolderNode : ObservableObject
{
    private readonly Action<TriggerFolder> _save;
    private bool _propagating;

    public TriggerFolder Folder { get; }
    public int FolderId => Folder.Id;
    public bool IsFolder => true;

    [ObservableProperty] private string _folderName;
    [ObservableProperty] private bool _folderEnabled;
    [ObservableProperty] private bool _isExpanded = true;
    [ObservableProperty] private bool _isRenaming;
    [ObservableProperty] private bool _isMarked;

    /// <summary>Mixed collection: TriggerFolderNode sub-folders (first) then TriggerNode items.</summary>
    public ObservableCollection<object> Children { get; } = [];

    public TriggerFolderNode(TriggerFolder folder, Action<TriggerFolder> save)
    {
        Folder = folder;
        _save = save;
        _folderName = folder.Name;
        _folderEnabled = folder.IsEnabled;
    }

    partial void OnFolderNameChanged(string value)
    {
        Folder.Name = value;
        _save(Folder);
    }

    partial void OnFolderEnabledChanged(bool value)
    {
        Folder.IsEnabled = value;
        _save(Folder);
    }

    partial void OnIsMarkedChanged(bool value)
    {
        if (_propagating) return;
        _propagating = true;
        foreach (var child in Children)
        {
            if (child is TriggerFolderNode sub) sub.IsMarked = value;
            else if (child is TriggerNode tn) tn.IsMarked = value;
        }
        _propagating = false;
    }
}

/// <summary>
/// Tree node wrapping a Trigger (leaf).
/// IsEnabled auto-saves. IsMarked is transient select-mode state — not persisted.
/// </summary>
public sealed partial class TriggerNode : ObservableObject
{
    private readonly Action<Trigger> _save;

    public Trigger Trigger { get; }
    public string DisplayName => Trigger.Name;

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _isMarked;

    public TriggerNode(Trigger trigger, Action<Trigger> save)
    {
        Trigger = trigger;
        _save = save;
        _isEnabled = trigger.IsEnabled;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        Trigger.IsEnabled = value;
        _save(Trigger);
    }

    public void RefreshDisplayName() => OnPropertyChanged(nameof(DisplayName));
}
