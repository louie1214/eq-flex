using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EqFlex.App.Services;
using EqFlex.Core.Models;

namespace EqFlex.App.ViewModels;

public sealed partial class ImportShareViewModel : ObservableObject
{
    private readonly TriggerShareService _svc;
    private TriggerSharePackage? _fetched;

    [ObservableProperty] private string _codeInput = string.Empty;
    [ObservableProperty] private bool   _isLoading;
    [ObservableProperty] private string _previewText = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool   _hasPreview;

    public bool CanImport => _fetched is not null && !IsLoading;

    partial void OnHasPreviewChanged(bool value) => ImportCommand.NotifyCanExecuteChanged();
    partial void OnIsLoadingChanged(bool value)  => ImportCommand.NotifyCanExecuteChanged();

    public ImportShareViewModel(TriggerShareService svc, string prefilledCode = "")
    {
        _svc      = svc;
        CodeInput = prefilledCode;
    }

    [RelayCommand]
    private async Task Preview()
    {
        var code = TriggerShareService.ExtractCode(CodeInput);
        if (code.Length != 8)
        {
            ErrorMessage = "Invalid code. Enter an 8-character share code or paste {FLEX:share/XXXXXXXX}.";
            return;
        }

        IsLoading    = true;
        ErrorMessage = string.Empty;
        PreviewText  = string.Empty;
        HasPreview   = false;
        _fetched     = null;

        try
        {
            _fetched = await _svc.FetchAsync(code);
            if (_fetched is null)
            {
                ErrorMessage = $"Code not found: {code}. It may have expired (codes last 90 days).";
                return;
            }

            int totalFolders  = CountFolders(_fetched.Folders);
            int totalTriggers = CountTriggers(_fetched.Folders);

            var lines = new List<string>();
            foreach (var f in _fetched.Folders)
                lines.Add($"📁 {f.Name}");

            PreviewText = string.Join(Environment.NewLine, lines)
                + $"{Environment.NewLine}{Environment.NewLine}"
                + $"{totalTriggers} trigger{(totalTriggers == 1 ? "" : "s")} in {totalFolders} folder{(totalFolders == 1 ? "" : "s")}"
                + (string.IsNullOrEmpty(_fetched.Author) ? "" : $"{Environment.NewLine}Shared by: {_fetched.Author}")
                + $"{Environment.NewLine}Created: {_fetched.CreatedAt:yyyy-MM-dd}";

            HasPreview = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to fetch: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private void Import()
    {
        if (_fetched is null) return;
        _svc.Import(_fetched);
        // Signal dialog to close with success
        ImportCompleted?.Invoke();
    }

    public event Action? ImportCompleted;

    private static int CountFolders(List<ShareFolder> folders)
    {
        int n = folders.Count;
        foreach (var f in folders) n += CountFolders(f.Children);
        return n;
    }

    private static int CountTriggers(List<ShareFolder> folders)
    {
        int n = 0;
        foreach (var f in folders)
        {
            n += f.Triggers.Count;
            n += CountTriggers(f.Children);
        }
        return n;
    }
}
