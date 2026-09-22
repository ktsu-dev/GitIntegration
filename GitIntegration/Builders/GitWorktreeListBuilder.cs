// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

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
/// Builds <c>git worktree list --porcelain</c>.
/// </summary>
/// <remarks>
/// No options. The porcelain listing already reports every attribute this library models, and git's
/// remaining options on this verb either change the format (<c>-v</c>, which is the human-facing
/// form) or add a field this library reads from the porcelain output anyway (<c>--expire</c>, which
/// only affects which entries are annotated prunable).
/// </remarks>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
internal sealed class GitWorktreeListBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)
	: GitCommandBuilder<IReadOnlyList<GitWorktree>>(runner, repositoryPath), IGitWorktreeListBuilder
{
	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("worktree");
		arguments.Add("list");
		arguments.Add("--porcelain");
	}

	/// <inheritdoc />
	protected override IReadOnlyList<GitWorktree> ParseResult(GitProcessResult result) =>
		GitWorktreeParser.Parse(Ensure.NotNull(result).StandardOutput);
}
