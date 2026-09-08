// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

/// <summary>
/// The state of one file on one side of the index, as reported by <c>status</c>.
/// </summary>
public enum GitFileState
{
	/// <summary>The file is unchanged on this side.</summary>
	Unmodified,

	/// <summary>The file's contents changed.</summary>
	Modified,

	/// <summary>The file is newly tracked.</summary>
	Added,

	/// <summary>The file was removed.</summary>
	Deleted,

	/// <summary>The file was moved from another path.</summary>
	Renamed,

	/// <summary>The file was copied from another path.</summary>
	Copied,

	/// <summary>The file is not tracked and is not ignored.</summary>
	Untracked,

	/// <summary>The file matches an ignore rule.</summary>
	Ignored,

	/// <summary>The file has conflicting changes from an unfinished merge.</summary>
	Unmerged,

	/// <summary>The file changed kind, for instance from a regular file to a symbolic link.</summary>
	TypeChanged,
}

/// <summary>
/// The kind of change <c>diff --name-status</c> reported for one path.
/// </summary>
public enum GitChangeKind
{
	/// <summary>The path was added.</summary>
	Added,

	/// <summary>The path was copied from another path.</summary>
	Copied,

	/// <summary>The path was deleted.</summary>
	Deleted,

	/// <summary>The path's contents changed.</summary>
	Modified,

	/// <summary>The path was moved from another path.</summary>
	Renamed,

	/// <summary>The path changed kind, for instance from a regular file to a symbolic link.</summary>
	TypeChanged,

	/// <summary>The path has conflicting changes from an unfinished merge.</summary>
	Unmerged,

	/// <summary>Git reported a status letter this library does not recognise.</summary>
	Unknown,
}

/// <summary>
/// How much untracked detail <c>status</c> should report.
/// </summary>
public enum GitUntrackedFilesMode
{
	/// <summary>Report no untracked files at all.</summary>
	No,

	/// <summary>Report untracked files, collapsing a wholly untracked directory to one entry.</summary>
	Normal,

	/// <summary>Report every untracked file individually, including inside untracked directories.</summary>
	All,
}

/// <summary>
/// What state a submodule's working directory is in, relative to the commit the superproject
/// records for it.
/// </summary>
/// <remarks>
/// The four states <c>git submodule status</c> distinguishes with its leading marker character.
/// </remarks>
public enum GitSubmoduleState
{
	/// <summary>
	/// Git reported a marker this library does not recognise.
	/// </summary>
	/// <remarks>
	/// Also the state of a gitlink the superproject records but that <c>submodule status</c> did not
	/// report at all — a submodule removed from <c>.gitmodules</c> while its gitlink remains, for
	/// instance. Reporting the entry with an unknown state beats dropping it, since a caller
	/// deciding whether a directory is safe to remove needs to know the gitlink is still there.
	/// </remarks>
	Unknown,

	/// <summary>The checked-out commit is the one the superproject records.</summary>
	InSync,

	/// <summary>
	/// A different commit is checked out than the one the superproject records.
	/// </summary>
	/// <remarks>Git's <c>+</c> marker. The superproject's gitlink and the working directory disagree.</remarks>
	DifferentCommit,

	/// <summary>
	/// The submodule is registered but not initialised, so its working directory holds no checkout.
	/// </summary>
	/// <remarks>Git's <c>-</c> marker.</remarks>
	Uninitialised,

	/// <summary>The submodule has conflicting changes from an unfinished merge.</summary>
	/// <remarks>Git's <c>U</c> marker.</remarks>
	Conflicted,
}

/// <summary>
/// Whether a verb should also operate on the working trees and refs of the repository's submodules.
/// </summary>
/// <remarks>
/// The values <c>fetch</c> and <c>pull</c> accept for <c>--recurse-submodules</c>. Distinct from
/// <see cref="GitSubmodulePushCheck"/>, which spells a different set for a different question — see
/// that type's remarks.
/// </remarks>
public enum GitSubmoduleRecursion
{
	/// <summary>Do not touch submodules at all.</summary>
	No,

	/// <summary>Always recurse into every submodule.</summary>
	Yes,

	/// <summary>
	/// Recurse only into a submodule whose recorded commit the superproject's own update changed.
	/// </summary>
	OnDemand,
}

/// <summary>
/// Whether <c>push</c> should verify or push the submodules' own commits before pushing the
/// superproject.
/// </summary>
/// <remarks>
/// <c>push --recurse-submodules</c> spells the same flag name as <c>fetch</c> and <c>pull</c> but
/// means something else entirely, which is why it has its own type and its own method name. The
/// other verbs' flag asks "also operate on the submodules' working trees or refs". This one asks
/// what to do about the fact that a superproject commit referencing a submodule commit no remote
/// has is a commit nobody else can use — so it governs whether git checks for, or pushes, the
/// submodules' <em>own</em> commits to their <em>own</em> remotes first.
/// </remarks>
public enum GitSubmodulePushCheck
{
	/// <summary>Push the superproject without considering the submodules at all.</summary>
	No,

	/// <summary>
	/// Refuse the push when any submodule commit the superproject references is missing from that
	/// submodule's remote.
	/// </summary>
	Check,

	/// <summary>Push any such missing submodule commit first, then push the superproject.</summary>
	OnDemand,

	/// <summary>Push the submodules' commits and stop, leaving the superproject unpushed.</summary>
	Only,
}
