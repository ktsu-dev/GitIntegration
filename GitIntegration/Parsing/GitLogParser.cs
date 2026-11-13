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
	/// <summary>
	/// Sha, tree, parents, author date and committer date: the fields that cannot contain the unit
	/// separator, and so the only ones it separates.
	/// </summary>
	private const int MachineFieldCount = 5;

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
		// Only the machine fields are unit-separated. The bounded split leaves the free-text tail,
		// which may itself contain the separator, whole as the last element.
		string[] fields = record.Split(GitOutputFormats.UnitSeparator, MachineFieldCount + 1);

		if (fields.Length <= MachineFieldCount)
		{
			throw new GitParseException($"Malformed log record: '{record}'.");
		}

		TextFieldReader text = new(fields[MachineFieldCount], record);
		string authorName = text.ReadUntil('<');
		string authorEmail = text.ReadUntil('>');
		string committerName = text.ReadUntil('<');
		string committerEmail = text.ReadUntil('>');
		string subject = text.ReadUntil('\n');

		return new GitCommit
		{
			Sha = GitParseValues.ToSemantic<GitCommitSha>(fields[0], "commit id"),
			TreeSha = GitParseValues.ToSemantic<GitCommitSha>(fields[1], "tree id"),
			ParentShas = ReadParents(fields[2]),
			Author = new GitSignature
			{
				Name = GitParseValues.ToOptionalSemantic<GitAuthorName>(authorName, "author name"),
				Email = GitParseValues.ToOptionalSemantic<GitAuthorEmail>(authorEmail, "author email"),
				Timestamp = ReadTimestamp(fields[3]),
			},
			Committer = new GitSignature
			{
				Name = GitParseValues.ToOptionalSemantic<GitAuthorName>(committerName, "committer name"),
				Email = GitParseValues.ToOptionalSemantic<GitAuthorEmail>(committerEmail, "committer email"),
				Timestamp = ReadTimestamp(fields[4]),
			},
			Subject = subject,

			// %b ends with the newline git appends after the body. That terminator is not part of
			// the message, and leaving it on would make every round trip through this type add
			// another one.
			Body = text.Rest.TrimEnd('\n', '\r'),
		};
	}

	/// <summary>
	/// Reads the free-text tail of a log record, <c>%an&lt;%ae&gt;%cn&lt;%ce&gt;%s%n%b</c>, one
	/// delimiter at a time.
	/// </summary>
	/// <remarks>
	/// Each delimiter is one git guarantees the field before it cannot contain: see
	/// <see cref="GitOutputFormats.LogFormat"/>.
	/// </remarks>
	private sealed class TextFieldReader(string text, string record)
	{
		private int _position;

		internal string Rest => text[_position..];

		internal string ReadUntil(char delimiter)
		{
			int end = text.IndexOf(delimiter, _position);
			if (end < 0)
			{
				throw new GitParseException($"Malformed log record: '{record}'.");
			}

			string value = text[_position..end];
			_position = end + 1;
			return value;
		}
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
