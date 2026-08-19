// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
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
	protected abstract void AppendVerbArguments(ICollection<string> arguments);

	/// <summary>
	/// Turns a successful invocation's output into the result type.
	/// </summary>
	/// <param name="result">The raw invocation outcome.</param>
	/// <returns>The parsed result.</returns>
	protected abstract TResult ParseResult(GitProcessResult result);

	/// <summary>
	/// Appends caller-supplied operands after an end-of-options marker, so a value beginning with a
	/// dash cannot be reinterpreted by git as an option.
	/// </summary>
	/// <param name="arguments">The vector being assembled.</param>
	/// <param name="operands">The operands to append.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="arguments"/> or <paramref name="operands"/> is <see langword="null"/>.
	/// </exception>
	protected static void AppendOperands(ICollection<string> arguments, params string[] operands)
	{
		Ensure.NotNull(arguments);
		Ensure.NotNull(operands);

		// Everything after --end-of-options is an operand, whatever it starts with. This is the
		// second layer behind NotAnOptionAttribute: the attribute stops a dash-leading value being
		// constructed at all, and this stops any that reaches the vector another way from being
		// parsed as a flag.
		arguments.Add("--end-of-options");

		foreach (string operand in operands)
		{
			arguments.Add(operand);
		}
	}

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
