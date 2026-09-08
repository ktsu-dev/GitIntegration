// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

/// <summary>
/// One tag reference.
/// </summary>
/// <remarks>
/// Git has two kinds of tag and this record describes both. A lightweight tag is a reference
/// pointing straight at a commit, exactly like a branch that never moves. An annotated tag is a
/// reference pointing at a <em>tag object</em>, which is a real object in the database carrying its
/// own author, date, and message, and which in turn points at the commit. <see cref="Sha"/> and
/// <see cref="ObjectSha"/> are what tell the two apart by value; <see cref="IsAnnotated"/> says it
/// directly.
/// </remarks>
public sealed record GitTag
{
	/// <summary>Gets the short name, such as <c>v1.2.3</c>, without the <c>refs/tags/</c> prefix.</summary>
	public required GitTagName Name { get; init; }

	/// <summary>Gets the object id of the commit this tag ultimately names.</summary>
	/// <remarks>
	/// The commit in both cases, which is almost always what a caller wants. For an annotated tag
	/// this is the dereferenced target rather than the tag object's own id, so
	/// <c>Tags()</c> and <c>Branches()</c> report the same kind of value in the same field and a
	/// caller comparing a tag against a branch is comparing like with like.
	/// </remarks>
	public required GitCommitSha Sha { get; init; }

	/// <summary>Gets the object id the reference itself points at.</summary>
	/// <remarks>
	/// Equal to <see cref="Sha"/> for a lightweight tag, and the tag object's own id for an
	/// annotated one. Carried because it is the only way to address the tag object itself — to read
	/// its tagger or its signature, say — and because deriving it later would need a second
	/// invocation.
	/// </remarks>
	public required GitCommitSha ObjectSha { get; init; }

	/// <summary>Gets a value indicating whether this tag is annotated rather than lightweight.</summary>
	public bool IsAnnotated { get; init; }

	/// <summary>
	/// Gets the annotated tag's message subject, or <see langword="null"/> for a lightweight tag.
	/// </summary>
	/// <remarks>
	/// Null rather than the empty string for a lightweight tag, and that distinction is load-bearing.
	/// Git's <c>%(contents:subject)</c> falls through to the <em>commit's</em> subject when the
	/// reference names no tag object, so reporting the field verbatim would present a commit message
	/// as though the tagger had written it. The parser gates this on the object type for exactly that
	/// reason.
	/// </remarks>
	public string? Message { get; init; }
}
