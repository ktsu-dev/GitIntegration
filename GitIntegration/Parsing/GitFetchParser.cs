// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

/// <summary>
/// Reads <c>git fetch --porcelain</c>.
/// </summary>
/// <remarks>
/// Each record is <c>&lt;flag&gt;&lt;space&gt;&lt;old-id&gt; &lt;new-id&gt; &lt;local-ref&gt;</c>.
/// Unlike push, the object ids are full and explicit and there is no summary field. Available only
/// on git 2.41 and above; the fetch builder (added in a later task) decides whether this parser
/// runs at all.
/// </remarks>
internal static class GitFetchParser
{
	private const int FieldCount = 3;

	/// <summary>
	/// Parses porcelain fetch output.
	/// </summary>
	/// <param name="output">Everything git wrote to standard output.</param>
	/// <returns>The parsed account of every reference the fetch changed.</returns>
	/// <exception cref="GitParseException">A record did not have the expected shape.</exception>
	internal static GitFetchResult Parse(string output)
	{
		Ensure.NotNull(output);

		List<GitRefUpdate> updates = [];

		foreach (string line in output.Split('\n'))
		{
			string record = line.TrimEnd('\r');

			// Nothing at all is printed when the fetch changed nothing, so empty lines are the
			// ordinary case rather than a malformed record.
			if (record.Length == 0)
			{
				continue;
			}

			updates.Add(ReadUpdate(record));
		}

		return new GitFetchResult { Updates = updates, DetailAvailable = true };
	}

	private static GitRefUpdate ReadUpdate(string record)
	{
		// Character zero is the flag and character one is the separator, so the fields begin at
		// index two. A fast-forward's flag is a space, which is why the record cannot be trimmed
		// or split on whitespace generally — the flag would vanish into the delimiter.
		if (record.Length < 2)
		{
			throw new GitParseException($"Malformed fetch record: '{record}'.");
		}

		char flag = record[0];
		string[] fields = record[2..].Split(' ', FieldCount);

		if (fields.Length < FieldCount)
		{
			throw new GitParseException($"Malformed fetch record: '{record}'.");
		}

		return new GitRefUpdate
		{
			Kind = ToKind(flag),
			Reference = GitParseValues.ToSemantic<GitRefName>(fields[2], "fetched reference"),
			OldSha = GitParseValues.ToSemantic<GitCommitSha>(fields[0], "fetched old object id"),
			NewSha = GitParseValues.ToSemantic<GitCommitSha>(fields[1], "fetched new object id"),
		};
	}

	private static GitRefUpdateKind ToKind(char flag) => flag switch
	{
		' ' => GitRefUpdateKind.FastForward,
		'+' => GitRefUpdateKind.Forced,
		'-' => GitRefUpdateKind.Removed,
		'*' => GitRefUpdateKind.Created,
		'!' => GitRefUpdateKind.Rejected,
		'=' => GitRefUpdateKind.UpToDate,
		't' => GitRefUpdateKind.TagUpdate,
		_ => GitRefUpdateKind.Unknown,
	};
}
