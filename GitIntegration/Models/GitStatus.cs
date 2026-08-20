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
	public int Ahead { get; init; }

	/// <summary>Gets how many commits the upstream has that the current branch does not.</summary>
	public int Behind { get; init; }

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
}
