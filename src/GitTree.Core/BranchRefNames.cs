namespace GitTree.Core;

public static class BranchRefNames
{
    public static string? Validate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Enter a branch name.";

        var value = name.Trim();
        if (value.Equals("HEAD", StringComparison.Ordinal))
            return "HEAD is not a valid branch name.";
        if (!IsAcceptable(value))
            return "That branch name is not allowed.";
        return null;
    }

    public static string MergeRef(string branch) => "refs/heads/" + branch.Trim();

    public static string? BranchFromMerge(string? merge)
    {
        const string prefix = "refs/heads/";
        if (string.IsNullOrWhiteSpace(merge))
            return null;
        var value = merge.Trim();
        if (!value.StartsWith(prefix, StringComparison.Ordinal) || value.Length <= prefix.Length)
            return null;
        return value[prefix.Length..];
    }

    private static bool IsAcceptable(string value)
    {
        if (value.StartsWith('-') || value.StartsWith('/') || value.EndsWith('/'))
            return false;
        if (value.Contains("//", StringComparison.Ordinal)
            || value.Contains("..", StringComparison.Ordinal)
            || value.Contains("@{", StringComparison.Ordinal))
            return false;

        foreach (var part in value.Split('/'))
        {
            if (part.Length == 0
                || part.StartsWith('.')
                || part.EndsWith('.')
                || part.EndsWith(".lock", StringComparison.Ordinal))
                return false;

            foreach (var ch in part)
            {
                if (ch < 32 || ch == 127 || " ~^:?*[\\".Contains(ch))
                    return false;
            }
        }

        return true;
    }
}
