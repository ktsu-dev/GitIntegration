// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;

/// <summary>
/// Removes a whole file's staged changes, leaving the working tree alone.
/// </summary>
/// <remarks>
/// Unstaging one hunk of a file's patch is <c>Apply(text).ToIndex().Reversed()</c>. This builder is
/// the file-level verb, and the only option for a binary file, which has no hunks to apply in
/// reverse.
/// </remarks>
public interface IGitRestoreBuilder : IGitCommandBuilder<GitCompleted>
{
}

/// <summary>
/// Builds <c>git restore --staged</c>, with a fallback to <c>git reset HEAD</c> on a git older than
/// the one that introduced <c>restore</c>.
/// </summary>
/// <remarks>
/// <c>git restore</c> arrived in git 2.23. Below that, unstaging a path goes through <c>git reset
/// HEAD -- &lt;path&gt;</c> instead, which every supported git understands. The choice follows the
/// same shape as <see cref="GitFetchBuilder"/>: a version probe runs in <see cref="ExecuteAsync"/>
/// and <see cref="TryExecuteAsync"/> before the vector is built, because <c>BuildArguments</c> is
/// documented as a pure computation with no I/O and so cannot probe for itself.
/// </remarks>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
/// <param name="path">The path, relative to the repository root, to unstage.</param>
internal sealed class GitRestoreBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath, RelativeFilePath path)
	: GitCommandBuilder<GitCompleted>(runner, repositoryPath), IGitRestoreBuilder
{
	/// <summary>The first git release whose <c>restore</c> command exists.</summary>
	private const int RestoreMajor = 2;
	private const int RestoreMinor = 23;

	private readonly RelativeFilePath _path = Ensure.NotNull(path);

	/// <summary>
	/// Gets or sets a value indicating whether the installed git is new enough for <c>restore</c>.
	/// </summary>
	/// <remarks>
	/// Defaults true so <c>BuildArguments</c> emits the modern form until an execution path tells it
	/// otherwise, matching <see cref="GitFetchBuilder"/>'s own default.
	/// </remarks>
	private bool RestoreSupportedByVersion { get; set; } = true;

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		if (RestoreSupportedByVersion)
		{
			arguments.Add("restore");
			arguments.Add("--staged");
		}
		else
		{
			arguments.Add("reset");
			arguments.Add("HEAD");
		}

		AppendOperands(arguments, _path.WeakString);
	}

	/// <inheritdoc />
	protected override GitCompleted ParseResult(GitProcessResult result) =>
		new() { Arguments = Ensure.NotNull(result).Arguments };

	/// <inheritdoc />
	public override async Task<GitCompleted> ExecuteAsync(CancellationToken cancellationToken = default)
	{
		await ProbeVersionAsync(cancellationToken).ConfigureAwait(false);

		return await base.ExecuteAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public override async Task<GitResult<GitCompleted>> TryExecuteAsync(CancellationToken cancellationToken = default)
	{
		await ProbeVersionAsync(cancellationToken).ConfigureAwait(false);

		return await base.TryExecuteAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Asks the installed git what version it is, so the vector can be built to suit.
	/// </summary>
	/// <remarks>
	/// Goes through <see cref="IGitCommandBuilder{TResult}.TryExecuteAsync"/> rather than
	/// <see cref="IGitCommandBuilder{TResult}.ExecuteAsync"/>, and the same way regardless of which
	/// of this builder's own two entry points is running: a failed probe means the version genuinely
	/// could not be established, and falling back to <c>reset</c>, which every supported git
	/// understands, is the safer default. Mirroring each caller's own strictness would make
	/// <see cref="ExecuteAsync"/> throw a version exception for what is really a restore problem.
	/// </remarks>
	/// <param name="cancellationToken">A token to observe while probing.</param>
	private async Task ProbeVersionAsync(CancellationToken cancellationToken)
	{
		GitResult<GitVersion> probe = await new GitVersionBuilder(Runner)
			.TryExecuteAsync(cancellationToken).ConfigureAwait(false);

		RestoreSupportedByVersion = probe.Success && probe.Value!.AtLeast(RestoreMajor, RestoreMinor);
	}
}
