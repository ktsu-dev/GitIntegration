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
	/// Chosen because git forbids it in a reference name and no filesystem permits it in a path,
	/// so it can never appear inside a field and be mistaken for a separator.
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
}
