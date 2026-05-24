using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EqFlex.App.Services;
using EqFlex.Core.Services;
using EqFlex.Infrastructure.Storage;
using Trigger = EqFlex.Core.Models.Trigger;
using TriggerAction = EqFlex.Core.Models.TriggerAction;
using TriggerActionType = EqFlex.Core.Models.TriggerActionType;
using CapturePhrase = EqFlex.Core.Models.CapturePhrase;
using TriggerFolder = EqFlex.Core.Models.TriggerFolder;

namespace EqFlex.App.ViewModels;

public sealed partial class TriggersViewModel : ObservableObject
{
    private readonly TriggerStore _store;
    private readonly TriggerEngine _engine;
    private readonly Services.OverlayManager _overlayManager;

    // ── Tree state ────────────────────────────────────────────────────────────

    public ObservableCollection<TriggerFolderNode> FolderNodes { get; } = [];

    private TriggerFolderNode? _selectedFolderNode;
    private TriggerNode?       _selectedTriggerNode;

    [ObservableProperty] private bool _hasTreeSelection;
    [ObservableProperty] private bool _isSelectMode;
    [ObservableProperty] private bool _isLoading;

    /// <summary>Raised after Reload when a specific item should be selected in the TreeView.</summary>
    public event Action<object>? ItemSelectRequested;

    // ── Edit state (right-panel trigger editor) ───────────────────────────────

    [ObservableProperty] private Trigger? _selectedTrigger;
    [ObservableProperty] private string _editName = string.Empty;
    [ObservableProperty] private bool _editEnabled = true;
    [ObservableProperty] private double _editCooldownSec;
    [ObservableProperty] private ObservableCollection<CapturePhrase> _editPhrases = [];
    [ObservableProperty] private ObservableCollection<TriggerAction> _editActions = [];
    [ObservableProperty] private CapturePhrase? _selectedPhrase;
    [ObservableProperty] private TriggerAction? _selectedAction;
    [ObservableProperty] private bool _hasSelection;

    // ── Action mirror properties ──────────────────────────────────────────────

    [ObservableProperty] private string _editActionColor = "#FFD4D4D4";
    [ObservableProperty] private double _editActionFontSize = 13;
    [ObservableProperty] private bool _editActionBold;
    [ObservableProperty] private string _editActionTimerBarColor = "#FF007ACC";
    private bool _syncingAction;

    // ── Overlay + folder pickers ──────────────────────────────────────────────

    public sealed record OverlayOption(int Id, string Name);
    public IReadOnlyList<OverlayOption> AvailableOverlays { get; private set; } = [];

    public TriggersViewModel(TriggerStore store, TriggerEngine engine, Services.OverlayManager overlayManager)
    {
        _store = store;
        _engine = engine;
        _overlayManager = overlayManager;
        Reload();
    }

    // ── Select mode ───────────────────────────────────────────────────────────

    partial void OnIsSelectModeChanged(bool value)
    {
        if (!value)
            foreach (var fn in FolderNodes)
                fn.IsMarked = false;   // propagates to all children
    }

    /// <summary>
    /// Count of triggers (including those inside marked folders) that would be deleted.
    /// Marked folders are counted by their full trigger contents so the confirmation
    /// dialog shows an accurate impact number.
    /// </summary>
    public int CountMarked()
    {
        int n = 0;
        CountMarkedInTree(FolderNodes, ref n);
        return n;
    }

    public void DeleteMarked()
    {
        var triggerIds = new List<int>();
        var folderIds  = new List<int>();
        CollectMarkedForDeletion(FolderNodes, triggerIds, folderIds);
        foreach (var id in triggerIds) _store.Delete(id);
        foreach (var id in folderIds)  _store.DeleteFolder(id);
        IsSelectMode = false;
        Reload();
    }

    private static void CountMarkedInTree(IEnumerable<TriggerFolderNode> nodes, ref int n)
    {
        foreach (var fn in nodes)
        {
            if (fn.IsMarked)
            {
                CountEntireSubtree(fn, ref n);   // folder is marked → count everything inside
            }
            else
            {
                foreach (var child in fn.Children)
                {
                    if (child is TriggerNode tn && tn.IsMarked) n++;
                    else if (child is TriggerFolderNode sub) CountMarkedInTree([sub], ref n);
                }
            }
        }
    }

    private static void CountEntireSubtree(TriggerFolderNode node, ref int n)
    {
        foreach (var child in node.Children)
        {
            if (child is TriggerNode) n++;
            else if (child is TriggerFolderNode sub) CountEntireSubtree(sub, ref n);
        }
    }

    private static void CollectMarkedForDeletion(
        IEnumerable<TriggerFolderNode> nodes, List<int> triggerIds, List<int> folderIds)
    {
        foreach (var fn in nodes)
        {
            if (fn.IsMarked)
            {
                // Entire subtree goes + the folder itself
                CollectSubtreeIds(fn, triggerIds, folderIds);
                folderIds.Add(fn.FolderId);
            }
            else
            {
                foreach (var child in fn.Children)
                {
                    if (child is TriggerNode tn && tn.IsMarked)
                        triggerIds.Add(tn.Trigger.Id);
                    else if (child is TriggerFolderNode sub)
                        CollectMarkedForDeletion([sub], triggerIds, folderIds);
                }
            }
        }
    }

    // ── Selection ─────────────────────────────────────────────────────────────

    public void SetSelection(object? item)
    {
        if (item is TriggerFolderNode folder)
        {
            _selectedFolderNode  = folder;
            _selectedTriggerNode = null;
            SelectedTrigger      = null;
            HasTreeSelection     = true;
        }
        else if (item is TriggerNode triggerNode)
        {
            _selectedFolderNode  = FindContainingFolder(triggerNode.Trigger.Id, FolderNodes);
            _selectedTriggerNode = triggerNode;
            SelectedTrigger      = triggerNode.Trigger;
            HasTreeSelection     = true;
        }
        else
        {
            _selectedFolderNode  = null;
            _selectedTriggerNode = null;
            SelectedTrigger      = null;
            HasTreeSelection     = false;
        }
        AddTriggerCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedTriggerChanged(Trigger? value)
    {
        HasSelection = value is not null;
        if (value is not null) BeginEdit(value);
    }

    partial void OnHasSelectionChanged(bool value) =>
        SaveTriggerCommand.NotifyCanExecuteChanged();

    // ── Reload ────────────────────────────────────────────────────────────────

    private async void Reload(int? reSelectTriggerId = null, int? reSelectFolderId = null)
    {
        IsLoading = true;

        // DB queries on background thread — these dominate load time with large trigger sets
        var (allFolders, allTriggers) = await Task.Run(
            () => (_store.GetAllFolders(), _store.GetAll()));

        var childFolders = allFolders
            .GroupBy(f => f.ParentFolderId)
            .ToDictionary(g => g.Key, g => g.OrderBy(f => f.Name).ToList());
        var trigsByFolder = allTriggers
            .GroupBy(t => t.FolderId)
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.Name).ToList());

        FolderNodes.Clear();
        foreach (var root in childFolders.GetValueOrDefault(0, []))
            FolderNodes.Add(BuildFolderNode(root, childFolders, trigsByFolder));

        AvailableOverlays = new[] { new OverlayOption(0, "(Default)") }
            .Concat(_overlayManager.Overlays
                .Select(vm => new OverlayOption(vm.OverlayId, vm.OverlayName)))
            .ToList();
        ReloadEngine();
        IsLoading = false;

        // Restore tree selection
        if (reSelectTriggerId.HasValue)
        {
            var hit = FindTriggerInTree(reSelectTriggerId.Value, FolderNodes);
            if (hit is var (fn, tn))
            {
                _selectedFolderNode = fn; _selectedTriggerNode = tn;
                SelectedTrigger = tn.Trigger; HasTreeSelection = true;
                AddTriggerCommand.NotifyCanExecuteChanged();
                DeleteSelectedCommand.NotifyCanExecuteChanged();
                ItemSelectRequested?.Invoke(tn);
            }
        }
        else if (reSelectFolderId.HasValue)
        {
            var fn = FindFolderInTree(reSelectFolderId.Value, FolderNodes);
            if (fn != null)
            {
                _selectedFolderNode = fn; _selectedTriggerNode = null;
                SelectedTrigger = null; HasTreeSelection = true;
                AddTriggerCommand.NotifyCanExecuteChanged();
                DeleteSelectedCommand.NotifyCanExecuteChanged();
                ItemSelectRequested?.Invoke(fn);
                fn.IsRenaming = true;
            }
        }
    }

    private TriggerFolderNode BuildFolderNode(
        TriggerFolder folder,
        Dictionary<int, List<TriggerFolder>> childFolders,
        Dictionary<int, List<Trigger>> trigsByFolder)
    {
        var node = new TriggerFolderNode(folder, f => { _store.SaveFolder(f); ReloadEngine(); });
        // Sub-folders first
        foreach (var sub in childFolders.GetValueOrDefault(folder.Id, []))
            node.Children.Add(BuildFolderNode(sub, childFolders, trigsByFolder));
        // Then triggers
        foreach (var t in trigsByFolder.GetValueOrDefault(folder.Id, []))
            node.Children.Add(new TriggerNode(t, tr => { _store.Save(tr); ReloadEngine(); }));
        return node;
    }

    private void ReloadEngine()
    {
        var active = new List<Trigger>();
        CollectActiveFromTree(FolderNodes, parentEnabled: true, active);
        _engine.LoadTriggers(active);
    }

    private static void CollectActiveFromTree(
        IEnumerable<TriggerFolderNode> nodes, bool parentEnabled, List<Trigger> active)
    {
        foreach (var fn in nodes)
        {
            var enabled = parentEnabled && fn.FolderEnabled;
            foreach (var child in fn.Children)
            {
                if (child is TriggerNode tn && enabled && tn.IsEnabled) active.Add(tn.Trigger);
                else if (child is TriggerFolderNode sub) CollectActiveFromTree([sub], enabled, active);
            }
        }
    }

    // ── Tree search helpers ───────────────────────────────────────────────────

    private static (TriggerFolderNode, TriggerNode)? FindTriggerInTree(
        int id, IEnumerable<TriggerFolderNode> nodes)
    {
        foreach (var fn in nodes)
            foreach (var child in fn.Children)
            {
                if (child is TriggerNode tn && tn.Trigger.Id == id) return (fn, tn);
                if (child is TriggerFolderNode sub)
                {
                    var found = FindTriggerInTree(id, [sub]);
                    if (found != null) return found;
                }
            }
        return null;
    }

    private static TriggerFolderNode? FindFolderInTree(int id, IEnumerable<TriggerFolderNode> nodes)
    {
        foreach (var fn in nodes)
        {
            if (fn.FolderId == id) return fn;
            var found = FindFolderInTree(id, fn.Children.OfType<TriggerFolderNode>());
            if (found != null) return found;
        }
        return null;
    }

    private static TriggerFolderNode? FindContainingFolder(int triggerId, IEnumerable<TriggerFolderNode> nodes)
    {
        foreach (var fn in nodes)
        {
            if (fn.Children.OfType<TriggerNode>().Any(t => t.Trigger.Id == triggerId)) return fn;
            var found = FindContainingFolder(triggerId, fn.Children.OfType<TriggerFolderNode>());
            if (found != null) return found;
        }
        return null;
    }

    private static void CollectSubtreeIds(TriggerFolderNode node, List<int> triggerIds, List<int> folderIds)
    {
        foreach (var child in node.Children)
        {
            if (child is TriggerNode tn) triggerIds.Add(tn.Trigger.Id);
            else if (child is TriggerFolderNode sub)
            {
                CollectSubtreeIds(sub, triggerIds, folderIds);
                folderIds.Add(sub.FolderId);
            }
        }
    }

    private void BeginEdit(Trigger t)
    {
        EditName        = t.Name;
        EditEnabled     = t.IsEnabled;
        EditCooldownSec = t.CooldownSec;
        EditPhrases     = new ObservableCollection<CapturePhrase>(t.Phrases.Select(p => new CapturePhrase
        {
            Pattern  = p.Pattern,
            UseRegex = p.UseRegex
        }));
        EditActions    = new ObservableCollection<TriggerAction>(t.Actions.Select(CloneAction));
        SelectedPhrase = null;
        SelectedAction = null;
    }

    private static TriggerAction CloneAction(TriggerAction a) => new()
    {
        ActionType    = a.ActionType, Text = a.Text, DurationSec = a.DurationSec,
        TextColor     = a.TextColor,  FontSize = a.FontSize, IsBold = a.IsBold,
        AudioPath     = a.AudioPath,  SpeakInterrupt = a.SpeakInterrupt, OverlayId = a.OverlayId
    };

    // ── Import ────────────────────────────────────────────────────────────────

    public NagImporter.ImportResult ImportFromNag(string jsonPath)
    {
        var result = NagImporter.Import(jsonPath, _store, _overlayManager);
        if (result.Error is null) Reload();
        return result;
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void AddFolder()
    {
        // Create under the currently selected folder (subfolder), or at root if none selected.
        var parentId = _selectedFolderNode?.FolderId ?? 0;
        var folder = new TriggerFolder { Name = "New Folder", ParentFolderId = parentId };
        _store.SaveFolder(folder);
        Reload(reSelectFolderId: folder.Id);
    }

    [RelayCommand(CanExecute = nameof(HasTreeSelection))]
    private void AddTrigger()
    {
        if (_selectedFolderNode is null) return;
        var t = new Trigger { Name = "New Trigger", FolderId = _selectedFolderNode.FolderId };
        _store.Save(t);
        Reload(reSelectTriggerId: t.Id);
    }

    [RelayCommand(CanExecute = nameof(HasTreeSelection))]
    private void DeleteSelected()
    {
        if (_selectedTriggerNode != null)
        {
            _store.Delete(_selectedTriggerNode.Trigger.Id);
        }
        else if (_selectedFolderNode != null)
        {
            var trigIds = new List<int>();
            var fldIds  = new List<int>();
            CollectSubtreeIds(_selectedFolderNode, trigIds, fldIds);
            foreach (var id in trigIds) _store.Delete(id);
            foreach (var id in fldIds) _store.DeleteFolder(id);
            _store.DeleteFolder(_selectedFolderNode.FolderId);
        }

        _selectedFolderNode = null; _selectedTriggerNode = null;
        SelectedTrigger = null; HasTreeSelection = false;
        AddTriggerCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        Reload();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void SaveTrigger()
    {
        if (SelectedTrigger is null) return;
        var id = SelectedTrigger.Id;
        SelectedTrigger.Name        = EditName;
        SelectedTrigger.IsEnabled   = EditEnabled;
        SelectedTrigger.CooldownSec = EditCooldownSec;
        SelectedTrigger.Phrases     = [.. EditPhrases];
        SelectedTrigger.Actions     = [.. EditActions];
        _store.Save(SelectedTrigger);
        Reload(reSelectTriggerId: id);
    }

    // ── Phrase editing ────────────────────────────────────────────────────────

    [RelayCommand]
    private void AddPhrase() => EditPhrases.Add(new CapturePhrase());

    [RelayCommand]
    private void RemovePhrase()
    {
        if (SelectedPhrase is not null) EditPhrases.Remove(SelectedPhrase);
    }

    // ── Regex snippet helpers ─────────────────────────────────────────────────

    public sealed record SnippetItem(string Token, string Label, string Description);

    public IReadOnlyList<SnippetItem> SnippetTokens { get; } =
    [
        new("(.+)",        "(.+)",        "Any text — captured as {S1}"),
        new("(\\d+)",      "(\\d+)",      "Any number — captured as {S1}"),
        new("(\\w+)",      "(\\w+)",      "Single word — captured as {S1}"),
        new("(.+?)",       "(.+?)",       "Any text, lazy match"),
        new("{C}",         "{C}",         "Your character name"),
        new("(?<name>.+)", "(?<name>.+)", "Named capture — use as {name} in action text"),
    ];

    public IReadOnlyList<SnippetItem> SnippetTemplates { get; } =
    [
        new("You have slain (.+)!",               "You slain X",       "Fires when you kill something. {S1} = mob name."),
        new("(.+) has been slain by (.+)!",        "X slain by Y",      "{S1} = victim, {S2} = killer."),
        new("You begin casting (.+)\\.",            "You begin casting", "{S1} = spell name."),
        new("(.+) begins casting (.+)\\.",          "X begins casting",  "{S1} = caster, {S2} = spell."),
        new("(.+) has joined the group\\.",         "X joined group",    "{S1} = player name."),
        new("You have entered (.+)\\.",             "Zone entered",      "{S1} = zone name."),
        new("Your (.+) spell has worn off of (.+)\\.", "Your spell wore off", "{S1} = spell, {S2} = target."),
        new("(.+) tells you, '(.+)'",              "Tell received",     "{S1} = sender, {S2} = message."),
    ];

    [RelayCommand]
    private void CopySnippet(string? token)
    {
        if (!string.IsNullOrEmpty(token)) Clipboard.SetText(token);
    }

    // ── Action editing ────────────────────────────────────────────────────────

    [RelayCommand]
    private void AddAction()
    {
        EditActions.Add(new TriggerAction
        {
            ActionType  = TriggerActionType.DisplayText,
            DurationSec = 5,
            TextColor   = "#FFD4D4D4"
        });
    }

    partial void OnSelectedActionChanged(TriggerAction? value)
    {
        if (value is { ActionType: TriggerActionType.Timer, DurationSec: <= 5 })
            value.DurationSec = 30;
        _syncingAction = true;
        try
        {
            EditActionColor         = value?.TextColor ?? "#FFD4D4D4";
            EditActionFontSize      = value?.FontSize > 0 ? value.FontSize : 13;
            EditActionBold          = value?.IsBold ?? false;
            EditActionTimerBarColor = string.IsNullOrEmpty(value?.TimerBarColor)
                ? "#FF007ACC" : value.TimerBarColor;
        }
        finally { _syncingAction = false; }
    }

    partial void OnEditActionColorChanged(string value)
    {
        if (!_syncingAction && SelectedAction is not null) SelectedAction.TextColor = value;
    }

    partial void OnEditActionFontSizeChanged(double value)
    {
        if (!_syncingAction && SelectedAction is not null) SelectedAction.FontSize = value;
    }

    partial void OnEditActionBoldChanged(bool value)
    {
        if (!_syncingAction && SelectedAction is not null) SelectedAction.IsBold = value;
    }

    partial void OnEditActionTimerBarColorChanged(string value)
    {
        if (!_syncingAction && SelectedAction is not null) SelectedAction.TimerBarColor = value;
    }

    [RelayCommand]
    private void RemoveAction()
    {
        if (SelectedAction is not null) EditActions.Remove(SelectedAction);
    }
}
