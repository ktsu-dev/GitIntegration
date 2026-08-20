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
	/// Shared by <see cref="GitRepository.IsClonedAsync"/> and <see cref="GitClient.IsRepositoryAsync"/>,
	/// which both asked this exact question independently before this extraction.
	/// <see cref="GitInitBuilder"/> does not use this helper: after answering "is there a repository
	/// at exactly this path" via <c>--git-dir</c> instead, it asks a genuinely different question, not
	/// this one.
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
}
