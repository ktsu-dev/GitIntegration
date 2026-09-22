// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Removes a working tree and the administrative record of it.
/// </summary>
public interface IGitWorktreeRemoveBuilder : IGitCommandBuilder<GitCompleted>
{
	/// <summary>Removes the worktree even when its working directory is not clean.</summary>
	/// <remarks>
	/// Git refuses to remove a worktree holding modified tracked files or untracked files, since
	/// doing so discards them. This says the caller knows.
	/// </remarks>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitWorktreeRemoveBuilder Force();
}

/// <summary>
/// Removes administrative records for working trees whose directories have gone.
/// </summary>
/// <remarks>
/// No options. <c>--dry-run</c> is deliberately not exposed: it would change this verb's result
/// from <see cref="GitCompleted"/> into a listing, and a caller that wants to know what would be
/// removed reads <see cref="GitWorktree.IsPrunable"/> from a listing it can already obtain.
/// </remarks>
public interface IGitWorktreePruneBuilder : IGitCommandBuilder<GitCompleted>
{
}

/// <summary>
/// Builds <c>git worktree remove</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
/// <param name="path">The worktree to remove.</param>
internal sealed class GitWorktreeRemoveBuilder(
	IGitProcessRunner runner,
	AbsoluteDirectoryPath repositoryPath,
	AbsoluteDirectoryPath path)
	: GitCommandBuilder<GitCompleted>(runner, repositoryPath), IGitWorktreeRemoveBuilder
{
	private readonly AbsoluteDirectoryPath _path = Ensure.NotNull(path);

	private bool _force;

	/// <inheritdoc />
	public IGitWorktreeRemoveBuilder Force()
	{
		_force = true;
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("worktree");
		arguments.Add("remove");

		if (_force)
		{
			arguments.Add("--force");
		}

		AppendOperands(arguments, _path.WeakString);
	}

	/// <inheritdoc />
	protected override GitCompleted ParseResult(GitProcessResult result) =>
		new() { Arguments = Ensure.NotNull(result).Arguments };
}

/// <summary>
/// Builds <c>git worktree prune</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
internal sealed class GitWorktreePruneBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)
	: GitCommandBuilder<GitCompleted>(runner, repositoryPath), IGitWorktreePruneBuilder
{
	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("worktree");
		arguments.Add("prune");
	}

	/// <inheritdoc />
	protected override GitCompleted ParseResult(GitProcessResult result) =>
		new() { Arguments = Ensure.NotNull(result).Arguments };
}
