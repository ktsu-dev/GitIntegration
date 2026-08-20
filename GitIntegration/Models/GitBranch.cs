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
}
