using CommunityToolkit.Mvvm.ComponentModel;

namespace GitTree.App.ViewModels;

public partial class RecentRepoItem : ObservableObject
{
    [ObservableProperty] private string _path = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _branch = "…";
    [ObservableProperty] private string _lastCommit = "";
    [ObservableProperty] private string _lastCommitWhen = "";
    [ObservableProperty] private string _statusLabel = "Loading";
    [ObservableProperty] private bool _exists = true;
    [ObservableProperty] private bool _isDirty;

    public string ParentFolder
    {
        get
        {
            var parent = System.IO.Path.GetDirectoryName(Path);
            return string.IsNullOrWhiteSpace(parent) ? Path : parent;
        }
    }

    partial void OnPathChanged(string value) => OnPropertyChanged(nameof(ParentFolder));
}
