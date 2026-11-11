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
	/// <see cref="IsRepositoryRootAsync"/> instead, and <see cref="GitInitBuilder"/> answers "is there a
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
	/// Decides whether a path is the root of a repository: of a working tree, or of a bare repository.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>--is-inside-work-tree</c> alone walks up the directory tree, so it answers true for any
	/// directory beneath another repository's working tree, an empty one included. <c>--show-cdup</c>
	/// gives the working-tree root relative to the path: empty at the root and <c>../</c> and so on
	/// below it. It reads the working tree rather than the git directory, so a root whose <c>.git</c>
	/// is a file pointing elsewhere (a submodule, a linked worktree, a <c>--separate-git-dir</c>
	/// checkout) still counts.
	/// </para>
	/// <para>
	/// A bare repository has no working tree, so <c>--is-inside-work-tree</c> prints <c>false</c>
	/// there and exits 0 (#263). <c>--is-bare-repository</c> and <c>--git-dir</c> cover it:
	/// <c>--git-dir</c> prints <c>.</c> at the bare repository's own root and an absolute path in any
	/// directory below it. A non-bare repository's <c>.git</c> directory also prints <c>.</c>, but is
	/// not bare, so it is not a clone.
	/// </para>
	/// </remarks>
	/// <param name="runner">Runs the probe command.</param>
	/// <param name="path">The path to probe.</param>
	/// <param name="cancellationToken">Cancels the invocation.</param>
	/// <returns><see langword="true"/> when <paramref name="path"/> is the root of a working tree or of a bare repository.</returns>
	internal static async Task<bool> IsRepositoryRootAsync(
		IGitProcessRunner runner,
		AbsoluteDirectoryPath path,
		CancellationToken cancellationToken)
	{
		// TryExecuteAsync for the same reason as IsWorkTreeAsync: no repository, or no directory,
		// exits 128 and means "no".
		GitResult<string> result = await new GitTextBuilder(
			runner, path, "rev-parse", "--is-bare-repository", "--git-dir", "--is-inside-work-tree", "--show-cdup")
			.TryExecuteAsync(cancellationToken).ConfigureAwait(false);

		return result.Success && result.Value is not null && IsRepositoryRoot(result.Value);
	}

	/// <summary>
	/// Reads the answer to <see cref="IsRepositoryRootAsync"/>'s probe.
	/// </summary>
	/// <remarks>
	/// The lines are <c>--is-bare-repository</c>, <c>--git-dir</c>, <c>--is-inside-work-tree</c> and
	/// <c>--show-cdup</c>, in that order. The output is trimmed, so at a working-tree root the empty
	/// <c>--show-cdup</c> line is gone and three lines are left; below the root a fourth holds the way
	/// up. A bare repository prints no <c>--show-cdup</c> line at all.
	/// </remarks>
	/// <param name="output">The trimmed output of the probe.</param>
	/// <returns><see langword="true"/> when the probed path is a working-tree root or a bare repository's root.</returns>
	internal static bool IsRepositoryRoot(string output)
	{
		string[] lines = Ensure.NotNull(output).Split('\n');

		if (lines.Length < 3)
		{
			return false;
		}

		if (string.Equals(lines[0].TrimEnd('\r'), "true", StringComparison.Ordinal))
		{
			return string.Equals(lines[1].TrimEnd('\r'), ".", StringComparison.Ordinal);
		}

		return string.Equals(lines[2].TrimEnd('\r'), "true", StringComparison.Ordinal)
			&& (lines.Length == 3 || string.IsNullOrWhiteSpace(lines[3]));
	}
}
