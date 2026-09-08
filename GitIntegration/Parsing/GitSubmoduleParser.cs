// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.IO;

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
/// cannot say whether a submodule is initialised, or whether a different commit is checked out.
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
	/// Each record is <c>&lt;mode&gt; &lt;object&gt; &lt;stage&gt;\t&lt;path&gt;</c>. Everything that
	/// is not a gitlink is skipped, which is most of a repository: this command lists the whole index,
	/// and the mode filter is what turns that into a submodule listing.
	/// </remarks>
	/// <param name="output">Everything git wrote to standard output.</param>
	/// <returns>One entry per gitlink, in the order git listed them, with no state resolved yet.</returns>
	/// <exception cref="GitParseException">A gitlink record did not have the expected shape.</exception>
	internal static IReadOnlyList<GitSubmodule> ParseGitlinks(string output)
	{
		Ensure.NotNull(output);

		List<GitSubmodule> submodules = [];

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

			if (!string.Equals(fields[0], GitlinkMode, StringComparison.Ordinal))
			{
				continue;
			}

			submodules.Add(new GitSubmodule
			{
				Path = GitParseValues.ToRelativeDirectoryPath(record[(tab + 1)..]),
				Sha = GitParseValues.ToSemantic<GitCommitSha>(fields[1], "submodule gitlink object id"),
				State = GitSubmoduleState.Unknown,
			});
		}

		return submodules;
	}

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

		foreach (string line in output.Split('\n'))
		{
			string record = line.TrimEnd('\r');

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

			string prefix = path + " (";
			bool matched = false;

			foreach (KeyValuePair<string, StatusLine> candidate in byPath)
			{
				if (candidate.Key.StartsWith(prefix, StringComparison.Ordinal) &&
					candidate.Key.EndsWith(')', StringComparison.Ordinal))
				{
					string describe = candidate.Key[prefix.Length..^1];
					resolved.Add(Apply(submodule, candidate.Value, describe));
					matched = true;
					break;
				}
			}

			if (!matched)
			{
				resolved.Add(submodule);
			}
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

		// git prints the recorded gitlink again for an uninitialised submodule, which would make it
		// indistinguishable from a synchronised one. Nothing is checked out there, so nothing is
		// reported.
		CheckedOutSha = status.State == GitSubmoduleState.Uninitialised
			? null
			: GitParseValues.ToSemantic<GitCommitSha>(status.ObjectId, "submodule checked-out object id"),
		Describe = describe,
	};

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
		'-' => GitSubmoduleState.Uninitialised,
		'U' => GitSubmoduleState.Conflicted,

		// Not a closed set the way the change-kind letters are: submodule status is a shell wrapper,
		// and a marker it gains later should report the submodule with an unrecognised state rather
		// than fail the whole listing.
		_ => GitSubmoduleState.Unknown,
	};

	/// <summary>The parts of a status line this parser actually trusts.</summary>
	/// <param name="State">The state its marker character names.</param>
	/// <param name="ObjectId">The object id it reports as checked out.</param>
	private readonly record struct StatusLine(GitSubmoduleState State, string ObjectId);
}
