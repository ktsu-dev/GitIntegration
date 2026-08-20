// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;

/// <summary>
/// Sends local commits to a remote.
/// </summary>
/// <remarks>
/// The one verb in this library whose two entry points differ in more than exception-versus-result,
/// because a rejected push exits non-zero while still printing a complete account of every
/// reference. <see cref="IGitCommandBuilder{TResult}.ExecuteAsync"/> stays strict and throws
/// <see cref="GitPushRejectedException"/>, which carries that account so nothing is lost.
/// <see cref="IGitCommandBuilder{TResult}.TryExecuteAsync"/> returns the account as a value and
/// leaves the caller to check <see cref="GitPushResult.HasRejections"/> — git ran and said exactly
/// what happened, which is what "try" should surface.
/// </remarks>
public interface IGitPushBuilder : IGitCommandBuilder<GitPushResult>
{
	/// <summary>Pushes to this remote instead of the branch's configured upstream.</summary>
	/// <param name="name">The remote to push to.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	public IGitPushBuilder ToRemote(GitRemoteName name);

	/// <summary>
	/// Pushes this branch. Requires a remote, which git reads as the first positional operand.
	/// </summary>
	/// <remarks>
	/// The requirement is checked when the argument vector is built, not here: a caller may set the
	/// branch before the remote, so only the finished configuration knows whether the pair is
	/// complete. <c>BuildArguments</c> therefore throws <see cref="InvalidOperationException"/> for
	/// a branch with no remote — a configuration error, not I/O, so its purity is intact.
	/// </remarks>
	/// <param name="name">The branch to push.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	public IGitPushBuilder WithBranch(GitBranchName name);

	/// <summary>Records the pushed branch as the local branch's upstream.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitPushBuilder SettingUpstream();

	/// <summary>Overwrites the remote branch even when that discards commits.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitPushBuilder Force();

	/// <summary>
	/// Overwrites the remote branch, but only if it has not moved since it was last fetched.
	/// </summary>
	/// <remarks>Replaces <see cref="Force"/> when both are requested, being the safer of the two.</remarks>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitPushBuilder ForceWithLease();

	/// <summary>Deletes the named branch on the remote rather than updating it.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitPushBuilder DeletingRemoteBranch();

	/// <summary>Reports what would happen without changing anything on the remote.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitPushBuilder DryRun();

	/// <summary>Reports git's progress output as it arrives.</summary>
	/// <param name="progress">The sink to report to. Must be thread-safe.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="progress"/> is <see langword="null"/>.</exception>
	public IGitPushBuilder ReportingProgress(IProgress<string> progress);
}

/// <summary>
/// Builds <c>git push --porcelain</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
internal sealed class GitPushBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)
	: GitCommandBuilder<GitPushResult>(runner, repositoryPath), IGitPushBuilder
{
	private GitRemoteName? _remote;
	private GitBranchName? _branch;
	private bool _setUpstream;
	private bool _force;
	private bool _forceWithLease;
	private bool _delete;
	private bool _dryRun;

	/// <inheritdoc />
	public IGitPushBuilder ToRemote(GitRemoteName name)
	{
		_remote = Ensure.NotNull(name);
		return this;
	}

	/// <inheritdoc />
	public IGitPushBuilder WithBranch(GitBranchName name)
	{
		_branch = Ensure.NotNull(name);
		return this;
	}

	/// <inheritdoc />
	public IGitPushBuilder SettingUpstream()
	{
		_setUpstream = true;
		return this;
	}

	/// <inheritdoc />
	public IGitPushBuilder Force()
	{
		_force = true;
		return this;
	}

	/// <inheritdoc />
	public IGitPushBuilder ForceWithLease()
	{
		_forceWithLease = true;
		return this;
	}

	/// <inheritdoc />
	public IGitPushBuilder DeletingRemoteBranch()
	{
		_delete = true;
		return this;
	}

	/// <inheritdoc />
	public IGitPushBuilder DryRun()
	{
		_dryRun = true;
		return this;
	}

	/// <inheritdoc />
	public IGitPushBuilder ReportingProgress(IProgress<string> progress)
	{
		Progress = Ensure.NotNull(progress);
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("push");

		// Unconditional: it is what makes the outcome machine-readable, and it is the only reason
		// a rejected push can be reported as data rather than as prose.
		arguments.Add("--porcelain");

		if (_setUpstream)
		{
			arguments.Add("--set-upstream");
		}

		// The lease wins when both were asked for: it refuses if the remote moved since it was last
		// fetched, so letting the blunter flag override it would quietly remove that protection.
		if (_forceWithLease)
		{
			arguments.Add("--force-with-lease");
		}
		else if (_force)
		{
			arguments.Add("--force");
		}

		if (_delete)
		{
			arguments.Add("--delete");
		}

		if (_dryRun)
		{
			arguments.Add("--dry-run");
		}

		AppendRefspec(arguments);
	}

	private void AppendRefspec(ICollection<string> arguments)
	{
		if (_remote is null)
		{
			// git push <refspec> with no remote reads the first operand as the remote, so a branch
			// on its own would be silently misinterpreted rather than rejected.
			if (_branch is not null)
			{
				throw new InvalidOperationException(
					"A branch was given without a remote. git reads the first operand as the remote name, " +
					"so call ToRemote as well.");
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
	protected override GitPushResult ParseResult(GitProcessResult result) =>
		GitPushParser.Parse(Ensure.NotNull(result).StandardOutput);

	/// <inheritdoc />
	public override async Task<GitPushResult> ExecuteAsync(CancellationToken cancellationToken = default)
	{
		GitProcessResult result = await RunAsync(cancellationToken).ConfigureAwait(false);

		if (result.Success)
		{
			return ParseResult(result);
		}

		// A non-zero exit means one of two very different things. If porcelain records came back,
		// git got far enough to talk about references and refused some of them, and the caller
		// wants that detail. If none came back, git never reached the remote at all.
		GitPushResult parsed = ParseResult(result);

		throw parsed.Updates.Count > 0
			? new GitPushRejectedException(
				$"git refused at least one reference: {result.StandardOutput.Trim()}",
				result.ExitCode,
				result.Arguments,
				result.StandardError,
				parsed)
			: CreateException(result);
	}

	/// <inheritdoc />
	public override async Task<GitResult<GitPushResult>> TryExecuteAsync(CancellationToken cancellationToken = default)
	{
		GitProcessResult result = await RunAsync(cancellationToken).ConfigureAwait(false);

		if (result.Success)
		{
			return GitResult<GitPushResult>.FromValue(ParseResult(result));
		}

		GitPushResult parsed = ParseResult(result);

		// Deliberately a success carrying rejections rather than an error: git ran and reported
		// precisely what happened to every reference, so there is a value to return. Callers
		// distinguish the two with GitPushResult.HasRejections.
		return parsed.Updates.Count > 0
			? GitResult<GitPushResult>.FromValue(parsed)
			: GitResult<GitPushResult>.FromError(new GitCommandError
			{
				ExitCode = result.ExitCode,
				Arguments = result.Arguments,
				StandardError = result.StandardError,
			});
	}

	private Task<GitProcessResult> RunAsync(CancellationToken cancellationToken) =>
		Runner.RunAsync(
			new GitProcessRequest { Arguments = BuildArguments(), Progress = Progress },
			cancellationToken);
}
