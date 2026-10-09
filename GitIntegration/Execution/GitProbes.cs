// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;

/// <summary>
/// Shared single-invocation probes used by more than one verb.
/// </summary>
internal static class GitProbes
{
	/// <summary>
	/// Decides whether a path is inside a git working tree.
	/// </summary>
	/// <remarks>
	/// Used by <see cref="GitClient.IsRepositoryAsync"/>, whose contract really is "inside a working
	/// tree". <see cref="GitRepository.IsClonedAsync"/> asks the narrower
	/// <see cref="IsWorkTreeRootAsync"/> instead, and <see cref="GitInitBuilder"/> answers "is there a
	/// repository at exactly this path" via <c>--git-dir</c>; neither question is this one.
	/// </remarks>
	/// <param name="runner">Runs the probe command.</param>
	/// <param name="path">The path to probe.</param>
	/// <param name="cancellationToken">Cancels the invocation.</param>
	/// <returns><see langword="true"/> when <paramref name="path"/> is inside a working tree.</returns>
	internal static async Task<bool> IsWorkTreeAsync(
		IGitProcessRunner runner,
		AbsoluteDirectoryPath path,
		CancellationToken cancellationToken)
	{
		// TryExecuteAsync, because failure is a legitimate answer: the path may hold no repository,
		// or may not exist at all, and both exit 128 and both mean "no".
		GitResult<string> result = await new GitTextBuilder(runner, path, "rev-parse", "--is-inside-work-tree")
			.TryExecuteAsync(cancellationToken).ConfigureAwait(false);

		return result.Success && string.Equals(result.Value, "true", StringComparison.Ordinal);
	}

	/// <summary>
	/// Decides whether a path is the root of a git working tree.
	/// </summary>
	/// <remarks>
	/// <c>--is-inside-work-tree</c> alone walks up the directory tree, so it answers true for any
	/// directory beneath another repository's working tree, an empty one included. <c>--show-cdup</c>
	/// gives the working-tree root relative to the path: empty at the root and <c>../</c> and so on
	/// below it. It reads the working tree rather than the git directory, so a root whose <c>.git</c>
	/// is a file pointing elsewhere (a submodule, a linked worktree, a <c>--separate-git-dir</c>
	/// checkout) still counts, and a bare repository, which has no working tree, does not.
	/// </remarks>
	/// <param name="runner">Runs the probe command.</param>
	/// <param name="path">The path to probe.</param>
	/// <param name="cancellationToken">Cancels the invocation.</param>
	/// <returns><see langword="true"/> when <paramref name="path"/> is the root of a working tree.</returns>
	internal static async Task<bool> IsWorkTreeRootAsync(
		IGitProcessRunner runner,
		AbsoluteDirectoryPath path,
		CancellationToken cancellationToken)
	{
		// TryExecuteAsync for the same reason as IsWorkTreeAsync: no repository, or no directory,
		// exits 128 and means "no".
		GitResult<string> result = await new GitTextBuilder(runner, path, "rev-parse", "--is-inside-work-tree", "--show-cdup")
			.TryExecuteAsync(cancellationToken).ConfigureAwait(false);

		return result.Success && result.Value is not null && IsWorkTreeRoot(result.Value);
	}

	/// <summary>
	/// Reads the answer to <see cref="IsWorkTreeRootAsync"/>'s probe.
	/// </summary>
	/// <remarks>
	/// The output is trimmed, so at the root the empty <c>--show-cdup</c> line is gone and only
	/// <c>true</c> is left; below it a second line holds the way up.
	/// </remarks>
	/// <param name="output">The trimmed output of the probe.</param>
	/// <returns><see langword="true"/> when the probed path is a working-tree root.</returns>
	internal static bool IsWorkTreeRoot(string output)
	{
		string[] lines = Ensure.NotNull(output).Split('\n');

		return string.Equals(lines[0].TrimEnd('\r'), "true", StringComparison.Ordinal)
			&& (lines.Length == 1 || string.IsNullOrWhiteSpace(lines[1]));
	}
}
