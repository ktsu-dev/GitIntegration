// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;

/// <summary>
/// Reads <c>git push --porcelain</c>.
/// </summary>
/// <remarks>
/// Each record is <c>&lt;flag&gt;TAB&lt;local-ref&gt;:&lt;remote-ref&gt;TAB&lt;summary&gt;</c>.
/// Records are surrounded by lines that are not records — a leading <c>To &lt;url&gt;</c>, a
/// trailing <c>Done</c>, and a tracking notice when the push set an upstream — so the parser
/// recognises records by shape rather than by position.
/// </remarks>
internal static class GitPushParser
{
	private const int FieldCount = 3;

	/// <summary>
	/// Parses porcelain push output.
	/// </summary>
	/// <param name="output">Everything git wrote to standard output.</param>
	/// <returns>The parsed account of every reference the push touched.</returns>
	/// <exception cref="GitParseException">A record did not have the expected shape.</exception>
	internal static GitPushResult Parse(string output)
	{
		Ensure.NotNull(output);

		List<GitRefUpdate> updates = [];

		foreach (string line in output.Split('\n'))
		{
			string record = line.TrimEnd('\r');

			// A record always carries two tabs. Nothing else git prints on this stream does, which
			// is what lets the header, the trailer, and the tracking notice be skipped by shape.
			if (record.Length == 0 || !record.Contains('\t', StringComparison.Ordinal))
			{
				continue;
			}

			updates.Add(ReadUpdate(record));
		}

		return new GitPushResult { Updates = updates };
	}

	private static GitRefUpdate ReadUpdate(string record)
	{
		string[] fields = record.Split('\t');

		if (fields.Length < FieldCount || fields[0].Length != 1)
		{
			throw new GitParseException($"Malformed push record: '{record}'.");
		}

		int colon = fields[1].IndexOf(':');

		if (colon < 0)
		{
			throw new GitParseException($"A push record's reference field has no colon: '{record}'.");
		}

		string source = fields[1][..colon];
		string destination = fields[1][(colon + 1)..];
		string summary = fields[2];
		(GitCommitSha? oldSha, GitCommitSha? newSha) = ReadShaRange(summary);

		return new GitRefUpdate
		{
			Kind = ToKind(fields[0][0]),
			Reference = GitParseValues.ToSemantic<GitRefName>(destination, "pushed reference"),

			// A deletion writes an empty local side — ":refs/heads/gone" — because nothing is being
			// sent, so an empty source is a normal record rather than a malformed one.
			Source = source.Length == 0
				? null
				: GitParseValues.ToSemantic<GitRefName>(source, "pushed source reference"),
			OldSha = oldSha,
			NewSha = newSha,
			Summary = summary,
		};
	}

	/// <summary>
	/// Pulls the object ids out of a summary that carries a commit range.
	/// </summary>
	/// <remarks>
	/// Push reports object ids only inside the summary, abbreviated, as <c>66afe49..7c85857</c>.
	/// Every other summary git writes there is bracketed prose, so anything without the separator
	/// simply has no range to report.
	/// </remarks>
	private static (GitCommitSha? OldSha, GitCommitSha? NewSha) ReadShaRange(string summary)
	{
		int separator = summary.IndexOf("..", StringComparison.Ordinal);

		if (separator <= 0)
		{
			return (null, null);
		}

		string before = summary[..separator];
		string after = summary[(separator + 2)..];

		return GitCommitSha.TryCreate(before, out GitCommitSha? oldSha) &&
			GitCommitSha.TryCreate(after, out GitCommitSha? newSha)
				? (oldSha, newSha)
				: (null, null);
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

		// Deliberately tolerant, unlike the status parser: git's push flags are not a closed set
		// this library can rely on never growing, and failing a whole push report over one
		// unrecognised character would be worse than naming the reference with an unknown kind.
		_ => GitRefUpdateKind.Unknown,
	};
}
