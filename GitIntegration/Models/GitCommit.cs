// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;

/// <summary>
/// One commit.
/// </summary>
public sealed record GitCommit
{
	/// <summary>Gets the commit's own object id.</summary>
	public required GitCommitSha Sha { get; init; }

	/// <summary>Gets the object id of the tree this commit points at.</summary>
	public required GitCommitSha TreeSha { get; init; }

	/// <summary>
	/// Gets the parents, in git's own order: empty for a root commit, one for an ordinary commit,
	/// and two or more for a merge.
	/// </summary>
	public required IReadOnlyList<GitCommitSha> ParentShas { get; init; }

	/// <summary>Gets who wrote the change, and when.</summary>
	public required GitSignature Author { get; init; }

	/// <summary>Gets who committed the change, and when. Differs from the author after a rebase or a cherry-pick.</summary>
	public required GitSignature Committer { get; init; }

	/// <summary>Gets the first line of the commit message.</summary>
	public required string Subject { get; init; }

	/// <summary>Gets the remainder of the commit message, empty when there is none.</summary>
	public string Body { get; init; } = string.Empty;
}

/// <summary>
/// A name, an address, and a timestamp, as recorded on a commit.
/// </summary>
public sealed record GitSignature
{
	/// <summary>Gets the recorded name.</summary>
	public required GitAuthorName Name { get; init; }

	/// <summary>Gets the recorded email address.</summary>
	public required GitAuthorEmail Email { get; init; }

	/// <summary>
	/// Gets the recorded time, with the offset the commit was made in.
	/// </summary>
	/// <remarks>
	/// Parsed from git's strict ISO-8601 output, so the original offset is preserved rather than
	/// normalised to UTC — the local time a commit was made in is information a caller may want.
	/// </remarks>
	public required DateTimeOffset Timestamp { get; init; }
}
