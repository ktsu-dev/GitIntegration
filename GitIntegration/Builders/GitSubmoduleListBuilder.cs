// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;

/// <summary>
/// Lists the repository's submodules, with what each records and what each has checked out.
/// </summary>
/// <remarks>
/// <para>
/// Lists the superproject's own submodules and does not recurse. That is a deliberate limit rather
/// than an omission. The authoritative, NUL-terminated source for a submodule's path is
/// <c>ls-files --stage</c>, and it enumerates only the superproject's own index — a nested
/// submodule's path would have to come from <c>submodule status --recursive</c> instead, whose
/// output has no <c>-z</c> form and cannot be split unambiguously without already knowing the path.
/// Recursing would therefore mean giving up, for nested entries only, the exact property this verb
/// is built on.
/// </para>
/// <para>
/// A caller who needs nested submodules recurses through composition instead: open each submodule as
/// a <see cref="GitRepository"/> in its own right — its path is
/// <see cref="GitRepository.LocalPath"/> combined with <see cref="GitSubmodule.Path"/> — and call
/// this verb on that. Every level is then exact, and it reuses this code rather than a second,
/// weaker implementation of it.
/// </para>
/// </remarks>
public interface IGitSubmoduleListBuilder : IGitCommandBuilder<IReadOnlyList<GitSubmodule>>
{
}

/// <summary>
/// Builds <c>git ls-files --stage -z</c> and reads <c>git submodule status</c> alongside it.
/// </summary>
/// <remarks>
/// Two invocations, for the same reason <c>commit</c> runs git twice: no single command answers the
/// whole question. <c>ls-files</c> reports what the superproject records, NUL-terminated so a path
/// may contain anything; <c>submodule status</c> reports what is actually checked out, which
/// <c>ls-files</c> cannot know. See <see cref="GitSubmoduleParser"/> for how the second command's
/// ambiguous output is made safe to read by already knowing every path from the first.
/// </remarks>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
internal sealed class GitSubmoduleListBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)
	: GitCommandBuilder<IReadOnlyList<GitSubmodule>>(runner, repositoryPath), IGitSubmoduleListBuilder
{
	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("ls-files");
		arguments.Add("--stage");
		arguments.Add("-z");
	}

	/// <inheritdoc />
	/// <remarks>
	/// Reads the gitlinks only. The base class calls this with the first invocation's output, and
	/// the state each entry needs comes from the second — see <see cref="ExecuteAsync"/>.
	/// </remarks>
	protected override IReadOnlyList<GitSubmodule> ParseResult(GitProcessResult result) =>
		GitSubmoduleParser.ParseGitlinks(Ensure.NotNull(result).StandardOutput);

	/// <inheritdoc />
	public override async Task<IReadOnlyList<GitSubmodule>> ExecuteAsync(CancellationToken cancellationToken = default)
	{
		IReadOnlyList<GitSubmodule> gitlinks =
			await base.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		return await ApplyStatusAsync(gitlinks, cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public override async Task<GitResult<IReadOnlyList<GitSubmodule>>> TryExecuteAsync(CancellationToken cancellationToken = default)
	{
		GitResult<IReadOnlyList<GitSubmodule>> gitlinks =
			await base.TryExecuteAsync(cancellationToken).ConfigureAwait(false);

		if (!gitlinks.Success || gitlinks.Value is null)
		{
			return gitlinks;
		}

		return GitResult<IReadOnlyList<GitSubmodule>>.FromValue(
			await ApplyStatusAsync(gitlinks.Value, cancellationToken).ConfigureAwait(false));
	}

	/// <summary>
	/// Runs <c>submodule status</c> and folds its answer into the gitlinks already read.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Skipped entirely when there are no gitlinks, so a repository with no submodules — the common
	/// case — costs one invocation rather than two.
	/// </para>
	/// <para>
	/// The status probe goes through the <em>result-based</em> <c>TryExecuteAsync</c> rather than the
	/// throwing entry point, and does so the same way regardless of which of this builder's own two
	/// entry points is running, for the reason <c>GitFetchBuilder</c>'s version probe gives: the
	/// gitlinks are already known and are the authoritative half of the answer, so failing the whole
	/// listing because the wrapper could not run would discard a correct result over a missing
	/// embellishment. Every entry keeps <see cref="GitSubmoduleState.Unknown"/> in that case, which
	/// already means exactly "git did not say".
	/// </para>
	/// </remarks>
	/// <param name="gitlinks">The gitlinks read from the first invocation.</param>
	/// <param name="cancellationToken">Cancels the invocation.</param>
	/// <returns>The submodules, with state resolved where git reported it.</returns>
	private async Task<IReadOnlyList<GitSubmodule>> ApplyStatusAsync(
		IReadOnlyList<GitSubmodule> gitlinks,
		CancellationToken cancellationToken)
	{
		if (gitlinks.Count == 0)
		{
			return gitlinks;
		}

		GitResult<string> status = await new GitSubmoduleStatusBuilder(Runner, RepositoryPath!)
			.TryExecuteAsync(cancellationToken).ConfigureAwait(false);

		return status.Success && status.Value is not null
			? GitSubmoduleParser.ApplyStatus(gitlinks, status.Value)
			: gitlinks;
	}

	/// <summary>
	/// Runs <c>git submodule status</c> and returns its standard output <em>untrimmed</em>.
	/// </summary>
	/// <remarks>
	/// Its own type rather than <see cref="GitTextBuilder"/>, whose contract is trimmed output. That
	/// contract is right for the single-value probes it serves and wrong here: the marker for a
	/// synchronised submodule is a leading <b>space</b>, so trimming silently deletes the very field
	/// this invocation exists to read, and every in-sync submodule would come back
	/// <see cref="GitSubmoduleState.Unknown"/>. Widening <see cref="GitTextBuilder"/> with a flag
	/// would put that trap one careless default away from every other probe instead.
	/// </remarks>
	/// <param name="runner">Runs the assembled command.</param>
	/// <param name="repositoryPath">The repository to scope the command to.</param>
	private sealed class GitSubmoduleStatusBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)
		: GitCommandBuilder<string>(runner, repositoryPath)
	{
		/// <inheritdoc />
		protected override void AppendVerbArguments(ICollection<string> arguments)
		{
			Ensure.NotNull(arguments);

			arguments.Add("submodule");
			arguments.Add("status");
		}

		/// <inheritdoc />
		protected override string ParseResult(GitProcessResult result) =>
			Ensure.NotNull(result).StandardOutput;
	}
}
