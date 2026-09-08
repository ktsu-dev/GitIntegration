// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using ktsu.Semantics.Paths;

/// <summary>
/// One submodule of a repository.
/// </summary>
/// <remarks>
/// A submodule is recorded in the superproject as a <em>gitlink</em>: a tree entry with mode
/// <c>160000</c> naming a path and a commit id, rather than a blob. What is actually checked out at
/// that path is a separate question, and the two can disagree — which is what
/// <see cref="State"/> and the difference between <see cref="Sha"/> and <see cref="CheckedOutSha"/>
/// describe.
/// </remarks>
public sealed record GitSubmodule
{
	/// <summary>Gets the submodule's path, relative to the superproject's root.</summary>
	public required RelativeDirectoryPath Path { get; init; }

	/// <summary>Gets the commit the superproject records for this submodule.</summary>
	/// <remarks>
	/// The gitlink itself, read from the superproject's index. This is what a
	/// <c>submodule update</c> would check out, and it does not change when someone commits inside
	/// the submodule's working directory — <see cref="CheckedOutSha"/> is what moves then.
	/// </remarks>
	public required GitCommitSha Sha { get; init; }

	/// <summary>
	/// Gets the commit actually checked out in the submodule's working directory, or
	/// <see langword="null"/> when nothing is checked out.
	/// </summary>
	/// <remarks>
	/// Equal to <see cref="Sha"/> when <see cref="State"/> is
	/// <see cref="GitSubmoduleState.InSync"/>, and different when it is
	/// <see cref="GitSubmoduleState.DifferentCommit"/>. <see langword="null"/> for an uninitialised
	/// submodule, whose working directory holds no checkout to report — git prints the recorded
	/// gitlink again in that case, which would otherwise make an uninitialised submodule look
	/// indistinguishable from a synchronised one.
	/// </remarks>
	public GitCommitSha? CheckedOutSha { get; init; }

	/// <summary>Gets how the working directory relates to the recorded commit.</summary>
	public GitSubmoduleState State { get; init; }

	/// <summary>
	/// Gets the reference expression git reports alongside the commit, such as <c>heads/main</c> or
	/// a tag description, or <see langword="null"/> when git reported none.
	/// </summary>
	/// <remarks>
	/// Git's own <c>git describe</c>-style suffix, purely descriptive. It is absent for an
	/// uninitialised submodule, since there is nothing checked out to describe.
	/// </remarks>
	public string? Describe { get; init; }
}
