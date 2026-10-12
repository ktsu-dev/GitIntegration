// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;

/// <summary>
/// Lists the repository's working trees.
/// </summary>
/// <remarks>
/// Reports the main working tree first, which is the order git emits and the only thing identifying
/// it — see <see cref="GitWorktree.IsMain"/>.
/// </remarks>
public interface IGitWorktreeListBuilder : IGitCommandBuilder<IReadOnlyList<GitWorktree>>
{
}

/// <summary>
/// Builds <c>git worktree list --porcelain -z</c>, or <c>git worktree list --porcelain</c> below git 2.36.
/// </summary>
/// <remarks>
/// No caller options. The porcelain listing already reports every attribute this library models, and
/// git's remaining options on this verb either change the format (<c>-v</c>, which is the human-facing
/// form) or add a field this library reads from the porcelain output anyway (<c>--expire</c>, which
/// only affects which entries are annotated prunable).
/// <para>
/// <c>-z</c> is asked for whenever the installed git has it, because the newline-terminated form
/// cannot carry every path: git prints the <c>worktree</c> path raw, so a path containing a newline
/// is split across two lines and would be reported as a different directory. Under <c>-z</c> every
/// field is NUL-terminated and printed raw, lock and prunable reasons included. Below 2.36 the
/// newline form is the only one there is; the parser decodes its C-quoted reasons, but a newline in
/// a path remains unrepresentable there.
/// </para>
/// </remarks>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
internal sealed class GitWorktreeListBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)
	: GitCommandBuilder<IReadOnlyList<GitWorktree>>(runner, repositoryPath), IGitWorktreeListBuilder
{
	/// <summary>The first git release whose <c>worktree list</c> accepts <c>-z</c>, major part.</summary>
	private const int NulTerminationMajor = 2;

	/// <summary>The first git release whose <c>worktree list</c> accepts <c>-z</c>, minor part.</summary>
	private const int NulTerminationMinor = 36;

	/// <summary>
	/// Gets a value indicating whether the installed git accepts <c>worktree list -z</c>.
	/// </summary>
	/// <remarks>
	/// True until <see cref="ProbeVersionAsync"/> says otherwise, so <c>BuildArguments</c> on its own
	/// shows the vector a current git receives.
	/// </remarks>
	private bool NulTerminationSupportedByVersion { get; set; } = true;

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("worktree");
		arguments.Add("list");
		arguments.Add("--porcelain");

		if (NulTerminationSupportedByVersion)
		{
			arguments.Add("-z");
		}
	}

	/// <inheritdoc />
	protected override IReadOnlyList<GitWorktree> ParseResult(GitProcessResult result) =>
		GitWorktreeParser.Parse(Ensure.NotNull(result).StandardOutput, NulTerminationSupportedByVersion);

	/// <inheritdoc />
	public override async Task<IReadOnlyList<GitWorktree>> ExecuteAsync(CancellationToken cancellationToken = default)
	{
		await ProbeVersionAsync(cancellationToken).ConfigureAwait(false);

		return await base.ExecuteAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public override async Task<GitResult<IReadOnlyList<GitWorktree>>> TryExecuteAsync(CancellationToken cancellationToken = default)
	{
		await ProbeVersionAsync(cancellationToken).ConfigureAwait(false);

		return await base.TryExecuteAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Asks the installed git what version it is, so the vector can be built to suit.
	/// </summary>
	/// <remarks>
	/// Mirrors <c>GitFetchBuilder</c>'s probe: it goes through the result-based
	/// <see cref="IGitCommandBuilder{TResult}.TryExecuteAsync"/>, and a failed probe falls back to the
	/// newline form, which every git understands. A genuinely broken git still fails loudly moments
	/// later, at the listing itself.
	/// </remarks>
	/// <param name="cancellationToken">Cancels the probe.</param>
	/// <returns>A task that completes once the version is known.</returns>
	private async Task ProbeVersionAsync(CancellationToken cancellationToken)
	{
		GitResult<GitVersion> probe = await new GitVersionBuilder(Runner)
			.TryExecuteAsync(cancellationToken).ConfigureAwait(false);

		NulTerminationSupportedByVersion = probe.Success && probe.Value!.AtLeast(NulTerminationMajor, NulTerminationMinor);
	}
}
