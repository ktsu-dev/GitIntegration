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
		// Bounded to FieldCount: the last field, %b, is free-form message text that may itself
		// contain the unit separator (verified against real git). A bounded split makes the
		// trailing body field absorb any embedded separator instead of overflowing into extra
		// array elements and shifting every field that follows.
		string[] fields = record.Split(GitOutputFormats.UnitSeparator, FieldCount);

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
				Name = GitParseValues.ToOptionalSemantic<GitAuthorName>(fields[3], "author name"),
				Email = GitParseValues.ToOptionalSemantic<GitAuthorEmail>(fields[4], "author email"),
				Timestamp = ReadTimestamp(fields[5]),
			},
			Committer = new GitSignature
			{
				Name = GitParseValues.ToOptionalSemantic<GitAuthorName>(fields[6], "committer name"),
				Email = GitParseValues.ToOptionalSemantic<GitAuthorEmail>(fields[7], "committer email"),
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

	private static DateTimeOffset ReadTimestamp(string value)
	{
		if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset timestamp)
			|| TryReadOutOfRangeOffset(value, out timestamp))
		{
			return timestamp;
		}

		throw new GitParseException($"git reported a commit timestamp that is not valid: '{value}'.");
	}

	/// <summary>
	/// Reads a timestamp whose offset <see cref="DateTimeOffset"/> refuses: one beyond ±14:00, or one
	/// whose minutes are 60 or more.
	/// </summary>
	/// <remarks>
	/// git stores a commit's zone as an arbitrary <c>±hhmm</c> and prints it unchanged, so imported
	/// history and misconfigured clocks produce values such as <c>+15:00</c> (ktsu-dev/GitIntegration#262)
	/// or <c>+00:99</c> (ktsu-dev/GitIntegration#287) that git itself accepts. git reads the digits as
	/// <c>hh * 60 + mm</c> minutes, so the instant is still exact, and it is returned in UTC rather
	/// than failing the whole log for one record.
	/// </remarks>
	private static bool TryReadOutOfRangeOffset(string value, out DateTimeOffset timestamp)
	{
		timestamp = default;

		// %aI is "yyyy-MM-ddTHH:mm:ss" followed by "+hh:mm" or "-hh:mm".
		const int OffsetLength = 6;
		if (value.Length <= OffsetLength)
		{
			return false;
		}

		string localPart = value[..^OffsetLength];
		string offsetPart = value[^OffsetLength..];

		if (offsetPart[0] is not ('+' or '-')
			|| offsetPart[3] != ':'
			|| !int.TryParse(offsetPart[1..3], NumberStyles.None, CultureInfo.InvariantCulture, out int hours)
			|| !int.TryParse(offsetPart[4..], NumberStyles.None, CultureInfo.InvariantCulture, out int minutes)
			|| !DateTime.TryParseExact(localPart, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime local))
		{
			return false;
		}

		TimeSpan offset = TimeSpan.FromMinutes((hours * 60) + minutes);
		if (offsetPart[0] == '-')
		{
			offset = offset.Negate();
		}

		// Only the cases DateTimeOffset refuses are handled here: an in-range offset that still failed
		// to parse is malformed for some other reason.
		if (minutes < 60 && offset.Duration() <= TimeSpan.FromHours(14))
		{
			return false;
		}

		try
		{
			timestamp = new DateTimeOffset(DateTime.SpecifyKind(local - offset, DateTimeKind.Utc));
			return true;
		}
		catch (ArgumentOutOfRangeException)
		{
			// The instant falls outside the range DateTime can represent.
			return false;
		}
	}
}
