// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;

/// <summary>
/// The entry point to the local layer: finds and opens repositories, and reports on the git binary.
/// </summary>
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
	/// <exception cref="GitParseException">
	/// git reported a working-tree root that is not an absolute directory path.
	/// </exception>
	public Task<GitRepository> OpenAsync(AbsoluteDirectoryPath path, CancellationToken cancellationToken = default);

	/// <summary>
	/// Opens the repository containing a path, reporting the ordinary case of absence as a result
	/// rather than an exception.
	/// </summary>
	/// <param name="startingPath">A path inside, or below, the repository.</param>
	/// <param name="cancellationToken">Cancels the invocation.</param>
	/// <returns>The opened repository, or <see langword="null"/> when there is none.</returns>
	/// <exception cref="GitParseException">
	/// git reported a working-tree root that is not an absolute directory path.
	/// </exception>
	public Task<GitRepository?> DiscoverAsync(AbsoluteDirectoryPath startingPath, CancellationToken cancellationToken = default);

	/// <summary>
	/// Creates a repository at a path.
	/// </summary>
	/// <remarks>
	/// Safe to run against a path that already holds a repository: git reinitialises it, and the
	/// result reports <see cref="GitInitResult.AlreadyExisted"/> so a caller can tell.
	/// </remarks>
	/// <param name="path">Where the repository should be.</param>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
	public IGitInitBuilder Init(AbsoluteDirectoryPath path);

	/// <summary>
	/// Clones a repository into a local working copy.
	/// </summary>
	/// <param name="source">The repository to clone.</param>
	/// <param name="destination">Where the working copy should go.</param>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="source"/> or <paramref name="destination"/> is <see langword="null"/>.
	/// </exception>
	public IGitCloneBuilder Clone(GitRepositoryRemotePath source, AbsoluteDirectoryPath destination);

	/// <summary>
	/// Clones the repository a hosting provider described.
	/// </summary>
	/// <remarks>
	/// This is the one seam between the library's two layers: a provider produces a
	/// <see cref="GitRepository"/> carrying remote metadata and an intended local path, and this
	/// turns it into a working copy.
	/// </remarks>
	/// <param name="repository">The repository to clone, carrying a non-null
	/// <see cref="GitRepository.RemotePath"/>.</param>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="repository"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="repository"/> has no <see cref="GitRepository.RemotePath"/>.
	/// </exception>
	public IGitCloneBuilder Clone(GitRepository repository);
}
