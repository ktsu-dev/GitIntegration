// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Reads <c>git status --porcelain=v2 --branch -z</c>.
/// </summary>
/// <remarks>
/// The v2 porcelain format is documented and stable across git versions, which is why it is parsed
/// rather than the human-facing output. <c>-z</c> terminates every record with a NUL so that a path
/// containing a space or a quote survives intact.
/// </remarks>
internal static class GitStatusParser
{
	/// <summary>
	/// Parses porcelain v2 status output.
	/// </summary>
	/// <param name="output">Everything git wrote to standard output.</param>
	/// <returns>The parsed status.</returns>
	/// <exception cref="GitParseException">A record did not have the expected shape.</exception>
	internal static GitStatus Parse(string output)
	{
		Ensure.NotNull(output);

		GitBranchName? branch = null;
		GitBranchName? upstream = null;
		int ahead = 0;
		int behind = 0;
		bool isDetached = false;
		List<GitStatusEntry> entries = [];

		// Records are NUL-terminated rather than NUL-separated, so the final element is always
		// empty; empty elements are skipped rather than counted.
		string[] records = output.Split('\0');

		for (int index = 0; index < records.Length; index++)
		{
			string record = records[index];

			if (record.Length == 0)
			{
				continue;
			}

			switch (record[0])
			{
				case '#':
					ReadHeader(record, ref branch, ref upstream, ref ahead, ref behind, ref isDetached);
					break;

				case '1':
					entries.Add(ReadOrdinaryEntry(record));
					break;

				case '2':
					// A rename or copy carries its original path as the next NUL-terminated field,
					// which is consumed here so the outer loop does not see it as a record.
					if (index + 1 >= records.Length)
					{
						throw new GitParseException(
							$"A rename status record has no following original path: '{record}'.");
					}

					entries.Add(ReadRenameEntry(record, records[++index]));
					break;

				case 'u':
					entries.Add(ReadUnmergedEntry(record));
					break;

				case '?':
					entries.Add(ReadPathOnlyEntry(record, GitFileState.Untracked));
					break;

				case '!':
					entries.Add(ReadPathOnlyEntry(record, GitFileState.Ignored));
					break;

				default:
					throw new GitParseException($"Unrecognised status record: '{record}'.");
			}
		}

		return new GitStatus
		{
			Branch = branch,
			Upstream = upstream,
			Ahead = ahead,
			Behind = behind,
			IsDetached = isDetached,
			Entries = entries,
		};
	}

	private static void ReadHeader(
		string record,
		ref GitBranchName? branch,
		ref GitBranchName? upstream,
		ref int ahead,
		ref int behind,
		ref bool isDetached)
	{
		// "# branch.head main" splits into "#", "branch.head", "main"; the value keeps any spaces
		// it contains, which matters for "# branch.ab +1 -0".
		string[] parts = record.Split(' ', 3);

		if (parts.Length < 3)
		{
			return;
		}

		switch (parts[1])
		{
			case "branch.head":
				if (string.Equals(parts[2], "(detached)", StringComparison.Ordinal))
				{
					isDetached = true;
				}
				else
				{
					branch = GitParseValues.ToSemantic<GitBranchName>(parts[2], "branch name");
				}

				break;

			case "branch.upstream":
				upstream = GitParseValues.ToSemantic<GitBranchName>(parts[2], "upstream branch name");
				break;

			case "branch.ab":
				ReadAheadBehind(parts[2], ref ahead, ref behind);
				break;

			default:
				// branch.oid is deliberately ignored — it is "(initial)" in a repository with no
				// commits, so it is not always an object id — and so is any header a future git
				// adds, which must not break existing callers.
				break;
		}
	}

	private static void ReadAheadBehind(string value, ref int ahead, ref int behind)
	{
		foreach (string token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			if (token.Length < 2 ||
				!int.TryParse(token.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int count))
			{
				continue;
			}

			if (token[0] == '+')
			{
				ahead = count;
			}
			else if (token[0] == '-')
			{
				behind = count;
			}
		}
	}

	private static GitStatusEntry ReadOrdinaryEntry(string record)
	{
		// 1 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <path>
		string[] fields = SplitFields(record, count: 9);

		return new GitStatusEntry
		{
			IndexState = ToFileState(fields[1][0]),
			WorkTreeState = ToFileState(fields[1][1]),
			Path = GitParseValues.ToRelativeFilePath(fields[8]),
		};
	}

	private static GitStatusEntry ReadRenameEntry(string record, string originalPath)
	{
		// 2 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <X><score> <path>
		string[] fields = SplitFields(record, count: 10);

		return new GitStatusEntry
		{
			IndexState = ToFileState(fields[1][0]),
			WorkTreeState = ToFileState(fields[1][1]),
			Path = GitParseValues.ToRelativeFilePath(fields[9]),
			OriginalPath = GitParseValues.ToRelativeFilePath(originalPath),
		};
	}

	private static GitStatusEntry ReadUnmergedEntry(string record)
	{
		// u <XY> <sub> <m1> <m2> <m3> <mW> <h1> <h2> <h3> <path>
		string[] fields = SplitFields(record, count: 11);

		// Both sides report Unmerged rather than the individual XY letters. For a 'u' record the
		// letters describe which side of the merge contributed what — "AA" for both-added, "DU" for
		// deleted-by-us — and collapsing them to Added or Deleted would lose the one fact a caller
		// needs, which is that the path is conflicted.
		return new GitStatusEntry
		{
			IndexState = GitFileState.Unmerged,
			WorkTreeState = GitFileState.Unmerged,
			Path = GitParseValues.ToRelativeFilePath(fields[10]),
		};
	}

	private static GitStatusEntry ReadPathOnlyEntry(string record, GitFileState state)
	{
		// ? <path> and ! <path> carry no per-side detail, so the single state applies to both.
		string[] fields = SplitFields(record, count: 2);

		return new GitStatusEntry
		{
			IndexState = state,
			WorkTreeState = state,
			Path = GitParseValues.ToRelativeFilePath(fields[1]),
		};
	}

	private static string[] SplitFields(string record, int count)
	{
		// Bounded so the path, which is always last and may contain spaces, is never split.
		string[] fields = record.Split(' ', count);

		if (fields.Length < count || (count > 2 && fields[1].Length < 2))
		{
			throw new GitParseException($"Malformed status record: '{record}'.");
		}

		return fields;
	}

	private static GitFileState ToFileState(char code) => code switch
	{
		'.' => GitFileState.Unmodified,
		'M' => GitFileState.Modified,
		'A' => GitFileState.Added,
		'D' => GitFileState.Deleted,
		'R' => GitFileState.Renamed,
		'C' => GitFileState.Copied,
		'T' => GitFileState.TypeChanged,
		'U' => GitFileState.Unmerged,
		_ => throw new GitParseException($"Unrecognised status code '{code}'."),
	};
}
