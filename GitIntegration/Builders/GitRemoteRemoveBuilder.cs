// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Removes a remote and every remote-tracking branch belonging to it.
/// </summary>
public interface IGitRemoteRemoveBuilder : IGitCommandBuilder<GitCompleted>
{
}

/// <summary>
/// Builds <c>git remote remove &lt;name&gt;</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
/// <param name="name">The remote to remove.</param>
internal sealed class GitRemoteRemoveBuilder(
	IGitProcessRunner runner,
	AbsoluteDirectoryPath repositoryPath,
	GitRemoteName name)
	: GitCommandBuilder<GitCompleted>(runner, repositoryPath), IGitRemoteRemoveBuilder
{
	private readonly GitRemoteName _name = Ensure.NotNull(name);

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("remote");
		arguments.Add("remove");

		AppendOperands(arguments, _name.WeakString);
	}

	/// <inheritdoc />
	protected override GitCompleted ParseResult(GitProcessResult result) =>
		new() { Arguments = Ensure.NotNull(result).Arguments };
}
