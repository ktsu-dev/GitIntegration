// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;

/// <summary>
/// Creates a repository.
/// </summary>
public interface IGitInitBuilder : IGitCommandBuilder<GitInitResult>
{
	/// <summary>Creates a repository with no working tree.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitInitBuilder Bare();

	/// <summary>
	/// Names the branch the repository starts on instead of taking git's configured default.
	/// </summary>
	/// <remarks>
	/// Ignored by git when the repository already exists, which is why
	/// <see cref="GitInitResult.AlreadyExisted"/> is worth checking after asking for one.
	/// </remarks>
	/// <param name="name">The initial branch name.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	public IGitInitBuilder WithInitialBranch(GitBranchName name);
}

/// <summary>
/// Builds <c>git init</c>, preceded by a probe for an existing repository.
/// </summary>
/// <remarks>
/// The target is passed as an operand rather than through <c>-C</c>, because <c>-C</c> requires the
/// directory to exist and <c>init</c> is frequently the thing that creates it.
/// </remarks>
/// <param name="runner">Runs the assembled commands.</param>
/// <param name="targetPath">Where the repository should be.</param>
internal sealed class GitInitBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath targetPath)
	: GitCommandBuilder<GitInitResult>(runner, repositoryPath: null), IGitInitBuilder
{
	private readonly AbsoluteDirectoryPath _targetPath = Ensure.NotNull(targetPath);
	private GitBranchName? _initialBranch;
	private bool _bare;
	private bool _alreadyExisted;

	/// <inheritdoc />
	public IGitInitBuilder Bare()
	{
		_bare = true;
		return this;
	}

	/// <inheritdoc />
	public IGitInitBuilder WithInitialBranch(GitBranchName name)
	{
		_initialBranch = Ensure.NotNull(name);
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("init");

		if (_bare)
		{
			arguments.Add("--bare");
		}

		if (_initialBranch is not null)
		{
			arguments.Add("--initial-branch=" + _initialBranch.WeakString);
		}

		AppendOperands(arguments, _targetPath.WeakString);
	}

	/// <summary>
	/// Builds the result, taking the already-existed answer from the probe run just before.
	/// </summary>
	/// <remarks>
	/// The probe's answer arrives through a field rather than through <paramref name="result"/>,
	/// because it is not in <c>init</c>'s output — it cannot be, which is the whole reason the probe
	/// exists. Holding it in a field is safe: a builder is single-use and not thread-safe by
	/// contract, and both entry points set it immediately before delegating to the base.
	/// </remarks>
	/// <param name="result">The invocation outcome, which carries nothing this result needs.</param>
	/// <returns>The initialized repository and whether it was already there.</returns>
	protected override GitInitResult ParseResult(GitProcessResult result)
	{
		Ensure.NotNull(result);

		return new GitInitResult
		{
			Repository = new GitRepository
			{
				LocalPath = _targetPath,
				ProcessRunner = Runner,
			},
			AlreadyExisted = _alreadyExisted,
		};
	}

	/// <inheritdoc />
	public override async Task<GitInitResult> ExecuteAsync(CancellationToken cancellationToken = default)
	{
		// Probe first, then let the base run init and call ParseResult exactly as it does for every
		// other verb. Reimplementing the run-and-classify flow here would duplicate the base's
		// failure handling for no gain.
		_alreadyExisted = await ProbeAsync(cancellationToken).ConfigureAwait(false);

		return await base.ExecuteAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public override async Task<GitResult<GitInitResult>> TryExecuteAsync(CancellationToken cancellationToken = default)
	{
		_alreadyExisted = await ProbeAsync(cancellationToken).ConfigureAwait(false);

		return await base.TryExecuteAsync(cancellationToken).ConfigureAwait(false);
	}

	private async Task<bool> ProbeAsync(CancellationToken cancellationToken)
	{
		// Asks "is there a repository at exactly this path", deliberately not via
		// GitProbes.IsWorkTreeAsync's --is-inside-work-tree: that answers a different question,
		// whether the path is inside *some* working tree. That wrongly reports AlreadyExisted =
		// true for a plain subdirectory of an existing repository (git init there creates a
		// nested repository), and wrongly reports AlreadyExisted = false for an existing bare
		// repository (git init there only prints a re-init warning).
		//
		// TryExecuteAsync, because failure is the expected answer: the directory may hold no
		// repository, or may not exist at all, and both exit 128 and both mean "not yet".
		GitResult<string> probe = await new GitTextBuilder(
			Runner, _targetPath, "rev-parse", "--is-bare-repository", "--git-dir", "--show-cdup")
			.TryExecuteAsync(cancellationToken).ConfigureAwait(false);

		return probe.Success && probe.Value is not null && IsRepositoryRoot(probe.Value);
	}

	/// <summary>
	/// Reads the probe's answer: whether the repository git found is rooted at the target itself.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>--git-dir</c> alone cannot tell. It prints <c>.git</c> for an ordinary repository at the
	/// target and <c>.</c> for a bare one, but an absolute path both for a repository rooted above
	/// the target and for one at the target whose <c>.git</c> is a file pointing elsewhere — a
	/// submodule, a linked worktree, or a <c>--separate-git-dir</c> repository. git re-initializes
	/// the latter, so it is the working-tree root, not the git directory, that decides.
	/// </para>
	/// <para>
	/// <c>--show-cdup</c> gives that root relative to the target: empty at the root, <c>../</c>
	/// and so on below it. It prints nothing at all outside a working tree, and the output is
	/// trimmed, so a missing line and an empty one read the same; the bare check comes first
	/// because a subdirectory of a bare repository is the one place that difference would matter.
	/// </para>
	/// </remarks>
	/// <param name="output">The trimmed output of the probe.</param>
	/// <returns><see langword="true"/> when a repository already exists at the target.</returns>
	internal static bool IsRepositoryRoot(string output)
	{
		string[] lines = Ensure.NotNull(output).Split('\n');

		if (lines.Length < 2)
		{
			return false;
		}

		// "." is the target itself being a git directory: a bare repository, or a .git directory.
		if (string.Equals(lines[1].TrimEnd('\r'), ".", StringComparison.Ordinal))
		{
			return true;
		}

		if (string.Equals(lines[0].TrimEnd('\r'), "true", StringComparison.Ordinal))
		{
			return false;
		}

		return lines.Length < 3 || lines[2].TrimEnd('\r').Length == 0;
	}
}
