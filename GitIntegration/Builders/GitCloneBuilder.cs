// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Essentials;
using ktsu.Semantics.Paths;

/// <summary>
/// Copies a remote repository into a local working copy.
/// </summary>
public interface IGitCloneBuilder : IGitCommandBuilder<GitRepository>
{
	/// <summary>Checks this branch out instead of the remote's default.</summary>
	/// <param name="name">The branch to check out.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	public IGitCloneBuilder WithBranch(GitBranchName name);

	/// <summary>
	/// Fetches only this many commits of history, producing a shallow clone.
	/// </summary>
	/// <param name="depth">How many commits of history to fetch. Must be positive.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="depth"/> is not positive.</exception>
	public IGitCloneBuilder WithDepth(int depth);

	/// <summary>Creates a repository with no working tree.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitCloneBuilder Bare();

	/// <summary>
	/// Reports git's progress output as it arrives, rather than only when the clone finishes.
	/// </summary>
	/// <remarks>
	/// Clone writes its entire progress stream to standard error. The sink may be invoked
	/// concurrently by the standard-output and standard-error readers and must be thread-safe.
	/// </remarks>
	/// <param name="progress">The sink to report to.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="progress"/> is <see langword="null"/>.</exception>
	public IGitCloneBuilder ReportingProgress(IProgress<string> progress);
}

/// <summary>
/// Builds <c>git clone</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="fileSystem">Checks the destination before the clone starts.</param>
/// <param name="source">The repository to clone.</param>
/// <param name="destination">Where the working copy should go.</param>
internal sealed class GitCloneBuilder(
	IGitProcessRunner runner,
	IFileSystemProvider fileSystem,
	GitRepositoryRemotePath source,
	AbsoluteDirectoryPath destination)
	: GitCommandBuilder<GitRepository>(runner, repositoryPath: null), IGitCloneBuilder
{
	private readonly IFileSystemProvider _fileSystem = Ensure.NotNull(fileSystem);
	private readonly GitRepositoryRemotePath _source = Ensure.NotNull(source);
	private readonly AbsoluteDirectoryPath _destination = Ensure.NotNull(destination);
	private GitBranchName? _branch;
	private int? _depth;
	private bool _bare;

	/// <inheritdoc />
	public IGitCloneBuilder WithBranch(GitBranchName name)
	{
		_branch = Ensure.NotNull(name);
		return this;
	}

	/// <inheritdoc />
	public IGitCloneBuilder WithDepth(int depth)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);
		_depth = depth;
		return this;
	}

	/// <inheritdoc />
	public IGitCloneBuilder Bare()
	{
		_bare = true;
		return this;
	}

	/// <inheritdoc />
	public IGitCloneBuilder ReportingProgress(IProgress<string> progress)
	{
		Progress = Ensure.NotNull(progress);
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("clone");

		if (_branch is not null)
		{
			// --branch takes its value as a separate argument, unlike --depth.
			arguments.Add("--branch");
			arguments.Add(_branch.WeakString);
		}

		if (_depth is int depth)
		{
			arguments.Add("--depth=" + depth.ToString(CultureInfo.InvariantCulture));
		}

		if (_bare)
		{
			arguments.Add("--bare");
		}

		// Both operands are caller-supplied. The source especially: an unvalidated remote path of
		// --upload-pack=... reaching git clone is arbitrary code execution, which is what
		// NotAnOptionAttribute and this marker together close off.
		AppendOperands(arguments, _source.WeakString, _destination.WeakString);
	}

	/// <inheritdoc />
	protected override GitRepository ParseResult(GitProcessResult result)
	{
		Ensure.NotNull(result);

		// Nothing to parse: git clone writes only progress prose, all of it to standard error.
		return new GitRepository
		{
			LocalPath = _destination,
			RemotePath = _source,
			ProcessRunner = Runner,
		};
	}

	/// <inheritdoc />
	public override Task<GitRepository> ExecuteAsync(CancellationToken cancellationToken = default)
	{
		ThrowIfDestinationOccupied();
		return base.ExecuteAsync(cancellationToken);
	}

	/// <inheritdoc />
	public override Task<GitResult<GitRepository>> TryExecuteAsync(CancellationToken cancellationToken = default)
	{
		if (IsDestinationOccupied())
		{
			return Task.FromResult(GitResult<GitRepository>.FromError(new GitCommandError
			{
				ExitCode = -1,
				Arguments = [],
				StandardError = DestinationOccupiedMessage,
			}));
		}

		return base.TryExecuteAsync(cancellationToken);
	}

	private string DestinationOccupiedMessage =>
		$"The clone destination '{_destination.WeakString}' already exists and is not empty.";

	/// <summary>
	/// Decides whether the destination already holds anything.
	/// </summary>
	/// <remarks>
	/// Advisory only. Git enforces the same rule itself and exits 128 when the destination is
	/// non-empty, leaving no directory behind; this check exists so a clone that cannot possibly
	/// succeed fails before paying its network cost. It is inherently racy — a directory can appear
	/// between this check and the clone — so git's refusal, not this, is the authority.
	/// </remarks>
	private bool IsDestinationOccupied() =>
		_fileSystem.Directory.Exists(_destination.WeakString) &&
		_fileSystem.Directory.GetFileSystemEntries(_destination.WeakString).Length > 0;

	private void ThrowIfDestinationOccupied()
	{
		if (IsDestinationOccupied())
		{
			// Exit code -1 and an empty vector, because no git command ran. GitCommandException
			// documents -1 as its "no data" default for exactly this reason.
			throw new GitCommandException(DestinationOccupiedMessage, -1, [], string.Empty);
		}
	}
}
