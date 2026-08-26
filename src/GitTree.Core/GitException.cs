namespace GitTree.Core;

public sealed class GitException : Exception
{
    public int ExitCode { get; }
    public string StdErr { get; }
    public string Command { get; }

    public GitException(string command, int exitCode, string stdErr, string? stdOut = null)
        : base(BuildMessage(command, exitCode, stdErr, stdOut))
    {
        Command = command;
        ExitCode = exitCode;
        StdErr = stdErr;
    }

    private static string BuildMessage(string command, int exitCode, string stdErr, string? stdOut)
    {
        var detail = string.IsNullOrWhiteSpace(stdErr) ? stdOut : stdErr;
        return $"git {command} failed ({exitCode}): {detail}".Trim();
    }
}
