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
	/// <summary>
	/// Gets the runner this builder executes through.
	/// </summary>
	/// <remarks>
	/// Exposed to derived builders because not every git operation is one invocation of one
	/// argument vector. <c>Commit</c> runs <c>commit</c> and then <c>log -1</c> to read back what
	/// was written, and <c>Fetch</c> assembles a vector that depends on a runtime probe of the
	/// installed git version. Neither is expressible through <see cref="BuildArguments"/> alone.
	/// </remarks>
	protected IGitProcessRunner Runner { get; } = Ensure.NotNull(runner);

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
	public virtual async Task<TResult> ExecuteAsync(CancellationToken cancellationToken = default)
	{
		GitProcessResult result = await Runner.RunAsync(
			new GitProcessRequest { Arguments = BuildArguments() },
			cancellationToken).ConfigureAwait(false);

		if (!result.Success)
		{
			throw CreateException(result);
		}

		return ParseResult(result);
	}

	/// <inheritdoc />
	public virtual async Task<GitResult<TResult>> TryExecuteAsync(CancellationToken cancellationToken = default)
	{
		GitProcessResult result = await Runner.RunAsync(
			new GitProcessRequest { Arguments = BuildArguments() },
			cancellationToken).ConfigureAwait(false);

		return result.Success
			? GitResult<TResult>.FromValue(ParseResult(result))
			: GitResult<TResult>.FromError(new GitCommandError
			{
				ExitCode = result.ExitCode,
				Arguments = result.Arguments,
				StandardError = result.StandardError,
			});
	}

	/// <summary>
	/// Classifies a failed invocation into an exception type.
	/// </summary>
	/// <remarks>
	/// Virtual so a derived builder can recognise the failures specific to its own verb — a
	/// <c>rev-parse</c> builder seeing "unknown revision", for instance — and fall back to
	/// <c>base.CreateException</c> for everything else.
	/// </remarks>
	/// <param name="result">The failed invocation outcome.</param>
	/// <returns>The exception to throw.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
	protected virtual GitCommandException CreateException(GitProcessResult result)
	{
		Ensure.NotNull(result);

		string message = $"git exited with code {result.ExitCode}: {result.StandardError.Trim()}";

		// Git reports a missing working tree with a stable phrase and exit code 128. Surfacing it
		// as a distinct type lets callers distinguish "wrong directory" from "command failed".
		//
		// The phrase match assumes an English git. ktsu.RunCommand cannot set environment
		// variables (upstream ktsu-dev/RunCommand issue #40), so LC_ALL=C cannot be forced; on a
		// machine with git's translation catalogs installed and a non-English locale active, the
		// match silently misses and the failure degrades to a generic GitCommandException rather
		// than GitRepositoryNotFoundException. That is a lossy but safe degradation: the exit code,
		// arguments, and stderr are still carried.
		return result.StandardError.Contains("not a git repository", StringComparison.OrdinalIgnoreCase)
			? new GitRepositoryNotFoundException(message, result.ExitCode, result.Arguments, result.StandardError)
			: new GitCommandException(message, result.ExitCode, result.Arguments, result.StandardError);
	}
}
