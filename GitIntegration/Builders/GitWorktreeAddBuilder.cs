// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Creates an additional working tree.
/// </summary>
/// <remarks>
/// <see cref="CheckingOut"/>, <see cref="CreatingBranch"/>, <see cref="CreatingOrResettingBranch"/>
/// and <see cref="Detached"/> are four settings of one mode, and a later call replaces an earlier
/// one, as <c>IGitBranchListBuilder</c>'s <c>LocalOnly</c> and <c>RemoteOnly</c> already do. A vector
/// carrying two of them at once is one git rejects.
/// </remarks>
public interface IGitWorktreeAddBuilder : IGitCommandBuilder<GitCompleted>
{
	/// <summary>Checks out an existing branch in the new worktree. Replaces any previous mode.</summary>
	/// <param name="branch">The branch to check out.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitWorktreeAddBuilder CheckingOut(GitBranchName branch);

	/// <summary>Creates a branch and checks it out in the new worktree. Replaces any previous mode.</summary>
	/// <remarks>Fails when the branch already exists; see <see cref="CreatingOrResettingBranch"/>.</remarks>
	/// <param name="branch">The branch to create.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitWorktreeAddBuilder CreatingBranch(GitBranchName branch);

	/// <summary>
	/// Creates a branch, resetting it when it already exists, and checks it out. Replaces any
	/// previous mode.
	/// </summary>
	/// <param name="branch">The branch to create or reset.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitWorktreeAddBuilder CreatingOrResettingBranch(GitBranchName branch);

	/// <summary>Checks out a commit rather than a branch. Replaces any previous mode.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitWorktreeAddBuilder Detached();

	/// <summary>
	/// Sets the commit-ish the new worktree starts from: the start point for the branch-creating
	/// modes, and the commit to detach at for <see cref="Detached"/>.
	/// </summary>
	/// <remarks>
	/// Writes the same operand as <see cref="CheckingOut"/>, so the later of the two calls wins.
	/// Distinct from it only in taking a <see cref="GitRefName"/>, which admits a tag or a raw
	/// revision rather than a branch alone.
	/// </remarks>
	/// <param name="commitish">The revision to start from.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitWorktreeAddBuilder From(GitRefName commitish);

	/// <summary>Creates the worktree even when git would otherwise refuse.</summary>
	/// <remarks>
	/// Git refuses when the branch is already checked out in another worktree, and when the
	/// destination is a missing-but-registered worktree directory.
	/// </remarks>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitWorktreeAddBuilder Force();

	/// <summary>Registers the worktree without populating its working directory.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitWorktreeAddBuilder WithoutCheckout();
}

/// <summary>
/// Builds <c>git worktree add</c>.
/// </summary>
/// <remarks>
/// <c>--guess-remote</c> is deliberately not exposed. Plain <c>git worktree add &lt;path&gt;
/// &lt;branch&gt;</c> already creates a local tracking branch when the name matches exactly one
/// remote, which covers creating a worktree for a branch that exists only on the remote.
/// <c>--guess-remote</c> extends that to the case where no branch is named at all, which this
/// builder always names.
/// </remarks>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
/// <param name="path">Where the new worktree goes.</param>
internal sealed class GitWorktreeAddBuilder(
	IGitProcessRunner runner,
	AbsoluteDirectoryPath repositoryPath,
	AbsoluteDirectoryPath path)
	: GitCommandBuilder<GitCompleted>(runner, repositoryPath), IGitWorktreeAddBuilder
{
	private enum AddMode
	{
		Plain,
		CreateBranch,
		CreateOrResetBranch,
		Detach,
	}

	private readonly AbsoluteDirectoryPath _path = Ensure.NotNull(path);

	private AddMode _mode = AddMode.Plain;
	private string? _branch;
	private string? _commitish;
	private bool _force;
	private bool _withoutCheckout;

	/// <inheritdoc />
	public IGitWorktreeAddBuilder CheckingOut(GitBranchName branch)
	{
		Ensure.NotNull(branch);

		_mode = AddMode.Plain;
		_branch = null;
		_commitish = branch.WeakString;
		return this;
	}

	/// <inheritdoc />
	public IGitWorktreeAddBuilder CreatingBranch(GitBranchName branch)
	{
		Ensure.NotNull(branch);

		_mode = AddMode.CreateBranch;
		_branch = branch.WeakString;
		return this;
	}

	/// <inheritdoc />
	public IGitWorktreeAddBuilder CreatingOrResettingBranch(GitBranchName branch)
	{
		Ensure.NotNull(branch);

		_mode = AddMode.CreateOrResetBranch;
		_branch = branch.WeakString;
		return this;
	}

	/// <inheritdoc />
	public IGitWorktreeAddBuilder Detached()
	{
		_mode = AddMode.Detach;
		_branch = null;
		return this;
	}

	/// <inheritdoc />
	public IGitWorktreeAddBuilder From(GitRefName commitish)
	{
		Ensure.NotNull(commitish);

		_commitish = commitish.WeakString;
		return this;
	}

	/// <inheritdoc />
	public IGitWorktreeAddBuilder Force()
	{
		_force = true;
		return this;
	}

	/// <inheritdoc />
	public IGitWorktreeAddBuilder WithoutCheckout()
	{
		_withoutCheckout = true;
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("worktree");
		arguments.Add("add");

		if (_force)
		{
			arguments.Add("--force");
		}

		if (_withoutCheckout)
		{
			arguments.Add("--no-checkout");
		}

		switch (_mode)
		{
			case AddMode.CreateBranch:
				arguments.Add("-b");
				arguments.Add(_branch!);
				break;
			case AddMode.CreateOrResetBranch:
				arguments.Add("-B");
				arguments.Add(_branch!);
				break;
			case AddMode.Detach:
				arguments.Add("--detach");
				break;
			case AddMode.Plain:
			default:
				break;
		}

		// The path and the commit-ish are both caller-supplied operands and share one end-of-options
		// marker, which git reads as applying to everything after it.
		if (_commitish is null)
		{
			AppendOperands(arguments, _path.WeakString);
		}
		else
		{
			AppendOperands(arguments, _path.WeakString, _commitish);
		}
	}

	/// <inheritdoc />
	protected override GitCompleted ParseResult(GitProcessResult result) =>
		new() { Arguments = Ensure.NotNull(result).Arguments };
}
