using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EqFlex.App.Services;

namespace EqFlex.App.ViewModels;

public sealed partial class ShareTriggerViewModel : ObservableObject
{
    private readonly TriggerShareService _svc;
    private readonly TriggerFolderNode _folder;

    public string FolderName    { get; }
    public int    TriggerCount  { get; }

    [ObservableProperty] private string _authorName = string.Empty;
    [ObservableProperty] private bool   _isUploading;
    [ObservableProperty] private string _shareCode = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;

    public bool HasCode    => ShareCode.Length > 0;
    public string FullCode => $"{{FLEX:share/{ShareCode}}}";

    partial void OnShareCodeChanged(string value)
    {
        OnPropertyChanged(nameof(HasCode));
        OnPropertyChanged(nameof(FullCode));
        CopyCodeCommand.NotifyCanExecuteChanged();
    }

    public ShareTriggerViewModel(TriggerFolderNode folder, TriggerShareService svc)
    {
        _folder       = folder;
        _svc          = svc;
        FolderName    = folder.FolderName;
        TriggerCount  = TriggerShareService.CountTriggers(folder);
    }

    [RelayCommand]
    private async Task GenerateCode()
    {
        IsUploading  = true;
        ErrorMessage = string.Empty;
        ShareCode    = string.Empty;

        try
        {
            var package = _svc.Serialize(_folder, AuthorName);
            ShareCode = await _svc.UploadAsync(package);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsUploading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasCode))]
    private void CopyCode()
    {
        Clipboard.SetText(FullCode);
    }
}
