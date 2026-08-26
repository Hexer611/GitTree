namespace GitTree.Core;

public sealed class SlashTreeNode<T>
{
    public required string Name { get; init; }
    public T? Value { get; init; }
    public required bool IsFolder { get; init; }
    public List<SlashTreeNode<T>> Children { get; } = [];
}

public static class SlashTree
{
    public static List<SlashTreeNode<T>> Build<T>(IEnumerable<(string Path, T Value)> items)
    {
        var root = new SlashTreeNode<T> { Name = "", IsFolder = true };
        foreach (var (path, value) in items)
        {
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
                continue;

            var current = root;
            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                var isLeaf = i == parts.Length - 1;
                if (isLeaf)
                {
                    current.Children.Add(new SlashTreeNode<T> { Name = part, Value = value, IsFolder = false });
                    continue;
                }

                var folder = current.Children.FirstOrDefault(c => c.IsFolder &&
                    string.Equals(c.Name, part, StringComparison.OrdinalIgnoreCase));
                if (folder is null)
                {
                    folder = new SlashTreeNode<T> { Name = part, IsFolder = true };
                    current.Children.Add(folder);
                }

                current = folder;
            }
        }

        Sort(root);
        return root.Children;
    }

    private static void Sort<T>(SlashTreeNode<T> node)
    {
        node.Children.Sort((a, b) =>
        {
            if (a.IsFolder != b.IsFolder)
                return a.IsFolder ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        foreach (var child in node.Children)
            Sort(child);
    }
}
