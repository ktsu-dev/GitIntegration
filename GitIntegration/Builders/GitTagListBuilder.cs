// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Lists tag references.
/// </summary>
public interface IGitTagListBuilder : IGitCommandBuilder<IReadOnlyList<GitTag>>
{
}

/// <summary>
/// Builds <c>git for-each-ref</c> over the tag namespace.
/// </summary>
/// <remarks>
/// <c>for-each-ref</c> rather than <c>tag --list</c>, for the reason
/// <see cref="GitBranchListBuilder"/> gives: it takes an explicit format string, so the output is
/// machine-readable by construction rather than by hoping a human-facing listing keeps its shape.
/// <c>tag --list</c> prints bare names and nothing else without a format of its own, so it could not
/// answer whether a tag is annotated at all.
/// </remarks>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
internal sealed class GitTagListBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)
	: GitCommandBuilder<IReadOnlyList<GitTag>>(runner, repositoryPath), IGitTagListBuilder
{
	private const string TagPrefix = "refs/tags";

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("for-each-ref");
		arguments.Add("--format=" + GitOutputFormats.ForEachTagFormat);

		// A library constant, not a caller-supplied operand, so it needs no --end-of-options guard:
		// no caller value can reach this position.
		arguments.Add(TagPrefix);
	}

	/// <inheritdoc />
	protected override IReadOnlyList<GitTag> ParseResult(GitProcessResult result) =>
		GitTagParser.Parse(Ensure.NotNull(result).StandardOutput);
}
