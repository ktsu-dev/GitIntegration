// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;

/// <summary>
/// The shipped <see cref="IGitClient"/>, running everything through an
/// <see cref="IGitProcessRunner"/>.
/// </summary>
/// <remarks>
/// Repository discovery is delegated to <c>git rev-parse --show-toplevel</c>, which performs the
/// upward walk itself. That is why this type takes no filesystem abstraction: there is nothing to
/// walk that git does not already walk, and asking git keeps the answer consistent with what every
/// subsequent verb will see. Phase 4's <c>Init</c> and <c>Clone</c> do need one, because they act on
/// a destination where no repository exists yet to be asked.
/// </remarks>
/// <param name="runner">Runs every command this client issues.</param>
public sealed class GitClient(IGitProcessRunner runner) : IGitClient
{
	private readonly IGitProcessRunner _runner = Ensure.NotNull(runner);

	/// <inheritdoc />
	public Task<GitVersion> GetVersionAsync(CancellationToken cancellationToken = default) =>
		new GitVersionBuilder(_runner).ExecuteAsync(cancellationToken);

	/// <inheritdoc />
	public Task<bool> IsRepositoryAsync(AbsoluteDirectoryPath path, CancellationToken cancellationToken = default)
	{
		// Validated here rather than in the async body so that the exception is thrown by the call
		// itself, not deferred until the returned task is awaited.
		Ensure.NotNull(path);

		return IsRepositoryCoreAsync(path, cancellationToken);
	}

	/// <inheritdoc />
	public Task<GitRepository> OpenAsync(AbsoluteDirectoryPath path, CancellationToken cancellationToken = default)
	{
		Ensure.NotNull(path);

		return OpenCoreAsync(path, cancellationToken);
	}

	/// <inheritdoc />
	public Task<GitRepository?> DiscoverAsync(AbsoluteDirectoryPath startingPath, CancellationToken cancellationToken = default)
	{
		Ensure.NotNull(startingPath);

		return DiscoverCoreAsync(startingPath, cancellationToken);
	}

	private async Task<bool> IsRepositoryCoreAsync(AbsoluteDirectoryPath path, CancellationToken cancellationToken)
	{
		GitResult<string> result = await new GitTextBuilder(_runner, path, "rev-parse", "--is-inside-work-tree")
			.TryExecuteAsync(cancellationToken).ConfigureAwait(false);

		// TryExecuteAsync rather than ExecuteAsync: "not a git repository" and "cannot change to
		// that directory" both exit 128, and both mean no rather than failure.
		return result.Success && string.Equals(result.Value, "true", StringComparison.Ordinal);
	}

	private async Task<GitRepository> OpenCoreAsync(AbsoluteDirectoryPath path, CancellationToken cancellationToken)
	{
		GitRepository? repository = await DiscoverCoreAsync(path, cancellationToken).ConfigureAwait(false);

		return repository ?? throw new GitRepositoryNotFoundException(
			$"'{path.WeakString}' is not inside a git working tree.");
	}

	private async Task<GitRepository?> DiscoverCoreAsync(AbsoluteDirectoryPath startingPath, CancellationToken cancellationToken)
	{
		GitResult<string> topLevel = await new GitTextBuilder(_runner, startingPath, "rev-parse", "--show-toplevel")
			.TryExecuteAsync(cancellationToken).ConfigureAwait(false);

		if (!topLevel.Success || string.IsNullOrEmpty(topLevel.Value))
		{
			return null;
		}

		// git prints the working tree root with forward slashes on every platform;
		// AbsoluteDirectoryPath canonicalises to the host separator.
		if (!AbsoluteDirectoryPath.TryCreate(topLevel.Value, out AbsoluteDirectoryPath? localPath) ||
			localPath is null)
		{
			throw new GitParseException(
				$"git reported a working tree root that is not an absolute directory path: '{topLevel.Value}'.");
		}

		return new GitRepository
		{
			LocalPath = localPath,
			RemotePath = await ReadOriginUrlAsync(localPath, cancellationToken).ConfigureAwait(false),
			ProcessRunner = _runner,
		};
	}

	private async Task<GitRepositoryRemotePath?> ReadOriginUrlAsync(
		AbsoluteDirectoryPath localPath,
		CancellationToken cancellationToken)
	{
		GitResult<string> originUrl = await new GitTextBuilder(_runner, localPath, "remote", "get-url", "origin")
			.TryExecuteAsync(cancellationToken).ConfigureAwait(false);

		// A repository with no origin exits 2 here. That is an ordinary state, not a failure, so
		// the metadata stays null and discovery still succeeds.
		return originUrl.Success &&
			!string.IsNullOrEmpty(originUrl.Value) &&
			GitRepositoryRemotePath.TryCreate(originUrl.Value, out GitRepositoryRemotePath? remotePath)
				? remotePath
				: null;
	}
}
