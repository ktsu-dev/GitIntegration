// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;

/// <summary>
/// The shared behaviour of every git command builder: global argument injection, execution, and
/// failure translation.
/// </summary>
/// <typeparam name="TResult">The parsed result type.</typeparam>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">
/// The repository to scope the command to, or <see langword="null"/> for commands that are not
/// repository-scoped, such as <c>init</c>, <c>clone</c>, and <c>--version</c>.
/// </param>
public abstract class GitCommandBuilder<TResult>(IGitProcessRunner runner, AbsoluteDirectoryPath? repositoryPath)
	: IGitCommandBuilder<TResult>
{
	private IGitProcessRunner Runner { get; } = Ensure.NotNull(runner);

	/// <summary>
	/// Gets the repository this command is scoped to, if any.
	/// </summary>
	protected AbsoluteDirectoryPath? RepositoryPath { get; } = repositoryPath;

	/// <summary>
	/// Appends the verb and its options to the argument vector, after the global arguments.
	/// </summary>
	/// <param name="arguments">The vector being assembled.</param>
	[SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "The task 6 brief mandates this exact signature verbatim, since every verb builder in Phases 3-5 overrides it and simply appends to the caller-owned vector; wrapping it in Collection<T> would add no safety here and would break the required override signature across every future builder.")]
	protected abstract void AppendVerbArguments(List<string> arguments);

	/// <summary>
	/// Turns a successful invocation's output into the result type.
	/// </summary>
	/// <param name="result">The raw invocation outcome.</param>
	/// <returns>The parsed result.</returns>
	protected abstract TResult ParseResult(GitProcessResult result);

	/// <inheritdoc />
	public IReadOnlyList<string> BuildArguments()
	{
		List<string> arguments = [];

		if (RepositoryPath is not null)
		{
			// RunCommand cannot set a process working directory, so the repository is selected
			// with -C rather than by launching git inside it.
			arguments.Add("-C");
			arguments.Add(RepositoryPath.WeakString);
		}

		// Git must never block on a pager, must not octal-escape non-ASCII paths, and must not
		// emit ANSI colour codes, or the output stops being parseable.
		arguments.Add("--no-pager");
		arguments.Add("-c");
		arguments.Add("core.quotepath=false");
		arguments.Add("-c");
		arguments.Add("color.ui=false");

		AppendVerbArguments(arguments);

		return arguments;
	}

	/// <inheritdoc />
	public async Task<TResult> ExecuteAsync(CancellationToken cancellationToken = default)
	{
		IReadOnlyList<string> arguments = BuildArguments();
		GitProcessResult result = await Runner.RunAsync(arguments, cancellationToken).ConfigureAwait(false);

		if (!result.Success)
		{
			throw CreateException(result);
		}

		return ParseResult(result);
	}

	/// <inheritdoc />
	public async Task<GitResult<TResult>> TryExecuteAsync(CancellationToken cancellationToken = default)
	{
		IReadOnlyList<string> arguments = BuildArguments();
		GitProcessResult result = await Runner.RunAsync(arguments, cancellationToken).ConfigureAwait(false);

		return result.Success
			? GitResult<TResult>.FromValue(ParseResult(result))
			: GitResult<TResult>.FromError(new GitCommandError
			{
				ExitCode = result.ExitCode,
				Arguments = result.Arguments,
				StandardError = result.StandardError,
			});
	}

	private static GitCommandException CreateException(GitProcessResult result)
	{
		string message = $"git exited with code {result.ExitCode}: {result.StandardError.Trim()}";

		// Git reports a missing working tree with a stable phrase and exit code 128. Surfacing it
		// as a distinct type lets callers distinguish "wrong directory" from "command failed".
		return result.StandardError.Contains("not a git repository", StringComparison.OrdinalIgnoreCase)
			? new GitRepositoryNotFoundException(message, result.ExitCode, result.Arguments, result.StandardError)
			: new GitCommandException(message, result.ExitCode, result.Arguments, result.StandardError);
	}
}
