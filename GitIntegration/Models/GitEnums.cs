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
