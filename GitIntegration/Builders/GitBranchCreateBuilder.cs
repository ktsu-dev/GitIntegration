// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Creates a branch.
/// </summary>
public interface IGitBranchCreateBuilder : IGitCommandBuilder<GitCompleted>
{
	/// <summary>
	/// Points the new branch at this revision instead of at HEAD.
	/// </summary>
	/// <param name="startPoint">The revision the branch should start from.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="startPoint"/> is <see langword="null"/>.</exception>
	public IGitBranchCreateBuilder StartingAt(GitRefName startPoint);

	/// <summary>
	/// Resets the branch to the start point if it already exists, instead of failing.
	/// </summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitBranchCreateBuilder Force();
}

/// <summary>
/// Builds <c>git branch &lt;name&gt; [&lt;start-point&gt;]</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
/// <param name="name">The branch to create.</param>
internal sealed class GitBranchCreateBuilder(
	IGitProcessRunner runner,
	AbsoluteDirectoryPath repositoryPath,
	GitBranchName name)
	: GitCommandBuilder<GitCompleted>(runner, repositoryPath), IGitBranchCreateBuilder
{
	private readonly GitBranchName _name = Ensure.NotNull(name);
	private GitRefName? _startPoint;
	private bool _force;

	/// <inheritdoc />
	public IGitBranchCreateBuilder StartingAt(GitRefName startPoint)
	{
		_startPoint = Ensure.NotNull(startPoint);
		return this;
	}

	/// <inheritdoc />
	public IGitBranchCreateBuilder Force()
	{
		_force = true;
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("branch");

		if (_force)
		{
			arguments.Add("--force");
		}

		// Order is load-bearing: git branch takes <name> then an optional <start-point>
		// positionally, and swapping them creates a differently-named branch without complaint.
		if (_startPoint is null)
		{
			AppendOperands(arguments, _name.WeakString);
		}
		else
		{
			AppendOperands(arguments, _name.WeakString, _startPoint.WeakString);
		}
	}

	/// <inheritdoc />
	protected override GitCompleted ParseResult(GitProcessResult result) =>
		new() { Arguments = Ensure.NotNull(result).Arguments };
}
