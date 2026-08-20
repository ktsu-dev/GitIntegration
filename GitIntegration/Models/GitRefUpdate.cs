// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

/// <summary>
/// What happened to one reference during a fetch or a push.
/// </summary>
/// <remarks>
/// The flag characters git uses are shared between the two verbs but not identical in meaning:
/// <c>-</c> is a deletion when pushing and a prune when fetching, and <c>t</c> appears only when
/// fetching. The names here are neutral between the two.
/// </remarks>
public enum GitRefUpdateKind
{
	/// <summary>The reference moved forward without rewriting history. Git's flag is a space.</summary>
	FastForward,

	/// <summary>The reference was moved in a way that discarded commits. Git's flag is <c>+</c>.</summary>
	Forced,

	/// <summary>The reference was deleted on push, or pruned on fetch. Git's flag is <c>-</c>.</summary>
	Removed,

	/// <summary>The reference did not exist before. Git's flag is <c>*</c>.</summary>
	Created,

	/// <summary>Git refused the update. Its flag is <c>!</c>.</summary>
	Rejected,

	/// <summary>There was nothing to do. Git's flag is <c>=</c>.</summary>
	UpToDate,

	/// <summary>A tag was updated. Git's flag is <c>t</c>, and only fetch emits it.</summary>
	TagUpdate,

	/// <summary>Git used a flag this library does not recognise.</summary>
	Unknown,
}

/// <summary>
/// One reference changed by a fetch or a push.
/// </summary>
public sealed record GitRefUpdate
{
	/// <summary>Gets what happened to the reference.</summary>
	public required GitRefUpdateKind Kind { get; init; }

	/// <summary>
	/// Gets the reference that was updated: the remote reference when pushing, the local
	/// remote-tracking reference when fetching.
	/// </summary>
	public required GitRefName Reference { get; init; }

	/// <summary>
	/// Gets the local reference that was pushed, or <see langword="null"/>.
	/// </summary>
	/// <remarks>
	/// Populated only by push, and null there too for a deletion — git writes an empty local side,
	/// as in <c>:refs/heads/gone</c>, because nothing is being sent.
	/// </remarks>
	public GitRefName? Source { get; init; }

	/// <summary>Gets the object id the reference pointed at before, when git reported one.</summary>
	/// <remarks>
	/// Fetch reports full object ids directly. Push reports them only inside
	/// <see cref="Summary"/> as an abbreviated range such as <c>66afe49..7c85857</c>; those are
	/// parsed out where present, so they may be shorter than a full identifier.
	/// </remarks>
	public GitCommitSha? OldSha { get; init; }

	/// <summary>Gets the object id the reference points at now, when git reported one.</summary>
	public GitCommitSha? NewSha { get; init; }

	/// <summary>
	/// Gets git's own summary text, verbatim — <c>[new branch]</c>, <c>[up to date]</c>,
	/// <c>[rejected] (fetch first)</c>, or a commit range. Empty for fetch, whose porcelain format
	/// carries no summary field.
	/// </summary>
	public string Summary { get; init; } = string.Empty;

	/// <summary>Gets a value indicating whether git refused this update.</summary>
	public bool IsRejected => Kind == GitRefUpdateKind.Rejected;
}
