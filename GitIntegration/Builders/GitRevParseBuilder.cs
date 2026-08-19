// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Resolves a revision to the object id it names.
/// </summary>
public interface IGitRevParseBuilder : IGitCommandBuilder<GitCommitSha>
{
}

/// <summary>
/// Builds <c>git rev-parse --verify</c>.
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
	private readonly GitRefName _revision = Ensure.NotNull(revision);

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("rev-parse");

		// --verify makes git fail on an unresolvable revision instead of echoing the input back,
		// which is what lets a non-zero exit code stand for "no such revision".
		arguments.Add("--verify");

		// The revision is caller-supplied, so it goes behind the end-of-options marker.
		AppendOperands(arguments, _revision.WeakString);
	}

	/// <inheritdoc />
	protected override GitCommitSha ParseResult(GitProcessResult result) =>
		GitParseValues.ToSemantic<GitCommitSha>(
			Ensure.NotNull(result).StandardOutput.Trim(),
			"resolved object id");
}
