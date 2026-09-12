using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitTree.Core;

namespace GitTree.App.ViewModels;

public partial class CloneRepositoryViewModel : ViewModelBase
{
    private string _lastSuggestion;
    private bool _updatingDestination;

    public CloneRepositoryViewModel(string? lastParentDirectory = null)
    {
        ParentDirectory = string.IsNullOrWhiteSpace(lastParentDirectory)
            ? GitClonePaths.DefaultParentDirectory()
            : lastParentDirectory.Trim();
        Destination = ParentDirectory;
        _lastSuggestion = Destination;
    }

    public Window? Host { get; set; }
    public bool Confirmed { get; private set; }
    public event Action? CloseRequested;

    [ObservableProperty] private string _url = "";
    [ObservableProperty] private string _destination = "";
    [ObservableProperty] private string _errorMessage = "";

    public string ParentDirectory { get; private set; }

    public bool CanClone =>
        !string.IsNullOrWhiteSpace(Url)
        && !string.IsNullOrWhiteSpace(Destination)
        && !string.IsNullOrWhiteSpace(GitClonePaths.FolderNameFromUrl(Url));

    partial void OnUrlChanged(string value)
    {
        var next = GitClonePaths.NextDestination(ParentDirectory, value, Destination, _lastSuggestion);
        _lastSuggestion = GitClonePaths.SuggestDestination(ParentDirectory, value);
        if (!string.Equals(next, Destination, StringComparison.Ordinal))
        {
            _updatingDestination = true;
            Destination = next;
            _updatingDestination = false;
        }

        CloneCommand.NotifyCanExecuteChanged();
    }

    partial void OnDestinationChanged(string value)
    {
        if (!_updatingDestination)
            ParentDirectory = GitClonePaths.ResolveParent(value, Url);
        CloneCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (Host is null)
            return;

        var folders = await Host.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose parent folder for the clone",
            AllowMultiple = false
        });
        var folder = folders.FirstOrDefault();
        if (folder is null)
            return;

        ParentDirectory = folder.Path.LocalPath;
        var suggested = GitClonePaths.SuggestDestination(ParentDirectory, Url);
        _lastSuggestion = suggested;
        _updatingDestination = true;
        Destination = suggested;
        _updatingDestination = false;
        CloneCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanClone))]
    private void Clone()
    {
        if (!CanClone)
            return;
        Confirmed = true;
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        Confirmed = false;
        CloseRequested?.Invoke();
    }
}
