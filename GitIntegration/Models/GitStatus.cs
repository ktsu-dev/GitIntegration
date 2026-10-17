// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// The working tree and index state of a repository.
/// </summary>
public sealed record GitStatus
{
	/// <summary>
	/// Gets the checked-out branch, or <see langword="null"/> when HEAD is detached.
	/// </summary>
	public GitBranchName? Branch { get; init; }

	/// <summary>
	/// Gets the upstream the current branch tracks, or <see langword="null"/> when it tracks none.
	/// </summary>
	public GitBranchName? Upstream { get; init; }

	/// <summary>Gets how many commits the current branch has that its upstream does not.</summary>
	/// <remarks>
	/// Zero while <see cref="IsUpstreamGone"/> is <see langword="true"/>, which does not mean the
	/// branch is in sync: git cannot compare against an upstream that does not exist.
	/// </remarks>
	public int Ahead { get; init; }

	/// <summary>Gets how many commits the upstream has that the current branch does not.</summary>
	/// <remarks>
	/// Zero while <see cref="IsUpstreamGone"/> is <see langword="true"/>, for the same reason as
	/// <see cref="Ahead"/>.
	/// </remarks>
	public int Behind { get; init; }

	/// <summary>
	/// Gets a value indicating whether the current branch is configured to track an
	/// <see cref="Upstream"/> that does not exist, because the remote branch was deleted or never
	/// fetched.
	/// </summary>
	/// <remarks>
	/// This is the state <c>git status -sb</c> shows as <c>[gone]</c>. While it is
	/// <see langword="true"/>, <see cref="Ahead"/> and <see cref="Behind"/> are both zero and say
	/// nothing about whether the branch has unpushed commits.
	/// </remarks>
	public bool IsUpstreamGone { get; init; }

	/// <summary>Gets a value indicating whether HEAD points at a commit rather than a branch.</summary>
	public bool IsDetached { get; init; }

	/// <summary>Gets every path git reported as differing from a clean checkout.</summary>
	public required IReadOnlyList<GitStatusEntry> Entries { get; init; }

	/// <summary>
	/// Gets a value indicating whether git reported no differing paths.
	/// </summary>
	/// <remarks>
	/// This reflects what the invocation asked for. A status built with
	/// <c>--untracked-files=no</c> reports clean while untracked files exist, because it was told
	/// not to look for them.
	/// </remarks>
	public bool IsClean => Entries.Count == 0;
}

/// <summary>
/// One path git reported as differing from a clean checkout.
/// </summary>
public sealed record GitStatusEntry
{
	/// <summary>Gets the difference between HEAD and the index.</summary>
	public required GitFileState IndexState { get; init; }

	/// <summary>Gets the difference between the index and the working tree.</summary>
	public required GitFileState WorkTreeState { get; init; }

	/// <summary>Gets the path, relative to the repository root.</summary>
	public required RelativeFilePath Path { get; init; }

	/// <summary>
	/// Gets the path this file came from for a rename or a copy, or <see langword="null"/>
	/// otherwise.
	/// </summary>
	public RelativeFilePath? OriginalPath { get; init; }

	/// <summary>
	/// Gets a value indicating whether <see cref="Path"/> names a directory rather than a file.
	/// </summary>
	/// <remarks>
	/// Only an untracked or ignored entry can be a directory. Under
	/// <see cref="GitUntrackedFilesMode.Normal"/>, git collapses a directory holding no tracked
	/// files, or an untracked nested repository, to one record for the directory itself, and marks it
	/// with a trailing slash that <see cref="Path"/> cannot carry. Without this flag that record reads
	/// exactly like an untracked file of the same name.
	/// </remarks>
	public bool IsDirectory { get; init; }
}
