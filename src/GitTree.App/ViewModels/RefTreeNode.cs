using System.Collections.ObjectModel;
using GitTree.Core;

namespace GitTree.App.ViewModels;

public sealed class RefTreeNode
{
    public required string Label { get; init; }
    public bool IsFolder { get; init; }
    public bool IsCurrent { get; init; }
    public BranchRef? Branch { get; init; }
    public TagRef? Tag { get; init; }
    public StashEntry? Stash { get; init; }
    public WorktreeInfo? Worktree { get; init; }
    public bool IsBranch => Branch is not null;
    public bool IsInactiveBranch => IsBranch && !IsCurrent;
    public bool IsTag => Tag is not null;
    public bool IsStash => Stash is not null;
    public bool IsWorktree => Worktree is not null;
    public ObservableCollection<RefTreeNode> Children { get; } = [];

    public static ObservableCollection<RefTreeNode> FromBranches(IEnumerable<BranchRef> branches)
    {
        var tree = SlashTree.Build(branches.Select(b => (b.Name, b)));
        return ToCollection(tree, n => new RefTreeNode
        {
            Label = n.Name,
            IsFolder = n.IsFolder,
            IsCurrent = n.Value?.IsCurrent == true,
            Branch = n.Value
        });
    }

    public static ObservableCollection<RefTreeNode> FromTags(IEnumerable<TagRef> tags)
    {
        var tree = SlashTree.Build(tags.Select(t => (t.Name, t)));
        return ToCollection(tree, n => new RefTreeNode
        {
            Label = n.Name,
            IsFolder = n.IsFolder,
            Tag = n.Value
        });
    }

    public static ObservableCollection<RefTreeNode> FromStashes(IEnumerable<StashEntry> stashes)
    {
        var nodes = stashes.Select(s => new RefTreeNode
        {
            Label = s.Message,
            Stash = s
        });
        return new ObservableCollection<RefTreeNode>(nodes);
    }

    public static ObservableCollection<RefTreeNode> FromWorktrees(IEnumerable<WorktreeInfo> worktrees)
    {
        var nodes = worktrees.Select(w => new RefTreeNode
        {
            Label = w.Label,
            IsCurrent = w.IsCurrent,
            Worktree = w
        });
        return new ObservableCollection<RefTreeNode>(nodes);
    }

    private static ObservableCollection<RefTreeNode> ToCollection<T>(
        IEnumerable<SlashTreeNode<T>> source,
        Func<SlashTreeNode<T>, RefTreeNode> map)
    {
        var list = new ObservableCollection<RefTreeNode>();
        foreach (var node in source)
        {
            var vm = map(node);
            foreach (var child in ToCollection(node.Children, map))
                vm.Children.Add(child);
            list.Add(vm);
        }

        return list;
    }
}
