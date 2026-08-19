// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Globalization;

using ktsu.Semantics.Paths;

/// <summary>
/// Lists commits.
/// </summary>
public interface IGitLogBuilder : IGitCommandBuilder<IReadOnlyList<GitCommit>>
{
	/// <summary>Limits the result to at most this many commits.</summary>
	/// <param name="maxCount">The maximum number of commits to return.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="maxCount"/> is negative.</exception>
	public IGitLogBuilder Take(int maxCount);

	/// <summary>Skips this many commits before returning any.</summary>
	/// <param name="count">The number of commits to skip.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
	public IGitLogBuilder Skip(int count);

	/// <summary>
	/// Lists commits reachable from this revision instead of from HEAD. A range expression such as
	/// <c>main..feature</c> is accepted.
	/// </summary>
	/// <param name="revision">The revision or range.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="revision"/> is <see langword="null"/>.</exception>
	public IGitLogBuilder ForRevision(GitRefName revision);

	/// <summary>Limits the result to commits touching this path. May be called more than once.</summary>
	/// <param name="path">The path, relative to the repository root.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
	public IGitLogBuilder ForPath(RelativeFilePath path);

	/// <summary>Follows only the first parent of a merge, hiding the merged-in history.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitLogBuilder FirstParentOnly();
}

/// <summary>
/// Builds <c>git log -z</c> with this library's pinned format.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
internal sealed class GitLogBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)
	: GitCommandBuilder<IReadOnlyList<GitCommit>>(runner, repositoryPath), IGitLogBuilder
{
	private readonly List<RelativeFilePath> _paths = [];
	private int? _maxCount;
	private int? _skip;
	private GitRefName? _revision;
	private bool _firstParentOnly;

	/// <inheritdoc />
	public IGitLogBuilder Take(int maxCount)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(maxCount);
		_maxCount = maxCount;
		return this;
	}

	/// <inheritdoc />
	public IGitLogBuilder Skip(int count)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(count);
		_skip = count;
		return this;
	}

	/// <inheritdoc />
	public IGitLogBuilder ForRevision(GitRefName revision)
	{
		_revision = Ensure.NotNull(revision);
		return this;
	}

	/// <inheritdoc />
	public IGitLogBuilder ForPath(RelativeFilePath path)
	{
		_paths.Add(Ensure.NotNull(path));
		return this;
	}

	/// <inheritdoc />
	public IGitLogBuilder FirstParentOnly()
	{
		_firstParentOnly = true;
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("log");
		arguments.Add("-z");
		arguments.Add("--format=" + GitOutputFormats.LogFormat);

		if (_maxCount is int maxCount)
		{
			arguments.Add("--max-count=" + maxCount.ToString(CultureInfo.InvariantCulture));
		}

		if (_skip is int skip)
		{
			arguments.Add("--skip=" + skip.ToString(CultureInfo.InvariantCulture));
		}

		if (_firstParentOnly)
		{
			arguments.Add("--first-parent");
		}

		if (_revision is not null)
		{
			AppendOperands(arguments, _revision.WeakString);
		}

		if (_paths.Count > 0)
		{
			// A pathspec goes after --, which is its own end-of-options marker for that position.
			// The revision must already have been emitted: git reads everything after -- as a
			// filename, so a revision placed there returns an empty log rather than failing.
			arguments.Add("--");

			foreach (RelativeFilePath path in _paths)
			{
				arguments.Add(path.WeakString);
			}
		}
	}

	/// <inheritdoc />
	protected override IReadOnlyList<GitCommit> ParseResult(GitProcessResult result) =>
		GitLogParser.Parse(Ensure.NotNull(result).StandardOutput);
}
