// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Reads <c>git diff --name-status -z</c>, and <c>git diff --raw --numstat -z</c> when line counts
/// were asked for.
/// </summary>
/// <remarks>
/// The output is a stream of NUL-terminated tokens rather than fixed-size records: an ordinary
/// change is a status token followed by one path, while a rename or a copy is a status token
/// followed by the source path and then the destination path. Consuming it pairwise silently
/// misreads every rename and everything after it.
/// </remarks>
internal static class GitDiffParser
{
	/// <summary>
	/// Parses NUL-terminated name-status output.
	/// </summary>
	/// <param name="output">Everything git wrote to standard output.</param>
	/// <returns>The changed paths, in the order git listed them.</returns>
	/// <exception cref="GitParseException">A record was missing a path.</exception>
	internal static IReadOnlyList<GitDiffEntry> Parse(string output)
	{
		Ensure.NotNull(output);

		string[] tokens = output.Split('\0');
		List<GitDiffEntry> entries = [];

		int index = 0;
		while (index < tokens.Length)
		{
			string status = tokens[index++];

			// Tokens are NUL-terminated, so the trailing element is empty.
			if (status.Length == 0)
			{
				continue;
			}

			entries.Add(ReadEntry(tokens, ref index, status));
		}

		return entries;
	}

	/// <summary>
	/// Parses NUL-terminated <c>--raw --numstat</c> output, correlating the two sections.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Asking for <c>--name-status</c> and <c>--numstat</c> together does <em>not</em> produce both:
	/// they are display formats and the last one wins. <c>--raw</c> is the one that combines, and it
	/// is a superset of <c>--name-status</c> for this library's purposes — the same change letter and
	/// similarity score, plus the modes and blob object ids — so both sections come from one process
	/// with no second invocation to keep consistent.
	/// </para>
	/// <para>
	/// Under <c>-z</c> the two sections are emitted back to back with <b>no delimiter between
	/// them</b>: the raw section's final path token runs straight into the numstat section's first
	/// record. What tells them apart is the shape of a record's first token, and this is the trap
	/// worth stating plainly — a raw record begins with <c>:</c>, and a numstat record begins with a
	/// digit or <c>-</c>. That test is safe because it is only ever applied at a record boundary: a
	/// path is consumed by the record that owns it and never reaches this check, so a file literally
	/// named <c>:something</c> cannot be mistaken for a raw header.
	/// </para>
	/// <para>
	/// Correlation between the sections is positional, because both list the same changes in the same
	/// order. Matching on path string would be the fragile choice: a rename appears in the raw
	/// section as two path tokens after the status, and in the numstat section as an <em>empty</em>
	/// path field followed by two more tokens, so the two sections do not spell a rename the same way.
	/// </para>
	/// <para>
	/// Every shape here was verified against git 2.43 and git 2.55.
	/// </para>
	/// </remarks>
	/// <param name="output">Everything git wrote to standard output.</param>
	/// <returns>The changed paths with their line counts, in the order git listed them.</returns>
	/// <exception cref="GitParseException">A record was malformed, or the two sections disagree.</exception>
	internal static IReadOnlyList<GitDiffEntry> ParseWithLineCounts(string output)
	{
		Ensure.NotNull(output);

		string[] tokens = output.Split('\0');
		int index = 0;

		List<GitDiffEntry> entries = ReadRawSection(tokens, ref index);
		List<(int? Insertions, int? Deletions)> counts = ReadNumstatSection(tokens, ref index);

		if (counts.Count != entries.Count)
		{
			// git emits one numstat record per raw record, so a mismatch means the section boundary
			// was read wrongly rather than that a count is genuinely missing. Reporting nulls instead
			// would present a parser bug as an absent measurement.
			throw new GitParseException(
				$"git reported {entries.Count} raw diff records but {counts.Count} numstat records.");
		}

		for (int entry = 0; entry < entries.Count; entry++)
		{
			entries[entry] = entries[entry] with
			{
				Insertions = counts[entry].Insertions,
				Deletions = counts[entry].Deletions,
			};
		}

		return entries;
	}

	/// <summary>
	/// Reads the raw section, stopping at the first token that is not a raw header.
	/// </summary>
	/// <param name="tokens">Every NUL-separated token.</param>
	/// <param name="index">The read position, advanced past the raw section.</param>
	/// <returns>The entries the raw section describes, without line counts.</returns>
	private static List<GitDiffEntry> ReadRawSection(string[] tokens, ref int index)
	{
		List<GitDiffEntry> entries = [];

		while (index < tokens.Length)
		{
			string header = tokens[index];

			// Tokens are NUL-terminated, so the trailing element is empty.
			if (header.Length == 0)
			{
				index++;
				continue;
			}

			if (header[0] != ':')
			{
				// The numstat section has begun. There is no delimiter to consume.
				break;
			}

			index++;

			// ":<oldmode> <newmode> <oldsha> <newsha> <status>" — the status is the last
			// space-separated field, and everything before it is metadata this library's model has no
			// place for. Taking it from the end rather than by field index means a future git adding
			// a field ahead of the status does not shift what is read.
			int lastSpace = header.LastIndexOf(' ');

			if (lastSpace < 0 || lastSpace == header.Length - 1)
			{
				throw new GitParseException($"Malformed raw diff header: '{header}'.");
			}

			entries.Add(ReadEntry(tokens, ref index, header[(lastSpace + 1)..]));
		}

		return entries;
	}

	/// <summary>
	/// Reads the numstat section from the position the raw section stopped at.
	/// </summary>
	/// <param name="tokens">Every NUL-separated token.</param>
	/// <param name="index">The read position, advanced past the numstat section.</param>
	/// <returns>One insertion and deletion pair per record, in order.</returns>
	private static List<(int? Insertions, int? Deletions)> ReadNumstatSection(string[] tokens, ref int index)
	{
		List<(int? Insertions, int? Deletions)> counts = [];

		while (index < tokens.Length)
		{
			string record = tokens[index++];

			if (record.Length == 0)
			{
				continue;
			}

			// "<insertions>\t<deletions>\t<path>", or "<insertions>\t<deletions>\t" followed by two
			// path tokens for a rename or a copy. The path is not read here at all: correlation is
			// positional, so only the counts and the number of tokens each record consumes matter.
			string[] fields = record.Split('\t');

			if (fields.Length != 3)
			{
				throw new GitParseException($"Malformed numstat diff record: '{record}'.");
			}

			// An empty path field is how -z spells a rename or a copy: the old and new paths follow
			// as their own tokens. Without -z the same record uses the "old => new" arrow form, which
			// is ambiguous against a filename containing " => " — which is why this builder asks for
			// -z here as it already does everywhere else.
			if (fields[2].Length == 0)
			{
				if (index + 1 >= tokens.Length)
				{
					throw new GitParseException(
						$"A renamed numstat record is missing its source or destination path: '{record}'.");
				}

				index += 2;
			}

			counts.Add((ReadCount(fields[0]), ReadCount(fields[1])));
		}

		return counts;
	}

	/// <summary>
	/// Reads one entry's paths, given the status token that introduced it.
	/// </summary>
	/// <remarks>
	/// Shared by both formats, because <c>--raw</c> spells a status exactly as
	/// <c>--name-status</c> does once the leading metadata is stripped: the same change letter and
	/// the same optional similarity score.
	/// </remarks>
	/// <param name="tokens">Every NUL-separated token.</param>
	/// <param name="index">The read position, advanced past this entry's path or paths.</param>
	/// <param name="status">The status token, such as <c>M</c> or <c>R075</c>.</param>
	/// <returns>The entry, without line counts.</returns>
	private static GitDiffEntry ReadEntry(string[] tokens, ref int index, string status)
	{
		GitChangeKind kind = ToChangeKind(status[0]);
		int? similarity = ReadSimilarity(status);

		if (kind is GitChangeKind.Renamed or GitChangeKind.Copied)
		{
			if (index + 1 >= tokens.Length)
			{
				throw new GitParseException(
					$"A '{status}' diff record is missing its source or destination path.");
			}

			string originalPath = tokens[index++];

			return new GitDiffEntry
			{
				Kind = kind,
				Path = GitParseValues.ToRelativeFilePath(tokens[index++]),
				OriginalPath = GitParseValues.ToRelativeFilePath(originalPath),
				SimilarityPercent = similarity,
			};
		}

		if (index >= tokens.Length)
		{
			throw new GitParseException($"A '{status}' diff record is missing its path.");
		}

		return new GitDiffEntry
		{
			Kind = kind,
			Path = GitParseValues.ToRelativeFilePath(tokens[index++]),
			SimilarityPercent = similarity,
		};
	}

	private static GitChangeKind ToChangeKind(char code) => code switch
	{
		'A' => GitChangeKind.Added,
		'C' => GitChangeKind.Copied,
		'D' => GitChangeKind.Deleted,
		'M' => GitChangeKind.Modified,
		'R' => GitChangeKind.Renamed,
		'T' => GitChangeKind.TypeChanged,
		'U' => GitChangeKind.Unmerged,

		// Unlike the status codes, this set is not closed: git also emits 'B' for a broken pairing
		// and 'X' for a state it documents as a bug. Failing the whole diff over one letter would
		// be worse than reporting the path with an unknown change kind.
		_ => GitChangeKind.Unknown,
	};

	private static int? ReadSimilarity(string status) =>
		status.Length > 1 &&
		int.TryParse(status.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int score)
			? score
			: null;

	/// <summary>
	/// Reads one numstat count, which git writes as <c>-</c> for a binary file.
	/// </summary>
	/// <remarks>
	/// A binary change is not a zero-line change, which is the whole reason the counts are nullable
	/// rather than defaulting to zero: reporting 0 here would state that nothing changed in a file
	/// that git declined to measure.
	/// </remarks>
	/// <param name="field">The count field as git printed it.</param>
	/// <returns>The count, or <see langword="null"/> when git reported none.</returns>
	/// <exception cref="GitParseException">The field is neither a whole number nor <c>-</c>.</exception>
	private static int? ReadCount(string field)
	{
		if (string.Equals(field, "-", StringComparison.Ordinal))
		{
			return null;
		}

		return int.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out int count)
			? count
			: throw new GitParseException($"A numstat count is neither a number nor '-': '{field}'.");
	}
}
