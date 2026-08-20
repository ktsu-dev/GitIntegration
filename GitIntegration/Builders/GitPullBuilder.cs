// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;

/// <summary>
/// Fetches from a remote and integrates the result into the current branch.
/// </summary>
/// <remarks>
/// Returns only success or failure. Everything <c>pull</c> prints is human prose with no porcelain
/// alternative, so rather than invent a parser for it this verb leaves the caller to ask
/// <c>Status()</c> and <c>Log()</c> what changed — both of which are precise. The one outcome worth
/// its own type is a conflict, because it leaves the repository mid-merge.
/// </remarks>
public interface IGitPullBuilder : IGitCommandBuilder<GitCompleted>
{
	/// <summary>Pulls from this remote instead of the branch's configured upstream.</summary>
	/// <param name="name">The remote to pull from.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	public IGitPullBuilder FromRemote(GitRemoteName name);

	/// <summary>Pulls this branch. Requires a remote, which git reads as the first operand.</summary>
	/// <remarks>
	/// The requirement is checked when the argument vector is built, not here: a caller may set the
	/// branch before the remote, so only the finished configuration knows whether the pair is
	/// complete. <c>BuildArguments</c> therefore throws <see cref="InvalidOperationException"/> for a
	/// branch with no remote — a configuration error, not I/O, so its purity is intact.
	/// </remarks>
	/// <param name="name">The branch to pull.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	public IGitPullBuilder WithBranch(GitBranchName name);

	/// <summary>
	/// Refuses to pull at all when the result would need a merge commit.
	/// </summary>
	/// <remarks>
	/// Cannot be combined with <see cref="Rebase"/>: the two mean opposite things about history —
	/// refuse to merge, versus rewrite to avoid needing one. A caller may set both in either order,
	/// so only the finished configuration can detect the contradiction; <c>BuildArguments</c> throws
	/// <see cref="InvalidOperationException"/> when both are set.
	/// </remarks>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitPullBuilder FastForwardOnly();

	/// <summary>
	/// Replays local commits on top of the fetched ones instead of merging.
	/// </summary>
	/// <remarks>
	/// Cannot be combined with <see cref="FastForwardOnly"/>, for the same reason:
	/// <c>BuildArguments</c> throws <see cref="InvalidOperationException"/> when both are set, since
	/// only the finished configuration can detect the contradiction.
	/// </remarks>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitPullBuilder Rebase();

	/// <summary>Deletes remote-tracking branches whose counterparts are gone, as part of the fetch.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitPullBuilder Prune();

	/// <summary>Reports git's progress output as it arrives.</summary>
	/// <param name="progress">The sink to report to. Must be thread-safe.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="progress"/> is <see langword="null"/>.</exception>
	public IGitPullBuilder ReportingProgress(IProgress<string> progress);
}

/// <summary>
/// Builds <c>git pull</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
internal sealed class GitPullBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)
	: GitCommandBuilder<GitCompleted>(runner, repositoryPath), IGitPullBuilder
{
	private GitRemoteName? _remote;
	private GitBranchName? _branch;
	private bool _fastForwardOnly;
	private bool _rebase;
	private bool _prune;

	/// <inheritdoc />
	public IGitPullBuilder FromRemote(GitRemoteName name)
	{
		_remote = Ensure.NotNull(name);
		return this;
	}

	/// <inheritdoc />
	public IGitPullBuilder WithBranch(GitBranchName name)
	{
		_branch = Ensure.NotNull(name);
		return this;
	}

	/// <inheritdoc />
	public IGitPullBuilder FastForwardOnly()
	{
		_fastForwardOnly = true;
		return this;
	}

	/// <inheritdoc />
	public IGitPullBuilder Rebase()
	{
		_rebase = true;
		return this;
	}

	/// <inheritdoc />
	public IGitPullBuilder Prune()
	{
		_prune = true;
		return this;
	}

	/// <inheritdoc />
	public IGitPullBuilder ReportingProgress(IProgress<string> progress)
	{
		Progress = Ensure.NotNull(progress);
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		// git accepts both and lets one quietly win. They mean opposite things about history —
		// refuse to merge, versus rewrite so no merge is needed — so a caller who asked for both
		// has a bug worth reporting rather than a preference worth guessing.
		if (_fastForwardOnly && _rebase)
		{
			throw new InvalidOperationException(
				"FastForwardOnly and Rebase cannot both be requested: one refuses to create a merge, " +
				"the other rewrites history to avoid needing one.");
		}

		arguments.Add("pull");

		if (_fastForwardOnly)
		{
			arguments.Add("--ff-only");
		}

		if (_rebase)
		{
			arguments.Add("--rebase");
		}

		if (_prune)
		{
			arguments.Add("--prune");
		}

		if (_remote is null)
		{
			// git pull <refspec> with no remote reads the first operand as the remote, so a branch
			// on its own would be silently misinterpreted rather than rejected.
			if (_branch is not null)
			{
				throw new InvalidOperationException(
					"A branch was given without a remote. git reads the first operand as the remote name, " +
					"so call FromRemote as well.");
			}

			return;
		}

		if (_branch is null)
		{
			AppendOperands(arguments, _remote.WeakString);
			return;
		}

		AppendOperands(arguments, _remote.WeakString, _branch.WeakString);
	}

	/// <inheritdoc />
	protected override GitCompleted ParseResult(GitProcessResult result) =>
		new() { Arguments = Ensure.NotNull(result).Arguments };

	/// <summary>
	/// Classifies a failed pull, recognising a conflict as its own outcome.
	/// </summary>
	/// <remarks>
	/// Overridden because the base class inspects standard error while git announces a conflict on
	/// standard <em>output</em> — the same trap <c>commit</c> sets with "nothing to commit". Both a
	/// merge conflict and a rebase conflict carry the word CONFLICT, so one match covers both, and
	/// the <c>LC_ALL=C</c> that every invocation runs under is what makes it dependable.
	/// </remarks>
	/// <param name="result">The failed invocation outcome.</param>
	/// <returns>The exception to throw.</returns>
	protected override GitCommandException CreateException(GitProcessResult result)
	{
		Ensure.NotNull(result);

		return result.StandardOutput.Contains("CONFLICT", StringComparison.Ordinal)
			? new GitPullConflictException(
				"The pull left conflicts in the working tree. Use Status() to see which paths " +
				$"are unmerged: {result.StandardOutput.Trim()}",
				result.ExitCode,
				result.Arguments,
				result.StandardError)
			: base.CreateException(result);
	}

	/// <inheritdoc />
	public override async Task<GitResult<GitCompleted>> TryExecuteAsync(CancellationToken cancellationToken = default)
	{
		GitProcessResult result = await Runner.RunAsync(
			new GitProcessRequest { Arguments = BuildArguments(), Progress = Progress },
			cancellationToken).ConfigureAwait(false);

		if (result.Success)
		{
			return GitResult<GitCompleted>.FromValue(ParseResult(result));
		}

		// Pull is the second verb whose diagnostic lands on standard output, so an error built only
		// from standard error would carry the fetch progress and say nothing about the conflict. The
		// two are joined on a single newline, with the trailing newline trimmed from standard error
		// first, so the result reads as two legible lines rather than a run-on string.
		string standardError = result.StandardError.TrimEnd('\n', '\r');
		string diagnostic = standardError.Length == 0
			? result.StandardOutput
			: standardError + "\n" + result.StandardOutput;

		return GitResult<GitCompleted>.FromError(new GitCommandError
		{
			ExitCode = result.ExitCode,
			Arguments = result.Arguments,
			StandardError = diagnostic,
		});
	}
}
