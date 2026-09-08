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

	/// <summary>
	/// Lists commits reachable from every reference, not only from HEAD or from
	/// <see cref="ForRevision"/>.
	/// </summary>
	/// <remarks>
	/// Emits <c>--all</c>, which covers everything under <c>refs/</c>, and that breadth is
	/// load-bearing rather than incidental. The narrower <c>--branches</c> covers only
	/// <c>refs/heads</c>, which misses a commit on a detached HEAD — and a detached HEAD is the
	/// <em>normal</em> state of a submodule working directory, since a submodule is checked out at
	/// the commit its gitlink records rather than on a branch. Paired with
	/// <see cref="ExcludingRemoteTrackingRefs"/>, a check built on <c>--branches</c> would therefore
	/// report "nothing unpushed" for a submodule holding commits that exist nowhere else, which is
	/// precisely the case where a wrong answer causes data loss.
	/// </remarks>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitLogBuilder IncludingAllRefs();

	/// <summary>
	/// Excludes every commit any remote-tracking reference already contains.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Emits <c>--not --remotes</c>. Combined with <see cref="IncludingAllRefs"/> this answers
	/// "which commits in this repository does no remote have?" — the check to run before discarding a
	/// working copy, to know whether doing so would destroy work that exists nowhere else. Combined
	/// with <see cref="ForRevision"/> instead, it narrows the same question to one revision's
	/// history.
	/// </para>
	/// <para>
	/// Reported as a commit list rather than a bare yes or no, because a caller that only wants the
	/// yes or no reads <c>Count &gt; 0</c>, while one that wants to show a user what is at stake
	/// already has the commits. It composes with <see cref="Take"/>, <see cref="ForPath"/>, and
	/// <see cref="FirstParentOnly"/> for the same reason a dedicated verb was not added.
	/// </para>
	/// </remarks>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitLogBuilder ExcludingRemoteTrackingRefs();
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
	private bool _includingAllRefs;
	private bool _excludingRemoteTrackingRefs;

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
	public IGitLogBuilder IncludingAllRefs()
	{
		_includingAllRefs = true;
		return this;
	}

	/// <inheritdoc />
	public IGitLogBuilder ExcludingRemoteTrackingRefs()
	{
		_excludingRemoteTrackingRefs = true;
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

		// --all must precede --not: git negates everything following a --not, so "--not --remotes
		// --all" excludes every reference instead of including them, and reports an empty log rather
		// than failing. Verified against git 2.43.
		if (_includingAllRefs)
		{
			arguments.Add("--all");
		}

		if (_excludingRemoteTrackingRefs)
		{
			// The three tokens are emitted together and in this order, and the closing --not is what
			// makes the pair safe rather than merely conventional.
			//
			// --not reverses the sense of every revision specifier that follows it, up to the next
			// --not. Without the closing one, a revision from ForRevision or a pathspec from ForPath
			// would fall inside the negation and be excluded instead of selected — "--not --remotes
			// <revision>" asks for commits in neither, which is a different question that quietly
			// returns nothing rather than failing. Closing the negation immediately scopes it to
			// --remotes alone, so nothing emitted below can be captured by it, now or after a future
			// option is added here.
			//
			// git also requires --not to precede every non-option argument, so this cannot instead be
			// deferred until after the operands: "git log --end-of-options HEAD --not --remotes" dies
			// with "fatal: option '--not' must come before non-option arguments". Both behaviours
			// verified against git 2.43.
			arguments.Add("--not");
			arguments.Add("--remotes");
			arguments.Add("--not");
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
