// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

/// <summary>
/// One branch reference.
/// </summary>
public sealed record GitBranch
{
	/// <summary>
	/// Gets the short name, such as <c>main</c> for a local branch or <c>origin/main</c> for a
	/// remote-tracking one.
	/// </summary>
	public required GitBranchName Name { get; init; }

	/// <summary>Gets the object id the branch points at.</summary>
	public required GitCommitSha Sha { get; init; }

	/// <summary>Gets the upstream this branch tracks, or <see langword="null"/> when it tracks none.</summary>
	public GitBranchName? Upstream { get; init; }

	/// <summary>Gets a value indicating whether this is the checked-out branch.</summary>
	public bool IsCurrent { get; init; }

	/// <summary>
	/// Gets a value indicating whether this is a remote-tracking branch under <c>refs/remotes</c>.
	/// </summary>
	/// <remarks>
	/// Determined from the full reference name, not from the short name: a local branch may
	/// legitimately be called <c>origin/main</c>, which is indistinguishable from a remote-tracking
	/// branch once shortened.
	/// </remarks>
	public bool IsRemote { get; init; }

	/// <summary>
	/// Gets a value indicating whether this branch is configured to track an
	/// <see cref="Upstream"/> that does not exist, because the remote branch was deleted or never
	/// fetched.
	/// </summary>
	/// <remarks>
	/// The same state as <see cref="GitStatus.IsUpstreamGone"/>, which covers only the checked-out
	/// branch, and the state <c>git branch -vv</c> shows as <c>[gone]</c>. Always
	/// <see langword="false"/> for a branch with no upstream and for a remote-tracking branch.
	/// </remarks>
	public bool IsUpstreamGone { get; init; }
}
