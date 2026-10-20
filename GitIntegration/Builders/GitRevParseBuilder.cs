// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

/// <summary>
/// Resolves a revision to the id of the commit it names, peeling an annotated tag to its commit.
/// </summary>
public interface IGitRevParseBuilder : IGitCommandBuilder<GitCommitSha>
{
}

/// <summary>
/// Builds <c>git rev-parse --verify &lt;revision&gt;</c>, then peels the object id it returns with
/// <c>git rev-parse --verify &lt;id&gt;^{commit}</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
/// <param name="revision">The revision to resolve.</param>
internal sealed class GitRevParseBuilder(
	IGitProcessRunner runner,
	AbsoluteDirectoryPath repositoryPath,
	GitRefName revision)
	: GitCommandBuilder<GitCommitSha>(runner, repositoryPath), IGitRevParseBuilder
{
	private readonly AbsoluteDirectoryPath _repositoryPath = Ensure.NotNull(repositoryPath);
	private readonly GitRefName _revision = Ensure.NotNull(revision);
	private readonly bool _isPeelStep;

	/// <summary>
	/// Builds the second step, which peels an object id the first step resolved.
	/// </summary>
	private GitRevParseBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath, GitCommitSha objectId)
		: this(runner, repositoryPath, (Ensure.NotNull(objectId).WeakString + "^{commit}").As<GitRefName>()) =>
		_isPeelStep = true;

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("rev-parse");

		// --verify makes git fail on an unresolvable revision instead of echoing the input back,
		// which is what lets a non-zero exit code stand for "no such revision".
		arguments.Add("--verify");

		// The revision is caller-supplied, so it goes behind the end-of-options marker. It is passed
		// exactly as given: appending ^{commit} here would change what some expressions mean, because
		// git reads everything after ":/" as the message to search for (ktsu-dev/GitIntegration#265).
		AppendOperands(arguments, _revision.WeakString);
	}

	/// <inheritdoc />
	public override async Task<GitCommitSha> ExecuteAsync(CancellationToken cancellationToken = default)
	{
		GitCommitSha objectId = await base.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		return _isPeelStep
			? objectId
			: await CreatePeelStep(objectId).ExecuteAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public override async Task<GitResult<GitCommitSha>> TryExecuteAsync(CancellationToken cancellationToken = default)
	{
		GitResult<GitCommitSha> resolved = await base.TryExecuteAsync(cancellationToken).ConfigureAwait(false);

		return _isPeelStep || !resolved.Success
			? resolved
			: await CreatePeelStep(resolved.Value!).TryExecuteAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	protected override GitCommitSha ParseResult(GitProcessResult result) =>
		GitParseValues.ToSemantic<GitCommitSha>(
			Ensure.NotNull(result).StandardOutput.Trim(),
			"resolved object id");

	// ^{commit} peels the resolved object to the commit it names, which is what the GitCommitSha
	// result promises: without it an annotated tag resolves to the tag object, and a tree or blob
	// revision comes back typed as a commit. With it, those fail like any other revision that names
	// no commit. It only ever follows a plain object id, so it cannot change what the caller asked for.
	private GitRevParseBuilder CreatePeelStep(GitCommitSha objectId) => new(Runner, _repositoryPath, objectId);
}
