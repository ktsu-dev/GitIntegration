// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Lists the paths that differ between two states of the repository.
/// </summary>
public interface IGitDiffBuilder : IGitCommandBuilder<IReadOnlyList<GitDiffEntry>>
{
	/// <summary>Compares the index against HEAD instead of the working tree against the index.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitDiffBuilder Staged();

	/// <summary>
	/// Compares against one revision. Replaces any previous revision selection.
	/// </summary>
	/// <param name="revision">The revision to compare against.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="revision"/> is <see langword="null"/>.</exception>
	public IGitDiffBuilder Against(GitRefName revision);

	/// <summary>
	/// Compares two revisions. Replaces any previous revision selection.
	/// </summary>
	/// <param name="from">The revision to compare from.</param>
	/// <param name="target">The revision to compare to.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="from"/> or <paramref name="target"/> is <see langword="null"/>.
	/// </exception>
	public IGitDiffBuilder Between(GitRefName from, GitRefName target);

	/// <summary>Reports a delete and an add of similar content as a rename.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitDiffBuilder DetectRenames();

	/// <summary>Reports an add whose content came from an existing file as a copy.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitDiffBuilder DetectCopies();

	/// <summary>Limits the result to this path. May be called more than once.</summary>
	/// <param name="path">The path, relative to the repository root.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
	public IGitDiffBuilder ForPath(RelativeFilePath path);
}

/// <summary>
/// Builds <c>git diff --name-status -z</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
internal sealed class GitDiffBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)
	: GitCommandBuilder<IReadOnlyList<GitDiffEntry>>(runner, repositoryPath), IGitDiffBuilder
{
	private readonly List<RelativeFilePath> _paths = [];

	// One slot, so Against and Between cannot combine into a three-revision vector git would reject.
	private string[] _revisions = [];
	private bool _staged;
	private bool _detectRenames;
	private bool _detectCopies;

	/// <inheritdoc />
	public IGitDiffBuilder Staged()
	{
		_staged = true;
		return this;
	}

	/// <inheritdoc />
	public IGitDiffBuilder Against(GitRefName revision)
	{
		_revisions = [Ensure.NotNull(revision).WeakString];
		return this;
	}

	/// <inheritdoc />
	public IGitDiffBuilder Between(GitRefName from, GitRefName target)
	{
		_revisions = [Ensure.NotNull(from).WeakString, Ensure.NotNull(target).WeakString];
		return this;
	}

	/// <inheritdoc />
	public IGitDiffBuilder DetectRenames()
	{
		_detectRenames = true;
		return this;
	}

	/// <inheritdoc />
	public IGitDiffBuilder DetectCopies()
	{
		_detectCopies = true;
		return this;
	}

	/// <inheritdoc />
	public IGitDiffBuilder ForPath(RelativeFilePath path)
	{
		_paths.Add(Ensure.NotNull(path));
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("diff");
		arguments.Add("--name-status");
		arguments.Add("-z");

		if (_staged)
		{
			arguments.Add("--cached");
		}

		if (_detectRenames)
		{
			arguments.Add("--find-renames");
		}

		if (_detectCopies)
		{
			arguments.Add("--find-copies");
		}

		if (_revisions.Length > 0)
		{
			AppendOperands(arguments, _revisions);
		}

		if (_paths.Count > 0)
		{
			arguments.Add("--");

			foreach (RelativeFilePath path in _paths)
			{
				arguments.Add(path.WeakString);
			}
		}
	}

	/// <inheritdoc />
	protected override IReadOnlyList<GitDiffEntry> ParseResult(GitProcessResult result) =>
		GitDiffParser.Parse(Ensure.NotNull(result).StandardOutput);
}
