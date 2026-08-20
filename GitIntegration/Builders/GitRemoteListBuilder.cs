// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Lists the configured remotes.
/// </summary>
public interface IGitRemoteListBuilder : IGitCommandBuilder<IReadOnlyList<GitRemote>>
{
}

/// <summary>
/// Builds <c>git remote -v</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
internal sealed class GitRemoteListBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)
	: GitCommandBuilder<IReadOnlyList<GitRemote>>(runner, repositoryPath), IGitRemoteListBuilder
{
	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("remote");
		arguments.Add("-v");
	}

	/// <inheritdoc />
	protected override IReadOnlyList<GitRemote> ParseResult(GitProcessResult result) =>
		GitRemoteParser.Parse(Ensure.NotNull(result).StandardOutput);
}
