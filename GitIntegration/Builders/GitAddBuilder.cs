// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Stages changes for the next commit.
/// </summary>
public interface IGitAddBuilder : IGitCommandBuilder<GitCompleted>
{
	/// <summary>Stages this path. May be called more than once.</summary>
	/// <remarks>
	/// At least one of this, <see cref="All"/> or <see cref="UpdateTrackedOnly"/> is required:
	/// <c>BuildArguments</c> throws <see cref="InvalidOperationException"/> when none was called,
	/// because a bare <c>git add</c> stages nothing yet exits 0.
	/// </remarks>
	/// <param name="path">The path, relative to the repository root.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
	public IGitAddBuilder ForPath(RelativeFilePath path);

	/// <summary>Stages every change in the working tree, including new and deleted files.</summary>
	/// <remarks>
	/// Cannot be combined with <see cref="UpdateTrackedOnly"/>: git rejects <c>--all</c> with
	/// <c>--update</c>, so <c>BuildArguments</c> throws <see cref="InvalidOperationException"/> when
	/// both are set, before a process is spawned.
	/// </remarks>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitAddBuilder All();

	/// <summary>
	/// Stages changes to files git already tracks, leaving untracked files alone.
	/// </summary>
	/// <remarks>
	/// Cannot be combined with <see cref="All"/>, for the same reason.
	/// </remarks>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitAddBuilder UpdateTrackedOnly();
}

/// <summary>
/// Builds <c>git add</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
internal sealed class GitAddBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)
	: GitCommandBuilder<GitCompleted>(runner, repositoryPath), IGitAddBuilder
{
	private readonly List<RelativeFilePath> _paths = [];
	private bool _all;
	private bool _updateTrackedOnly;

	/// <inheritdoc />
	public IGitAddBuilder ForPath(RelativeFilePath path)
	{
		_paths.Add(Ensure.NotNull(path));
		return this;
	}

	/// <inheritdoc />
	public IGitAddBuilder All()
	{
		_all = true;
		return this;
	}

	/// <inheritdoc />
	public IGitAddBuilder UpdateTrackedOnly()
	{
		_updateTrackedOnly = true;
		return this;
	}

	/// <inheritdoc />
	protected override bool PassesPathsLiterally => _paths.Count > 0;

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		// Both mistakes are refused here, before a process is spawned, the way Fetch and Checkout
		// refuse their own contradictions. A bare "git add" prints "Nothing specified, nothing added."
		// and exits 0, so it would otherwise report success having staged nothing. "--all --update"
		// does fail, but only as git's "options '-A' and '-u' cannot be used together" (exit 128),
		// which names neither of the two calls that caused it. Verified against git 2.43.
		if (_all && _updateTrackedOnly)
		{
			throw new InvalidOperationException(
				"All and UpdateTrackedOnly cannot both be requested: git add rejects --all with --update.");
		}

		if (!_all && !_updateTrackedOnly && _paths.Count == 0)
		{
			throw new InvalidOperationException(
				"Add needs something to stage: call ForPath, All or UpdateTrackedOnly. A bare git add " +
				"stages nothing and still reports success.");
		}

		arguments.Add("add");

		if (_all)
		{
			arguments.Add("--all");
		}

		if (_updateTrackedOnly)
		{
			arguments.Add("--update");
		}

		if (_paths.Count > 0)
		{
			// Pathspecs are relative to the -C directory, which GitClient always resolves to the
			// working-tree root, so a RelativeFilePath maps onto one with no translation.
			string[] operands = new string[_paths.Count];

			for (int index = 0; index < _paths.Count; index++)
			{
				operands[index] = _paths[index].WeakString;
			}

			AppendOperands(arguments, operands);
		}
	}

	/// <inheritdoc />
	protected override GitCompleted ParseResult(GitProcessResult result) =>
		new() { Arguments = Ensure.NotNull(result).Arguments };
}
