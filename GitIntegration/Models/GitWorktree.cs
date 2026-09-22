// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using ktsu.Semantics.Paths;

/// <summary>
/// One working tree of a repository: where it is, and what it has checked out.
/// </summary>
/// <remarks>
/// Inert data, like <see cref="GitBranch"/> and <see cref="GitSubmodule"/>. It carries no
/// <see cref="GitRepository"/> and no process runner. A caller that wants to run a verb inside a
/// worktree passes <see cref="Path"/> to <see cref="IGitClient.OpenAsync"/>, which is the same
/// journey it would make from any other path.
/// </remarks>
public sealed record GitWorktree
{
	/// <summary>Gets the worktree's own directory.</summary>
	public required AbsoluteDirectoryPath Path { get; init; }

	/// <summary>
	/// Gets the commit checked out here, or <see langword="null"/> when git reported none.
	/// </summary>
	/// <remarks>
	/// Absent for a bare worktree, which has no checkout to report. Present for a detached one,
	/// where it is the only thing identifying what is checked out.
	/// </remarks>
	public GitCommitSha? Head { get; init; }

	/// <summary>
	/// Gets the branch checked out here, or <see langword="null"/> when there is none.
	/// </summary>
	/// <remarks>
	/// Absent for a bare or detached worktree. Reported as a bare name with git's
	/// <c>refs/heads/</c> prefix stripped, so that a branch name from this half of the library has
	/// the same shape as one from the hosting half.
	/// </remarks>
	public GitBranchName? Branch { get; init; }

	/// <summary>
	/// Gets a value indicating whether this is the repository's main working tree.
	/// </summary>
	/// <remarks>
	/// <b>Positional.</b> Git's porcelain has no attribute for this: the main working tree is simply
	/// the first record git emits, always. The parser sets this on the first record and on no other.
	/// It exists because a caller managing worktrees needs to refuse to remove the one that owns the
	/// repository, and answering that once here beats every caller reimplementing it.
	/// </remarks>
	public bool IsMain { get; init; }

	/// <summary>Gets a value indicating whether this worktree has no working directory.</summary>
	public bool IsBare { get; init; }

	/// <summary>Gets a value indicating whether this worktree has a commit checked out rather than a branch.</summary>
	public bool IsDetached { get; init; }

	/// <summary>Gets a value indicating whether this worktree is locked against pruning.</summary>
	public bool IsLocked { get; init; }

	/// <summary>
	/// Gets the reason this worktree is locked, or <see langword="null"/> when git recorded none.
	/// </summary>
	/// <remarks>
	/// Separately nullable from <see cref="IsLocked"/>, because git emits <c>locked</c> both alone
	/// and with a reason, and "locked for a reason nobody recorded" is a different fact from "not
	/// locked".
	/// </remarks>
	public string? LockReason { get; init; }

	/// <summary>Gets a value indicating whether git considers this worktree's record removable.</summary>
	public bool IsPrunable { get; init; }

	/// <summary>
	/// Gets the reason this worktree is prunable, or <see langword="null"/> when git recorded none.
	/// </summary>
	public string? PrunableReason { get; init; }
}
