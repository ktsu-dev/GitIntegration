// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Reads <c>git diff --name-status -z</c>.
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

				entries.Add(new GitDiffEntry
				{
					Kind = kind,
					Path = GitParseValues.ToRelativeFilePath(tokens[index++]),
					OriginalPath = GitParseValues.ToRelativeFilePath(originalPath),
					SimilarityPercent = similarity,
				});
			}
			else
			{
				if (index >= tokens.Length)
				{
					throw new GitParseException($"A '{status}' diff record is missing its path.");
				}

				entries.Add(new GitDiffEntry
				{
					Kind = kind,
					Path = GitParseValues.ToRelativeFilePath(tokens[index++]),
					SimilarityPercent = similarity,
				});
			}
		}

		return entries;
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
}
