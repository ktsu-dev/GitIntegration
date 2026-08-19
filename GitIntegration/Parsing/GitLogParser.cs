// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Reads <c>git log -z</c> emitted with <see cref="GitOutputFormats.LogFormat"/>.
/// </summary>
internal static class GitLogParser
{
	private const int FieldCount = 11;

	/// <summary>
	/// Parses NUL-terminated, unit-separated log output.
	/// </summary>
	/// <param name="output">Everything git wrote to standard output.</param>
	/// <returns>The commits, in the order git listed them.</returns>
	/// <exception cref="GitParseException">A record did not have the expected shape.</exception>
	internal static IReadOnlyList<GitCommit> Parse(string output)
	{
		Ensure.NotNull(output);

		List<GitCommit> commits = [];

		// Records are NUL-terminated, so the final element is always empty.
		foreach (string record in output.Split('\0'))
		{
			if (record.Length == 0)
			{
				continue;
			}

			commits.Add(ReadCommit(record));
		}

		return commits;
	}

	private static GitCommit ReadCommit(string record)
	{
		string[] fields = record.Split(GitOutputFormats.UnitSeparator);

		if (fields.Length < FieldCount)
		{
			throw new GitParseException($"Malformed log record: '{record}'.");
		}

		return new GitCommit
		{
			Sha = GitParseValues.ToSemantic<GitCommitSha>(fields[0], "commit id"),
			TreeSha = GitParseValues.ToSemantic<GitCommitSha>(fields[1], "tree id"),
			ParentShas = ReadParents(fields[2]),
			Author = new GitSignature
			{
				Name = GitParseValues.ToSemantic<GitAuthorName>(fields[3], "author name"),
				Email = GitParseValues.ToSemantic<GitAuthorEmail>(fields[4], "author email"),
				Timestamp = ReadTimestamp(fields[5]),
			},
			Committer = new GitSignature
			{
				Name = GitParseValues.ToSemantic<GitAuthorName>(fields[6], "committer name"),
				Email = GitParseValues.ToSemantic<GitAuthorEmail>(fields[7], "committer email"),
				Timestamp = ReadTimestamp(fields[8]),
			},
			Subject = fields[9],

			// %b ends with the newline git appends after the body. That terminator is not part of
			// the message, and leaving it on would make every round trip through this type add
			// another one.
			Body = fields[10].TrimEnd('\n', '\r'),
		};
	}

	private static GitCommitSha[] ReadParents(string value)
	{
		// %P is space-separated and empty for a root commit.
		if (value.Length == 0)
		{
			return [];
		}

		string[] tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		GitCommitSha[] parents = new GitCommitSha[tokens.Length];

		for (int index = 0; index < tokens.Length; index++)
		{
			parents[index] = GitParseValues.ToSemantic<GitCommitSha>(tokens[index], "parent commit id");
		}

		return parents;
	}

	private static DateTimeOffset ReadTimestamp(string value) =>
		DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset timestamp)
			? timestamp
			: throw new GitParseException($"git reported a commit timestamp that is not valid: '{value}'.");
}
