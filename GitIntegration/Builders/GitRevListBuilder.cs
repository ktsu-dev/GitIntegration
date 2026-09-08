// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Globalization;

using ktsu.Semantics.Paths;

/// <summary>
/// Counts the commits a revision or range names.
/// </summary>
/// <remarks>
/// <c>rev-list --count</c> prints the number directly. The alternative — listing the commits with
/// <c>Log()</c> and reading <c>Count</c> — runs git with this library's pinned format and puts every
/// record through <see cref="GitLogParser"/>, building a full <see cref="GitCommit"/> with both
/// signatures, the subject, the body, and the parents, for every commit, in order to produce a
/// single integer. Over a few commits the difference is negligible; over thousands it is a great
/// deal of process output, allocation, and parsing thrown away.
/// <para>
/// The two are also not equally reliable. <see cref="GitLogParser"/> can raise
/// <see cref="GitParseException"/> on a commit whose record does not have the expected shape, and a
/// counting query has no reason to be able to fail that way.
/// </para>
/// <para>
/// For the common case — how far the current branch has diverged from <em>its own</em> upstream —
/// <see cref="GitStatus.Ahead"/> and <see cref="GitStatus.Behind"/> already answer it from a single
/// <c>Status()</c> call and need nothing here.
/// </para>
/// </remarks>
public interface IGitRevListBuilder : IGitCommandBuilder<int>
{
	/// <summary>Counts only first parents, ignoring the history merged in by a merge commit.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitRevListBuilder FirstParentOnly();

	/// <summary>Counts only commits touching this path. May be called more than once.</summary>
	/// <param name="path">The path, relative to the repository root.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
	public IGitRevListBuilder ForPath(RelativeFilePath path);
}

/// <summary>
/// Builds <c>git rev-list --count</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
/// <param name="revision">The revision or range to count.</param>
internal sealed class GitRevListBuilder(
	IGitProcessRunner runner,
	AbsoluteDirectoryPath repositoryPath,
	GitRefName revision)
	: GitCommandBuilder<int>(runner, repositoryPath), IGitRevListBuilder
{
	private readonly GitRefName _revision = Ensure.NotNull(revision);
	private readonly List<RelativeFilePath> _paths = [];
	private bool _firstParentOnly;

	/// <inheritdoc />
	public IGitRevListBuilder FirstParentOnly()
	{
		_firstParentOnly = true;
		return this;
	}

	/// <inheritdoc />
	public IGitRevListBuilder ForPath(RelativeFilePath path)
	{
		_paths.Add(Ensure.NotNull(path));
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("rev-list");
		arguments.Add("--count");

		if (_firstParentOnly)
		{
			arguments.Add("--first-parent");
		}

		AppendOperands(arguments, _revision.WeakString);

		if (_paths.Count > 0)
		{
			// A pathspec goes after --, exactly as it does in GitLogBuilder, and the revision must
			// already have been emitted: git reads everything after -- as a filename, so a revision
			// placed there counts nothing rather than failing.
			arguments.Add("--");

			foreach (RelativeFilePath path in _paths)
			{
				arguments.Add(path.WeakString);
			}
		}
	}

	/// <inheritdoc />
	protected override int ParseResult(GitProcessResult result) =>
		GitRevListParser.ParseCount(Ensure.NotNull(result).StandardOutput);
}

/// <summary>
/// Counts how far two revisions have diverged from each other.
/// </summary>
/// <remarks>
/// <c>rev-list --count --left-right &lt;upstream&gt;...&lt;local&gt;</c>, with three dots, which
/// prints both counts from one invocation. This is a genuinely different question from a plain
/// count rather than an option that quietly changes what <c>ExecuteAsync</c> returns, which is why
/// it is a separate builder with its own result type.
/// <para>
/// <see cref="GitStatus.Ahead"/> and <see cref="GitStatus.Behind"/> answer the same question only
/// for HEAD against its configured upstream. This answers it for any two revisions, including two
/// that have no tracking relationship at all.
/// </para>
/// </remarks>
public interface IGitRevListDivergenceBuilder : IGitCommandBuilder<GitDivergence>
{
}

/// <summary>
/// Builds <c>git rev-list --count --left-right &lt;upstream&gt;...&lt;local&gt;</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
/// <param name="upstream">The revision whose exclusive commits are counted as <c>Behind</c>.</param>
/// <param name="local">The revision whose exclusive commits are counted as <c>Ahead</c>.</param>
internal sealed class GitRevListDivergenceBuilder(
	IGitProcessRunner runner,
	AbsoluteDirectoryPath repositoryPath,
	GitRefName upstream,
	GitRefName local)
	: GitCommandBuilder<GitDivergence>(runner, repositoryPath), IGitRevListDivergenceBuilder
{
	private readonly GitRefName _upstream = Ensure.NotNull(upstream);
	private readonly GitRefName _local = Ensure.NotNull(local);

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("rev-list");
		arguments.Add("--count");
		arguments.Add("--left-right");

		// Three dots, not two. "a..b" names the commits b has and a does not — one number. "a...b"
		// names the commits either has and the other does not, which is the pair --left-right then
		// splits. Building the expression here rather than making the caller write it is what lets
		// both revisions stay GitRefName values, each carrying its own NotAnOption validation,
		// instead of being concatenated by a caller into one string this library cannot check.
		AppendOperands(arguments, $"{_upstream.WeakString}...{_local.WeakString}");
	}

	/// <inheritdoc />
	protected override GitDivergence ParseResult(GitProcessResult result) =>
		GitRevListParser.ParseDivergence(Ensure.NotNull(result).StandardOutput);
}

/// <summary>
/// Reads the counts <c>git rev-list --count</c> prints.
/// </summary>
internal static class GitRevListParser
{
	/// <summary>
	/// Parses the single integer <c>rev-list --count</c> prints.
	/// </summary>
	/// <param name="output">Everything git wrote to standard output.</param>
	/// <returns>The commit count.</returns>
	/// <exception cref="GitParseException">The output was not a single whole number.</exception>
	internal static int ParseCount(string output)
	{
		Ensure.NotNull(output);

		string trimmed = output.Trim();

		return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)
			? count
			: throw new GitParseException($"git rev-list --count reported something that is not a count: '{trimmed}'.");
	}

	/// <summary>
	/// Parses the tab-separated pair <c>rev-list --count --left-right</c> prints.
	/// </summary>
	/// <remarks>
	/// The left number counts commits only the left side of the <c>a...b</c> expression has, and the
	/// right number those only the right side has. This library always builds that expression as
	/// <c>upstream...local</c>, so left is <see cref="GitDivergence.Behind"/> and right is
	/// <see cref="GitDivergence.Ahead"/> — the mapping is fixed here rather than left to a caller
	/// precisely because it is easy to get backwards. Verified against git 2.43, which separates the
	/// two with a single tab.
	/// </remarks>
	/// <param name="output">Everything git wrote to standard output.</param>
	/// <returns>The divergence.</returns>
	/// <exception cref="GitParseException">The output was not two tab-separated whole numbers.</exception>
	internal static GitDivergence ParseDivergence(string output)
	{
		Ensure.NotNull(output);

		string trimmed = output.Trim();
		string[] fields = trimmed.Split('\t');

		if (fields.Length == 2 &&
			int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int leftOnly) &&
			int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int rightOnly))
		{
			return new GitDivergence(Ahead: rightOnly, Behind: leftOnly);
		}

		throw new GitParseException(
			$"git rev-list --count --left-right reported something that is not a pair of counts: '{trimmed}'.");
	}
}
