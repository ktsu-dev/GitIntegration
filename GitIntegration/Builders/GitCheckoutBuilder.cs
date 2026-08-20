// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Switches the working tree to a different branch, tag, or commit.
/// </summary>
/// <remarks>
/// <c>checkout</c> rather than the newer <c>switch</c>, because the target is a
/// <see cref="GitRefName"/> — which may be a branch, a tag, or an object id — and <c>switch</c>
/// handles only branches.
/// </remarks>
public interface IGitCheckoutBuilder : IGitCommandBuilder<GitCompleted>
{
	/// <summary>Creates the target as a new branch at the current HEAD and switches to it.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitCheckoutBuilder CreatingBranch();

	/// <summary>
	/// Switches even when it would discard uncommitted changes in the working tree.
	/// </summary>
	/// <remarks>Local modifications to files that differ between the two trees are lost.</remarks>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitCheckoutBuilder Force();

	/// <summary>Checks the target out as a detached HEAD rather than switching to a branch.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitCheckoutBuilder Detach();
}

/// <summary>
/// Builds <c>git checkout</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
/// <param name="target">The branch, tag, or commit to switch to.</param>
internal sealed class GitCheckoutBuilder(
	IGitProcessRunner runner,
	AbsoluteDirectoryPath repositoryPath,
	GitRefName target)
	: GitCommandBuilder<GitCompleted>(runner, repositoryPath), IGitCheckoutBuilder
{
	private readonly GitRefName _target = Ensure.NotNull(target);
	private bool _creatingBranch;
	private bool _force;
	private bool _detach;

	/// <inheritdoc />
	public IGitCheckoutBuilder CreatingBranch()
	{
		_creatingBranch = true;
		return this;
	}

	/// <inheritdoc />
	public IGitCheckoutBuilder Force()
	{
		_force = true;
		return this;
	}

	/// <inheritdoc />
	public IGitCheckoutBuilder Detach()
	{
		_detach = true;
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("checkout");

		// -b has no long form, unlike every other flag this library emits.
		if (_creatingBranch)
		{
			arguments.Add("-b");
		}

		if (_force)
		{
			arguments.Add("--force");
		}

		if (_detach)
		{
			arguments.Add("--detach");
		}

		// Every flag must precede the marker: anything after it is an operand, so a flag emitted
		// there would reach git as a ref name.
		AppendOperands(arguments, _target.WeakString);
	}

	/// <inheritdoc />
	protected override GitCompleted ParseResult(GitProcessResult result) =>
		new() { Arguments = Ensure.NotNull(result).Arguments };
}
