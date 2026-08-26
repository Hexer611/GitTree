using CliWrap;
using CliWrap.Buffered;
using GitTree.Core;

namespace GitTree.Git.Cli;

public sealed class GitCliRunner
{
    private readonly string _workingDirectory;

    public GitCliRunner(string workingDirectory)
    {
        _workingDirectory = workingDirectory;
    }

    public async Task<string> RunAsync(IReadOnlyList<string> args, bool throwOnError = true, CancellationToken cancellationToken = default)
    {
        var result = await CliWrap.Cli.Wrap("git")
            .WithWorkingDirectory(_workingDirectory)
            .WithArguments(args)
            .WithValidation(CommandResultValidation.None)
            .WithEnvironmentVariables(new Dictionary<string, string?>
            {
                ["GIT_TERMINAL_PROMPT"] = "0",
                ["GIT_OPTIONAL_LOCKS"] = "0",
                ["GIT_PAGER"] = "cat"
            })
            .ExecuteBufferedAsync(cancellationToken);

        if (throwOnError && result.ExitCode != 0)
            throw new GitException(string.Join(' ', args), result.ExitCode, result.StandardError, result.StandardOutput);

        return result.StandardOutput;
    }

    public async Task<string> RunWithEditorTrueAsync(IReadOnlyList<string> args, CancellationToken cancellationToken = default)
    {
        var result = await CliWrap.Cli.Wrap("git")
            .WithWorkingDirectory(_workingDirectory)
            .WithArguments(args)
            .WithValidation(CommandResultValidation.None)
            .WithEnvironmentVariables(new Dictionary<string, string?>
            {
                ["GIT_TERMINAL_PROMPT"] = "0",
                ["GIT_EDITOR"] = "true",
                ["EDITOR"] = "true",
                ["GIT_SEQUENCE_EDITOR"] = "true"
            })
            .ExecuteBufferedAsync(cancellationToken);

        if (result.ExitCode != 0)
            throw new GitException(string.Join(' ', args), result.ExitCode, result.StandardError, result.StandardOutput);

        return result.StandardOutput;
    }
}
