// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using ktsu.Semantics.Paths;

/// <summary>
/// Reads a repository's submodules from two invocations: <c>git ls-files --stage -z</c> for the
/// recorded gitlinks, and <c>git submodule status</c> for what is actually checked out.
/// </summary>
/// <remarks>
/// <para>
/// The split exists because neither command answers the whole question. <c>ls-files</c> is plumbing:
/// it is NUL-terminated, so a path may contain anything at all, and its mode field states outright
/// which entries are gitlinks. But it reports only what the superproject <em>records</em> — it
/// cannot say whether a submodule is initialized, or whether a different commit is checked out.
/// <c>submodule status</c> answers exactly that, but it is a shell wrapper rather than plumbing, so
/// its output carries no stability guarantee of the kind <c>for-each-ref</c> and
/// <c>status --porcelain=v2</c> do, and — worse — it has <b>no <c>-z</c> form at all</b>.
/// </para>
/// <para>
/// That missing <c>-z</c> is the trap. A <c>submodule status</c> line is a marker character, an
/// object id, a space, the path, and then an <em>optional</em> trailing <c>" (describe)"</c>. The
/// path is therefore not the last field, and a path containing <c>" ("</c> is genuinely ambiguous
/// with the describe suffix — there is no way to tell, from the line alone, where one ends and the
/// other begins.
/// </para>
/// <para>
/// This parser closes that ambiguity rather than documenting it, by never asking the question. The
/// set of paths is already known exactly, from <c>ls-files</c>, before a status line is read: so
/// instead of splitting a line to discover its path, each line's remainder is matched against the
/// paths already in hand. A path containing <c>" ("</c> matches itself and the describe suffix falls
/// out as whatever follows. The wrapper is consulted only for the marker and the checked-out object
/// id, which are unambiguous wherever the line came from.
/// </para>
/// </remarks>
internal static class GitSubmoduleParser
{
	/// <summary>The tree entry mode git uses for a gitlink, as opposed to a blob or a tree.</summary>
	private const string GitlinkMode = "160000";

	/// <summary>
	/// Reads the recorded gitlinks from NUL-terminated <c>ls-files --stage</c> output.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Each record is <c>&lt;mode&gt; &lt;object&gt; &lt;stage&gt;\t&lt;path&gt;</c>. Everything that
	/// is not a gitlink is skipped, which is most of a repository: this command lists the whole index,
	/// and the mode filter is what turns that into a submodule listing.
	/// </para>
	/// <para>
	/// The stage field is read rather than discarded, because it is the field that says whether a path
	/// appears once or several times. A merged path carries stage <c>0</c> and appears exactly once;
	/// an <em>unmerged</em> one — a submodule both sides of a merge moved to divergent commits —
	/// carries no stage <c>0</c> record at all and instead appears once per stage present, each naming
	/// a different commit. Listing those verbatim would report one submodule three times, with three
	/// contradictory <see cref="GitSubmodule.Sha"/> values, for a directory that exists once on disk.
	/// One entry per path is emitted instead — see <see cref="StagePrecedence"/> for which record wins.
	/// </para>
	/// </remarks>
	/// <param name="output">Everything git wrote to standard output.</param>
	/// <returns>One entry per gitlink path, in the order git first listed it, with no state resolved yet.</returns>
	/// <exception cref="GitParseException">A gitlink record did not have the expected shape.</exception>
	internal static IReadOnlyList<GitSubmodule> ParseGitlinks(string output)
	{
		Ensure.NotNull(output);

		// The dictionary carries the winning record per path and the order list carries git's own
		// ordering, because the two questions are separate: a later stage of a path already seen
		// revises that path's entry without moving it, so an unmerged submodule stays where git put it
		// rather than jumping to wherever its highest-precedence stage happened to appear.
		Dictionary<string, (int Precedence, GitSubmodule Submodule)> byPath = [];
		List<string> order = [];

		foreach (string record in output.Split('\0'))
		{
			// Tokens are NUL-terminated, so the trailing element is empty.
			if (record.Length == 0)
			{
				continue;
			}

			int tab = record.IndexOf('\t', StringComparison.Ordinal);

			if (tab < 0)
			{
				throw new GitParseException($"Malformed ls-files record: '{record}'.");
			}

			// The path is everything after the tab, which is why a path containing a space, or
			// indeed a tab-free anything, is safe here: the split point is the first tab, and the
			// three fields before it never contain one.
			string[] fields = record[..tab].Split(' ');

			if (fields.Length != 3)
			{
				throw new GitParseException($"Malformed ls-files record: '{record}'.");
			}

			// The mode filter runs before the stage is validated, so an unmerged *blob* — which this
			// command also emits one record per stage for, and which is most of any real conflict — is
			// skipped exactly as it always was rather than being held to a gitlink's expectations.
			if (!string.Equals(fields[0], GitlinkMode, StringComparison.Ordinal))
			{
				continue;
			}

			string path = record[(tab + 1)..];
			int precedence = StagePrecedence(fields[2], record);

			bool seen = byPath.TryGetValue(path, out (int Precedence, GitSubmodule Submodule) existing);

			if (seen && existing.Precedence >= precedence)
			{
				continue;
			}

			if (!seen)
			{
				order.Add(path);
			}

			byPath[path] = (precedence, new GitSubmodule
			{
				Path = GitParseValues.ToRelativeDirectoryPath(path),
				Sha = GitParseValues.ToSemantic<GitCommitSha>(fields[1], "submodule gitlink object id"),
				State = GitSubmoduleState.Unknown,
			});
		}

		return [.. order.Select(path => byPath[path].Submodule)];
	}

	/// <summary>
	/// Ranks an <c>ls-files --stage</c> stage field, so the record that best represents a path wins.
	/// </summary>
	/// <remarks>
	/// Stage <c>0</c> outranks everything: it is what a merged path carries, and its presence means
	/// there is nothing to collapse. Among the unmerged stages the order is <c>2</c> then <c>3</c>
	/// then <c>1</c> — "ours" first, because a caller inspecting a repository mid-merge is standing on
	/// the branch that is being merged <em>into</em> and that is the commit its history records;
	/// "theirs" next, and the merge base last, since the base is the one commit neither side chose.
	/// The fallbacks are not theoretical: a submodule deleted on one side of the merge produces stages
	/// <c>1</c> and <c>3</c> with no <c>2</c> at all.
	/// </remarks>
	/// <param name="stage">The stage field, exactly as git spelled it.</param>
	/// <param name="record">The whole record, for the diagnostic.</param>
	/// <returns>A rank, where higher wins.</returns>
	/// <exception cref="GitParseException">The stage was not one git defines.</exception>
	private static int StagePrecedence(string stage, string record) => stage switch
	{
		"0" => 3,
		"2" => 2,
		"3" => 1,
		"1" => 0,

		// A closed set, unlike submodule status's marker characters: this is plumbing, and git defines
		// exactly these four stages. A fifth would mean the record was misread, not that git grew a
		// state — and inventing a rank for it would silently pick a commit id at random.
		_ => throw new GitParseException($"Malformed ls-files record: '{record}'."),
	};

	/// <summary>
	/// Resolves each gitlink's working-directory state from <c>submodule status</c> output.
	/// </summary>
	/// <remarks>
	/// Matching is by known path rather than by parsing a path out of the line — see this class's own
	/// remarks for why that is what makes the wrapper's output safe to read at all. A gitlink with no
	/// matching status line keeps <see cref="GitSubmoduleState.Unknown"/> rather than being dropped:
	/// a submodule removed from <c>.gitmodules</c> while its gitlink remains is exactly the case a
	/// caller deciding whether a directory is safe to delete needs to see.
	/// </remarks>
	/// <param name="submodules">The gitlinks read from <c>ls-files</c>.</param>
	/// <param name="output">Everything <c>submodule status</c> wrote to standard output.</param>
	/// <returns>The same submodules, with state, checked-out id, and describe filled in.</returns>
	/// <exception cref="GitParseException">A status line did not have the expected shape.</exception>
	internal static IReadOnlyList<GitSubmodule> ApplyStatus(IReadOnlyList<GitSubmodule> submodules, string output)
	{
		Ensure.NotNull(submodules);
		Ensure.NotNull(output);

		Dictionary<string, StatusLine> byPath = [];

		foreach (string record in output.Split('\n').Select(static line => line.TrimEnd('\r')))
		{
			if (record.Length == 0)
			{
				continue;
			}

			(string remainder, StatusLine parsed) = ParseStatusLine(record);
			byPath[remainder] = parsed;
		}

		List<GitSubmodule> resolved = [];

		foreach (GitSubmodule submodule in submodules)
		{
			string path = ToGitSpelling(submodule.Path);

			// An exact match first: a submodule with no describe suffix leaves the remainder equal to
			// the path itself. Otherwise the remainder is the path followed by " (describe)", and
			// because the path is known the describe is simply what is left over — no guess about
			// where the path ended is ever made.
			if (byPath.TryGetValue(path, out StatusLine exact))
			{
				resolved.Add(Apply(submodule, exact, describe: null));
				continue;
			}

			// FirstOrDefault rather than a loop that breaks on its first iteration. Only one status
			// line can describe a given path, so "the first match, if any" is the whole operation, and
			// saying it directly is both clearer than a loop-and-break and free of the flag that
			// carried the result out of one.
			string prefix = path + " (";

			KeyValuePair<string, StatusLine> match = byPath.FirstOrDefault(
				candidate => candidate.Key.StartsWith(prefix, StringComparison.Ordinal) &&
					candidate.Key.EndsWith(')', StringComparison.Ordinal));

			// A no-match default leaves Key null, since Key is a string. The submodule is then kept
			// with its Unknown state rather than dropped — see this method's remarks.
			resolved.Add(match.Key is null
				? submodule
				: Apply(submodule, match.Value, match.Key[prefix.Length..^1]));
		}

		return resolved;
	}

	/// <summary>
	/// Splits one status line into its marker, its object id, and everything after them.
	/// </summary>
	/// <remarks>
	/// The remainder is deliberately left whole rather than split into a path and a describe: doing
	/// that split here is precisely the ambiguity this parser avoids. The object id's length is found
	/// rather than assumed, because a repository created with <c>--object-format=sha256</c> emits
	/// 64-character ids where the default emits 40.
	/// </remarks>
	/// <param name="record">One line of <c>submodule status</c> output.</param>
	/// <returns>The remainder, paired with the marker and object id read from the line.</returns>
	/// <exception cref="GitParseException">The line did not have the expected shape.</exception>
	private static (string Remainder, StatusLine Parsed) ParseStatusLine(string record)
	{
		// Searched from index 1, not 0. The marker for a synchronised submodule is itself a space, so
		// scanning from the start would find the marker rather than the separator after the object
		// id, and every in-sync submodule would be misread.
		int space = record.IndexOf(' ', 1);

		if (space < 2 || space == record.Length - 1)
		{
			throw new GitParseException($"Malformed submodule status line: '{record}'.");
		}

		return (
			record[(space + 1)..],
			new StatusLine(ToState(record[0]), record[1..space]));
	}

	/// <summary>
	/// Applies a matched status line to a gitlink entry.
	/// </summary>
	/// <param name="submodule">The gitlink read from <c>ls-files</c>.</param>
	/// <param name="status">The matched status line.</param>
	/// <param name="describe">The describe expression, or <see langword="null"/> when there was none.</param>
	/// <returns>The completed submodule.</returns>
	private static GitSubmodule Apply(GitSubmodule submodule, StatusLine status, string? describe) => submodule with
	{
		State = status.State,

		// git prints the recorded gitlink again for an uninitialized submodule, which would make it
		// indistinguishable from a synchronised one. Nothing is checked out there, so nothing is
		// reported.
		//
		// The null object id is the same question asked by the other command: git prints it for an
		// unmerged submodule, where there is no single checked-out commit to name. It is a well-formed
		// object id as far as the semantic type is concerned, so nothing downstream would catch it —
		// it would simply read as a commit that happens to be all zeroes.
		CheckedOutSha = status.State == GitSubmoduleState.Uninitialized || IsNullObjectId(status.ObjectId)
			? null
			: GitParseValues.ToSemantic<GitCommitSha>(status.ObjectId, "submodule checked-out object id"),
		Describe = describe,
	};

	/// <summary>
	/// Reports whether an object id is git's null id — the absence of an object, not an object.
	/// </summary>
	/// <remarks>
	/// Tested by its digits rather than against a constant, because the id's width is the repository's
	/// object format: 40 characters under SHA-1 and 64 under <c>--object-format=sha256</c>. The empty
	/// case is excluded explicitly, so "nothing at all" is never mistaken for "all zeroes".
	/// </remarks>
	/// <param name="objectId">The object id git printed.</param>
	/// <returns><see langword="true"/> when every character is a zero.</returns>
	private static bool IsNullObjectId(string objectId) =>
		objectId.Length > 0 && objectId.AsSpan().IndexOfAnyExcept('0') < 0;

	/// <summary>
	/// Spells a path the way git prints it, so a status line can be matched against it.
	/// </summary>
	/// <remarks>
	/// git always prints a path with forward slashes, on every platform. <see cref="RelativeDirectoryPath"/>
	/// canonicalises to the <em>platform's</em> separator, so on Windows the same submodule is
	/// <c>libs\sub</c> here and <c>libs/sub</c> in the status output, and every match would fail —
	/// leaving every submodule <see cref="GitSubmoduleState.Unknown"/> on Windows and nowhere else.
	/// The bug is invisible on a POSIX host, where the two spellings coincide.
	/// <para>
	/// Converting <see cref="Path.DirectorySeparatorChar"/> rather than a literal backslash is what
	/// makes this safe on POSIX too: there the separator is already <c>/</c>, so this is a no-op, and
	/// a backslash that is a legitimate character in a POSIX filename is left alone rather than being
	/// rewritten into a separator.
	/// </para>
	/// </remarks>
	/// <param name="path">The canonicalised path read from <c>ls-files</c>.</param>
	/// <returns>The same path spelled as git prints it.</returns>
	private static string ToGitSpelling(RelativeDirectoryPath path) =>
		path.WeakString.Replace(Path.DirectorySeparatorChar, '/');

	private static GitSubmoduleState ToState(char marker) => marker switch
	{
		' ' => GitSubmoduleState.InSync,
		'+' => GitSubmoduleState.DifferentCommit,
		'-' => GitSubmoduleState.Uninitialized,
		'U' => GitSubmoduleState.Conflicted,

		// Not a closed set the way the change-kind letters are: submodule status is a shell wrapper,
		// and a marker it gains later should report the submodule with an unrecognized state rather
		// than fail the whole listing.
		_ => GitSubmoduleState.Unknown,
	};

	/// <summary>The parts of a status line this parser actually trusts.</summary>
	/// <param name="State">The state its marker character names.</param>
	/// <param name="ObjectId">The object id it reports as checked out.</param>
	private readonly record struct StatusLine(GitSubmoduleState State, string ObjectId);
}
