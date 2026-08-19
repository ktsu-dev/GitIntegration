// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;

/// <summary>
/// The entry point to the local layer: finds and opens repositories, and reports on the git binary.
/// </summary>
/// <remarks>
/// Phase 4 adds <c>Init</c> and <c>Clone</c> to this interface. It carries only the read-only
/// operations for now.
/// </remarks>
public interface IGitClient
{
	/// <summary>Reports the version of the git binary being invoked.</summary>
	/// <param name="cancellationToken">Cancels the invocation.</param>
	/// <returns>The parsed version.</returns>
	public Task<GitVersion> GetVersionAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Decides whether a path is inside a git working tree.
	/// </summary>
	/// <remarks>
	/// Never throws for a path that is not a repository, or for a path that does not exist. Both
	/// answer the same question the same way.
	/// </remarks>
	/// <param name="path">The path to test.</param>
	/// <param name="cancellationToken">Cancels the invocation.</param>
	/// <returns><see langword="true"/> when the path is inside a working tree.</returns>
	public Task<bool> IsRepositoryAsync(AbsoluteDirectoryPath path, CancellationToken cancellationToken = default);

	/// <summary>
	/// Opens the repository containing a path.
	/// </summary>
	/// <remarks>
	/// The returned repository's <see cref="GitRepository.LocalPath"/> is the working tree root, not
	/// necessarily <paramref name="path"/>, and its
	/// <see cref="GitRepository.RemotePath"/> is back-filled from <c>origin</c> when one is
	/// configured.
	/// </remarks>
	/// <param name="path">A path inside the repository.</param>
	/// <param name="cancellationToken">Cancels the invocation.</param>
	/// <returns>The opened repository.</returns>
	/// <exception cref="GitRepositoryNotFoundException">The path is not inside a working tree.</exception>
	public Task<GitRepository> OpenAsync(AbsoluteDirectoryPath path, CancellationToken cancellationToken = default);

	/// <summary>
	/// Opens the repository containing a path, reporting absence as a result rather than an
	/// exception.
	/// </summary>
	/// <param name="startingPath">A path inside, or below, the repository.</param>
	/// <param name="cancellationToken">Cancels the invocation.</param>
	/// <returns>The opened repository, or <see langword="null"/> when there is none.</returns>
	public Task<GitRepository?> DiscoverAsync(AbsoluteDirectoryPath startingPath, CancellationToken cancellationToken = default);
}
