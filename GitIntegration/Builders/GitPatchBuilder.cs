// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Reads a patch: the hunks and lines a caller needs to show or stage a change, rather than the
/// per-file counts <see cref="IGitDiffBuilder"/> reports.
/// </summary>
public interface IGitPatchBuilder : IGitCommandBuilder<GitPatch>
{
	/// <summary>Compares the index against HEAD instead of the working tree against the index.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitPatchBuilder Staged();

	/// <summary>Sets how many lines of surrounding context each hunk carries.</summary>
	/// <param name="lines">The number of context lines. Git's own default applies when this is never called.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="lines"/> is negative.</exception>
	public IGitPatchBuilder WithContext(int lines);

	/// <summary>Limits the result to this path. May be called more than once.</summary>
	/// <param name="path">The path, relative to the repository root.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
	public IGitPatchBuilder ForPath(RelativeFilePath path);

	/// <summary>
	/// Compares against one revision. Replaces any previous revision selection.
	/// </summary>
	/// <param name="revision">The revision to compare against.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="revision"/> is <see langword="null"/>.</exception>
	public IGitPatchBuilder Against(GitRefName revision);

	/// <summary>
	/// Compares two revisions. Replaces any previous revision selection.
	/// </summary>
	/// <param name="fromRevision">The revision to compare from.</param>
	/// <param name="toRevision">The revision to compare to.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="fromRevision"/> or <paramref name="toRevision"/> is <see langword="null"/>.
	/// </exception>
	public IGitPatchBuilder Between(GitRefName fromRevision, GitRefName toRevision);

	/// <summary>Reports a delete and an add of similar content as a rename.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitPatchBuilder DetectRenames();
}

/// <summary>
/// Builds <c>git diff --no-ext-diff --no-textconv --no-color</c> and parses its output through
/// <see cref="GitPatchParser"/>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
internal sealed class GitPatchBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)
	: GitCommandBuilder<GitPatch>(runner, repositoryPath), IGitPatchBuilder
{
	private readonly List<RelativeFilePath> _paths = [];

	// One slot, so Against and Between cannot combine into a three-revision vector git would reject.
	private string[] _revisions = [];
	private bool _staged;
	private int? _contextLines;
	private bool _detectRenames;

	/// <inheritdoc />
	public IGitPatchBuilder Staged()
	{
		_staged = true;
		return this;
	}

	/// <inheritdoc />
	public IGitPatchBuilder WithContext(int lines)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(lines);
		_contextLines = lines;
		return this;
	}

	/// <inheritdoc />
	public IGitPatchBuilder ForPath(RelativeFilePath path)
	{
		_paths.Add(Ensure.NotNull(path));
		return this;
	}

	/// <inheritdoc />
	public IGitPatchBuilder Against(GitRefName revision)
	{
		_revisions = [Ensure.NotNull(revision).WeakString];
		return this;
	}

	/// <inheritdoc />
	public IGitPatchBuilder Between(GitRefName fromRevision, GitRefName toRevision)
	{
		_revisions = [Ensure.NotNull(fromRevision).WeakString, Ensure.NotNull(toRevision).WeakString];
		return this;
	}

	/// <inheritdoc />
	public IGitPatchBuilder DetectRenames()
	{
		_detectRenames = true;
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("diff");

		// Correctness, not tidiness. A repository with a gitattributes diff driver emits
		// human-readable output in place of a patch, which no caller can apply, and --no-color
		// guards a config setting color.diff to always, which would put escape sequences into text
		// that goes back to apply. color.ui on the base vector does not cover that: git lets
		// color.diff take precedence over it.
		arguments.Add("--no-ext-diff");
		arguments.Add("--no-textconv");
		arguments.Add("--no-color");

		if (_staged)
		{
			arguments.Add("--cached");
		}

		if (_contextLines is int contextLines)
		{
			arguments.Add($"-U{contextLines}");
		}

		if (_detectRenames)
		{
			arguments.Add("--find-renames");
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
	protected override GitPatch ParseResult(GitProcessResult result) =>
		GitPatchParser.Parse(Ensure.NotNull(result).StandardOutput);
}
