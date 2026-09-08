// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

/// <summary>
/// The machine-readable output formats this library asks git for.
/// </summary>
/// <remarks>
/// Shared between the builder that requests a format and the parser that reads it, so the two
/// cannot drift apart. A builder test asserts the exact string reaches the argument vector, and a
/// parser test asserts the shape it produces, which together pin the format from both ends.
/// </remarks>
internal static class GitOutputFormats
{
	/// <summary>
	/// The ASCII unit separator, used between fields within one record.
	/// </summary>
	/// <remarks>
	/// Chosen because git forbids it in a reference name and no filesystem permits it in a path, so
	/// it can never appear inside those fields and be mistaken for a separator. A commit message,
	/// however, is free-form text and legally can contain it. <see cref="GitLogParser"/> bounds its
	/// split of the log format's fields so the trailing body field absorbs any embedded separator
	/// rather than the field count shifting.
	/// </remarks>
	internal const char UnitSeparator = '\u001f';

	/// <summary>
	/// The <c>log</c> format: sha, tree, parents, author name/email/date, committer
	/// name/email/date, subject, body.
	/// </summary>
	/// <remarks>
	/// Used with <c>-z</c>, which NUL-terminates each commit, so a multi-line body cannot be
	/// mistaken for the start of a new record. <c>%x1f</c> is git's escape for a literal byte.
	/// </remarks>
	internal const string LogFormat = "%H%x1f%T%x1f%P%x1f%an%x1f%ae%x1f%aI%x1f%cn%x1f%ce%x1f%cI%x1f%s%x1f%b";

	/// <summary>
	/// The <c>for-each-ref</c> format: full reference name, short name, object id, upstream, and
	/// the current-branch marker.
	/// </summary>
	/// <remarks>
	/// The full reference name leads, and is the reason this format differs from the one sketched
	/// in the design document. A short name cannot say whether a branch is local or
	/// remote-tracking — a local branch may be called <c>origin/main</c> — and
	/// <c>refs/remotes/origin/HEAD</c>, present in every clone, shortens to the bare remote name
	/// and would otherwise be reported as a branch called <c>origin</c>. <c>%1f</c> is
	/// <c>for-each-ref</c>'s own hex escape, which differs in spelling from <c>log</c>'s
	/// <c>%x1f</c> but means the same byte.
	/// </remarks>
	internal const string ForEachRefFormat =
		"%(refname)%1f%(refname:short)%1f%(objectname)%1f%(upstream:short)%1f%(HEAD)";

	/// <summary>
	/// The <c>for-each-ref</c> format for tags: short name, the reference's own object id, the
	/// object type, the dereferenced object id, and the message subject.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A separate format from <see cref="ForEachRefFormat"/> rather than a widening of it, because
	/// the two share only the name. A tag has no upstream and no current-branch marker, and a branch
	/// has no dereferenced target — a single format carrying both sets would leave half its fields
	/// empty on every record and invite a parser to guess which half it was reading.
	/// </para>
	/// <para>
	/// <c>%(objecttype)</c> is what distinguishes the two kinds of tag: <c>tag</c> for an annotated
	/// tag, whose reference points at a tag object, and <c>commit</c> for a lightweight one, whose
	/// reference points straight at the commit. <c>%(*objectname)</c> dereferences a tag object to
	/// the commit beneath it and is empty for a lightweight tag, so the commit id is the starred
	/// field when it is populated and the plain one otherwise.
	/// </para>
	/// <para>
	/// <c>%(contents:subject)</c> is the trap in this format. For an annotated tag it is the tag
	/// message's subject, which is what it looks like it means. For a lightweight tag there is no tag
	/// object to read, and git falls through to the <em>commit's</em> subject instead of leaving the
	/// field empty — so a parser that reported it verbatim would present a commit message as the
	/// tagger's words. <see cref="GitTagParser"/> gates it on the object type for that reason.
	/// Verified against git 2.43.
	/// </para>
	/// </remarks>
	internal const string ForEachTagFormat =
		"%(refname:short)%1f%(objectname)%1f%(objecttype)%1f%(*objectname)%1f%(contents:subject)";
}
