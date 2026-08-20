// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Deletes a branch.
/// </summary>
public interface IGitBranchDeleteBuilder : IGitCommandBuilder<GitCompleted>
{
	/// <summary>
	/// Deletes the branch even when it has commits no other branch contains.
	/// </summary>
	/// <remarks>
	/// Without this, git refuses to delete an unmerged branch and exits with code 1. With it, the
	/// commits on that branch become unreachable and are eventually garbage collected.
	/// </remarks>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitBranchDeleteBuilder Force();
}

/// <summary>
/// Builds <c>git branch --delete &lt;name&gt;</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
/// <param name="name">The branch to delete.</param>
internal sealed class GitBranchDeleteBuilder(
	IGitProcessRunner runner,
	AbsoluteDirectoryPath repositoryPath,
	GitBranchName name)
	: GitCommandBuilder<GitCompleted>(runner, repositoryPath), IGitBranchDeleteBuilder
{
	private readonly GitBranchName _name = Ensure.NotNull(name);
	private bool _force;

	/// <inheritdoc />
	public IGitBranchDeleteBuilder Force()
	{
		_force = true;
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("branch");

		// Long forms throughout: --delete --force is -D, and spelling it out keeps the vector
		// readable when it is copied out of a GitCommandException and rerun by hand.
		arguments.Add("--delete");

		if (_force)
		{
			arguments.Add("--force");
		}

		AppendOperands(arguments, _name.WeakString);
	}

	/// <inheritdoc />
	protected override GitCompleted ParseResult(GitProcessResult result) =>
		new() { Arguments = Ensure.NotNull(result).Arguments };
}
