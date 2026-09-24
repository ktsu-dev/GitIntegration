// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using ktsu.Semantics.Paths;

/// <summary>
/// One line of a hunk, as parsed from the patch text.
/// </summary>
public sealed record GitPatchLine
{
	/// <summary>Gets what this line does.</summary>
	public required GitPatchLineKind Kind { get; init; }

	/// <summary>Gets the line's text, without its leading marker character.</summary>
	public required string Text { get; init; }

	/// <summary>
	/// Gets this line's number on the old side of the hunk, or <see langword="null"/> for a line
	/// present only on the new side.
	/// </summary>
	public int? OldNumber { get; init; }

	/// <summary>
	/// Gets this line's number on the new side of the hunk, or <see langword="null"/> for a line
	/// present only on the old side.
	/// </summary>
	public int? NewNumber { get; init; }
}

/// <summary>
/// One contiguous range of changed lines within a file's patch, together with the surrounding
/// context git included.
/// </summary>
public sealed record GitHunk
{
	/// <summary>Gets the first line number this hunk covers on the old side.</summary>
	public required int OldStart { get; init; }

	/// <summary>Gets how many lines this hunk covers on the old side.</summary>
	public required int OldCount { get; init; }

	/// <summary>Gets the first line number this hunk covers on the new side.</summary>
	public required int NewStart { get; init; }

	/// <summary>Gets how many lines this hunk covers on the new side.</summary>
	public required int NewCount { get; init; }

	/// <summary>
	/// Gets the function or section heading git found for this hunk, or the empty string when it
	/// found none.
	/// </summary>
	public required string Heading { get; init; }

	/// <summary>Gets the hunk's lines, parsed from <see cref="Text"/>.</summary>
	public required IReadOnlyList<GitPatchLine> Lines { get; init; }

	/// <summary>
	/// Gets the hunk's own text, from its <c>@@</c> line to its last line, exactly as git printed
	/// it.
	/// </summary>
	/// <remarks>
	/// Kept verbatim rather than regenerated from <see cref="Lines"/>, because a detail such as the
	/// no-newline-at-end-of-file marker has no home in the parsed lines and would otherwise be lost,
	/// leaving <c>git apply</c> to reject the reassembled hunk.
	/// <para>
	/// Verbatim covers the bytes git wrote, not the encoding it wrote them in. Git's output is
	/// decoded as UTF-8, so a repository whose files hold Latin-1 or Shift-JIS text, which git
	/// diffs as text because it decides binary on NUL bytes rather than on encoding validity, loses
	/// every invalid byte to U+FFFD here. <see cref="GitRepository.Apply"/> refuses patch text
	/// carrying that character rather than staging the replacement bytes.
	/// </para>
	/// </remarks>
	public required string Text { get; init; }
}

/// <summary>
/// One file's patch: git's own header for the file, plus every hunk git found within it.
/// </summary>
public sealed record GitFilePatch
{
	/// <summary>Gets the path as it exists after the change, relative to the repository root.</summary>
	public required RelativeFilePath Path { get; init; }

	/// <summary>
	/// Gets the path this file came from for a rename, or <see langword="null"/> otherwise.
	/// </summary>
	/// <remarks>
	/// A copy is not reported here. Git writes <c>copy from</c> and <c>copy to</c> only under
	/// <c>--find-copies</c>, which <see cref="IGitPatchBuilder"/> never asks for.
	/// </remarks>
	public RelativeFilePath? OriginalPath { get; init; }

	/// <summary>Gets what happened to the path.</summary>
	/// <remarks>
	/// <see cref="GitChangeKind.TypeChanged"/> never appears, because git does not report a type
	/// change as one file. A regular file becoming a symbolic link comes back as two entries for
	/// the same path, one <see cref="GitChangeKind.Deleted"/> and one <see cref="GitChangeKind.Added"/>,
	/// where <c>diff --name-status</c> reports a single <c>T</c>.
	/// </remarks>
	public required GitChangeKind Kind { get; init; }

	/// <summary>Gets whether git treated this file as binary rather than diffing its lines.</summary>
	/// <remarks><see cref="Hunks"/> is empty when this is <see langword="true"/>.</remarks>
	public required bool IsBinary { get; init; }

	/// <summary>Gets whether this file has conflicting changes from an unfinished merge.</summary>
	/// <remarks>
	/// <see cref="Hunks"/> is empty when this is <see langword="true"/>. Git prints an unmerged path
	/// in the combined format, which <c>git apply</c> does not accept, so its body is not parsed
	/// into hunks that could not be staged anyway.
	/// </remarks>
	public required bool IsConflicted { get; init; }

	/// <summary>
	/// Gets the patch header, from the <c>diff --git</c> line up to but not including the first
	/// hunk.
	/// </summary>
	public required string Header { get; init; }

	/// <summary>Gets the file's hunks, in the order git printed them.</summary>
	public required IReadOnlyList<GitHunk> Hunks { get; init; }

	/// <summary>
	/// Assembles the header and the given hunks into text <c>git apply</c> accepts.
	/// </summary>
	/// <remarks>
	/// Hunks are emitted in the order this file holds them rather than the order they were given,
	/// because git reads a patch top to bottom and rejects one whose hunks run backwards.
	/// <para>
	/// A hunk this file does not hold is refused rather than dropped. Only the hunks in
	/// <see cref="Hunks"/> can be written under this file's header, so a caller passing one from
	/// another file would otherwise get a shorter patch than it asked for, or a header with no body
	/// at all, and neither outcome says which hunk went missing.
	/// </para>
	/// </remarks>
	/// <param name="hunks">The hunks to include.</param>
	/// <returns>The patch text.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="hunks"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="hunks"/> is empty, or holds a hunk this file does not.
	/// </exception>
	public string PatchFor(IEnumerable<GitHunk> hunks)
	{
		Ensure.NotNull(hunks);

		HashSet<GitHunk> wanted = [.. hunks];

		if (wanted.Count == 0)
		{
			throw new ArgumentException(
				"A patch needs at least one hunk. A header with no body is rejected as corrupt, and "
				+ "git's message for it explains nothing.",
				nameof(hunks));
		}

		StringBuilder builder = new(Header);
		int emitted = 0;

		foreach (GitHunk hunk in Hunks.Where(wanted.Contains))
		{
			_ = builder.Append(hunk.Text);
			emitted++;
		}

		return emitted < wanted.Count
			? throw new ArgumentException(
				$"{wanted.Count - emitted} of the {wanted.Count} hunks given do not belong to "
				+ $"'{Path.WeakString}'. Only this file's own hunks can be written under its header, "
				+ "so the rest would be dropped without a word.",
				nameof(hunks))
			: builder.ToString();
	}
}

/// <summary>
/// The full patch for a set of files, as <c>diff</c> or <c>show</c> reported it.
/// </summary>
public sealed record GitPatch
{
	/// <summary>Gets the patch for each changed file.</summary>
	public required IReadOnlyList<GitFilePatch> Files { get; init; }
}
