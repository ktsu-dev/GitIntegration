// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;

/// <summary>
/// One pull request, as reported by a hosting provider.
/// </summary>
/// <remarks>
/// A hosting provider fills in what its API returned and leaves the rest unset. A <see
/// langword="null"/> optional field means the host did not report that value — it is distinct
/// from a field the host reported as genuinely empty, so a caller can tell "not known" from
/// "known to be nothing" instead of the two being silently indistinguishable.
/// </remarks>
public sealed record GitPullRequest
{
	/// <summary>Gets the pull request's host-assigned number.</summary>
	public required GitPullRequestNumber Number { get; init; }

	/// <summary>Gets the pull request's title.</summary>
	public required GitPullRequestTitle Title { get; init; }

	/// <summary>Gets the pull request's description, or <see langword="null"/> when not known.</summary>
	public string? Description { get; init; }

	/// <summary>Gets the branch the pull request merges from.</summary>
	public required GitBranchName SourceBranch { get; init; }

	/// <summary>Gets the branch the pull request merges into.</summary>
	public required GitBranchName TargetBranch { get; init; }

	/// <summary>
	/// Gets the host's identifier for the account that opened the pull request, or
	/// <see langword="null"/> when not known.
	/// </summary>
	public GitPullRequestAuthor? Author { get; init; }

	/// <summary>Gets the pull request's state.</summary>
	public required GitPullRequestState State { get; init; }

	/// <summary>Gets a value indicating whether the pull request is a draft.</summary>
	public bool IsDraft { get; init; }

	/// <summary>
	/// Gets the browser address of the pull request, or <see langword="null"/> when not known.
	/// </summary>
	public GitPullRequestWebURI? WebURI { get; init; }

	/// <summary>
	/// Gets when the pull request was created, or <see langword="null"/> when not known.
	/// </summary>
	public DateTimeOffset? CreatedAt { get; init; }
}

/// <summary>
/// A pull request's state.
/// </summary>
public enum GitPullRequestState
{
	/// <summary>The pull request is open and unresolved.</summary>
	Open,

	/// <summary>The pull request was merged.</summary>
	Merged,

	/// <summary>The pull request was closed without merging.</summary>
	Closed,
}
