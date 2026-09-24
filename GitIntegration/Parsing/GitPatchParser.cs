// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Globalization;

using ktsu.Semantics.Paths;

/// <summary>
/// Reads <c>git diff -U3</c> output into a <see cref="GitPatch"/>: one <see cref="GitFilePatch"/>
/// per changed file, and one <see cref="GitHunk"/> per contiguous change within it.
/// </summary>
/// <remarks>
/// Nothing here runs git. The text is whatever a caller captured, and every hunk's
/// <see cref="GitHunk.Text"/> is carried through verbatim so that feeding it back to
/// <c>git apply</c> reproduces exactly the bytes git itself emitted.
/// </remarks>
internal static class GitPatchParser
{
	private const string GitHeaderPrefix = "diff --git ";
	private const string CombinedHeaderPrefix = "diff --cc ";
	private const string BSidePathMarker = " b/";
	private const string RenameFromPrefix = "rename from ";
	private const string RenameToPrefix = "rename to ";
	private const string NewFileModePrefix = "new file mode";
	private const string DeletedFileModePrefix = "deleted file mode";
	private const string BinaryFilesPrefix = "Binary files ";
	private const string BinaryPatchPrefix = "GIT binary patch";
	private const string ConflictHunkPrefix = "@@@";
	private const string HunkPrefix = "@@ ";

	/// <summary>
	/// Parses the text a diff-producing command wrote to standard output.
	/// </summary>
	/// <param name="output">Everything git wrote to standard output.</param>
	/// <returns>The patch, with one file per <c>diff --git</c> or <c>diff --cc</c> block found.</returns>
	/// <exception cref="GitParseException">A header or a hunk was malformed.</exception>
	public static GitPatch Parse(string output)
	{
		Ensure.NotNull(output);

		List<(int Start, int End)> lines = SplitLines(output);
		List<GitFilePatch> files = [];

		int index = 0;
		while (index < lines.Count)
		{
			if (!IsFileStart(Line(output, lines, index)))
			{
				index++;
				continue;
			}

			files.Add(ParseFile(output, lines, ref index));
		}

		return new GitPatch { Files = files };
	}

	/// <summary>
	/// Splits <paramref name="output"/> into line spans, each excluding its own trailing
	/// <c>\n</c> but keeping every other byte, including a trailing <c>\r</c> from a
	/// carriage-return-terminated source line.
	/// </summary>
	/// <param name="output">The text to split.</param>
	/// <returns>Each line's start and end offset into <paramref name="output"/>.</returns>
	private static List<(int Start, int End)> SplitLines(string output)
	{
		List<(int Start, int End)> lines = [];

		int position = 0;
		while (position <= output.Length)
		{
			int newline = output.IndexOf('\n', position);

			if (newline < 0)
			{
				if (position < output.Length)
				{
					lines.Add((position, output.Length));
				}

				break;
			}

			lines.Add((position, newline));
			position = newline + 1;
		}

		return lines;
	}

	private static string Line(string output, List<(int Start, int End)> lines, int index) =>
		output[lines[index].Start..lines[index].End];

	/// <summary>
	/// The offset one past the last line belonging to a region ending at <paramref name="index"/>:
	/// the start of that line, or the end of the text when the region runs to the end of the input.
	/// </summary>
	private static int RegionEnd(string output, List<(int Start, int End)> lines, int index) =>
		index < lines.Count ? lines[index].Start : output.Length;

	private static bool IsFileStart(string line) =>
		line.StartsWith(GitHeaderPrefix, StringComparison.Ordinal) ||
		line.StartsWith(CombinedHeaderPrefix, StringComparison.Ordinal);

	private static GitFilePatch ParseFile(string output, List<(int Start, int End)> lines, ref int index)
	{
		int fileStart = lines[index].Start;
		string firstLine = Line(output, lines, index);

		RelativeFilePath path = GitParseValues.ToRelativeFilePath(ReadPathFromFileStart(firstLine));
		RelativeFilePath? originalPath = null;
		GitChangeKind kind = GitChangeKind.Modified;
		bool isBinary = false;
		bool isConflicted = false;

		index++;

		while (index < lines.Count)
		{
			string line = Line(output, lines, index);

			if (IsFileStart(line) ||
				line.StartsWith(ConflictHunkPrefix, StringComparison.Ordinal) ||
				line.StartsWith(HunkPrefix, StringComparison.Ordinal))
			{
				break;
			}

			ApplyHeaderLine(line, ref kind, ref path, ref originalPath, ref isBinary);
			index++;
		}

		string header = output[fileStart..RegionEnd(output, lines, index)];
		List<GitHunk> hunks = [];

		if (index < lines.Count)
		{
			string boundary = Line(output, lines, index);

			if (boundary.StartsWith(ConflictHunkPrefix, StringComparison.Ordinal))
			{
				// Combined format from an unmerged path is not a patch git apply accepts, so its
				// body is skipped rather than misread as ordinary hunks.
				isConflicted = true;
				index++;

				while (index < lines.Count && !IsFileStart(Line(output, lines, index)))
				{
					index++;
				}
			}
			else if (!isBinary)
			{
				while (index < lines.Count && !IsFileStart(Line(output, lines, index)))
				{
					hunks.Add(ParseHunk(output, lines, ref index));
				}
			}
		}

		return new GitFilePatch
		{
			Path = path,
			OriginalPath = originalPath,
			Kind = kind,
			IsBinary = isBinary,
			IsConflicted = isConflicted,
			Header = header,
			Hunks = hunks,
		};
	}

	/// <summary>
	/// Reads the path to use until a more specific header line overrides it: the combined format's
	/// single path, or the ordinary format's new-side path.
	/// </summary>
	/// <param name="line">The <c>diff --git</c> or <c>diff --cc</c> line that starts the file.</param>
	/// <returns>The path found on that line.</returns>
	/// <exception cref="GitParseException">The line does not carry a recognisable path.</exception>
	private static string ReadPathFromFileStart(string line)
	{
		if (line.StartsWith(CombinedHeaderPrefix, StringComparison.Ordinal))
		{
			return line[CombinedHeaderPrefix.Length..];
		}

		string remainder = line[GitHeaderPrefix.Length..];

		// Searched from the end rather than the first match, because the old-side path can itself
		// contain the literal text " b/" as part of a directory or file name.
		int split = remainder.LastIndexOf(BSidePathMarker, StringComparison.Ordinal);

		return split < 0
			? throw new GitParseException($"A diff header has no recognisable new-side path: '{line}'.")
			: remainder[(split + BSidePathMarker.Length)..];
	}

	private static void ApplyHeaderLine(
		string line,
		ref GitChangeKind kind,
		ref RelativeFilePath path,
		ref RelativeFilePath? originalPath,
		ref bool isBinary)
	{
		if (line.StartsWith(RenameFromPrefix, StringComparison.Ordinal))
		{
			originalPath = GitParseValues.ToRelativeFilePath(line[RenameFromPrefix.Length..]);
			kind = GitChangeKind.Renamed;
		}
		else if (line.StartsWith(RenameToPrefix, StringComparison.Ordinal))
		{
			path = GitParseValues.ToRelativeFilePath(line[RenameToPrefix.Length..]);
			kind = GitChangeKind.Renamed;
		}
		else if (line.StartsWith(NewFileModePrefix, StringComparison.Ordinal))
		{
			kind = GitChangeKind.Added;
		}
		else if (line.StartsWith(DeletedFileModePrefix, StringComparison.Ordinal))
		{
			kind = GitChangeKind.Deleted;
		}
		else if (line.StartsWith(BinaryFilesPrefix, StringComparison.Ordinal) ||
			line.StartsWith(BinaryPatchPrefix, StringComparison.Ordinal))
		{
			isBinary = true;
		}
	}

	private static GitHunk ParseHunk(string output, List<(int Start, int End)> lines, ref int index)
	{
		int hunkStart = lines[index].Start;
		(int oldStart, int oldCount, int newStart, int newCount, string heading) =
			ParseHunkHeader(Line(output, lines, index));

		int oldLine = oldStart;
		int newLine = newStart;
		List<GitPatchLine> patchLines = [];

		index++;

		while (index < lines.Count)
		{
			string line = Line(output, lines, index);
			char marker = line.Length > 0 ? line[0] : '\0';

			if (marker == '\\')
			{
				// "\ No newline at end of file" belongs to the hunk's text but is not itself a line
				// of content.
				index++;
				continue;
			}

			GitPatchLineKind? kind = marker switch
			{
				' ' => GitPatchLineKind.Context,
				'+' => GitPatchLineKind.Added,
				'-' => GitPatchLineKind.Removed,
				_ => null,
			};

			if (kind is null)
			{
				break;
			}

			int? oldNumber = kind == GitPatchLineKind.Added ? null : oldLine;
			int? newNumber = kind == GitPatchLineKind.Removed ? null : newLine;

			patchLines.Add(new GitPatchLine
			{
				Kind = kind.Value,
				Text = line[1..],
				OldNumber = oldNumber,
				NewNumber = newNumber,
			});

			if (kind != GitPatchLineKind.Added)
			{
				oldLine++;
			}

			if (kind != GitPatchLineKind.Removed)
			{
				newLine++;
			}

			index++;
		}

		string text = output[hunkStart..RegionEnd(output, lines, index)];

		return new GitHunk
		{
			OldStart = oldStart,
			OldCount = oldCount,
			NewStart = newStart,
			NewCount = newCount,
			Heading = heading,
			Lines = patchLines,
			Text = text,
		};
	}

	/// <summary>
	/// Parses a hunk's <c>@@ -old,count +new,count @@ heading</c> line.
	/// </summary>
	/// <param name="line">The hunk header line.</param>
	/// <returns>The two ranges and the heading, which is the empty string when git found none.</returns>
	/// <exception cref="GitParseException">The line is not a well-formed hunk header.</exception>
	private static (int OldStart, int OldCount, int NewStart, int NewCount, string Heading) ParseHunkHeader(
		string line)
	{
		// "@@ -" is always exactly four characters, so the old range starts right after it.
		int oldRangeStart = 4;
		int oldRangeEnd = line.IndexOf(' ', oldRangeStart);

		if (oldRangeEnd < 0)
		{
			throw new GitParseException($"Malformed hunk header: '{line}'.");
		}

		(int oldStart, int oldCount) = ParseRange(line[oldRangeStart..oldRangeEnd], line);

		// Skips the space and the '+' that introduce the new range.
		int newRangeStart = oldRangeEnd + 2;
		int newRangeEnd = line.IndexOf(' ', newRangeStart);

		if (newRangeEnd < 0)
		{
			throw new GitParseException($"Malformed hunk header: '{line}'.");
		}

		(int newStart, int newCount) = ParseRange(line[newRangeStart..newRangeEnd], line);

		int closingMarker = line.IndexOf("@@", newRangeEnd, StringComparison.Ordinal);
		string heading = closingMarker >= 0
			? line[(closingMarker + 2)..].TrimStart(' ')
			: string.Empty;

		return (oldStart, oldCount, newStart, newCount, heading);
	}

	/// <summary>
	/// Parses one side of a hunk header, such as <c>16,5</c> or <c>1</c>.
	/// </summary>
	/// <param name="range">The range text, without its leading <c>-</c> or <c>+</c>.</param>
	/// <param name="line">The whole hunk header, used only to report a failure.</param>
	/// <returns>The start line and the count, which git omits when it is 1.</returns>
	private static (int Start, int Count) ParseRange(string range, string line)
	{
		int comma = range.IndexOf(',');

		return comma < 0
			? (ParseLineNumber(range, line), 1)
			: (ParseLineNumber(range[..comma], line), ParseLineNumber(range[(comma + 1)..], line));
	}

	private static int ParseLineNumber(string value, string line) =>
		int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
			? number
			: throw new GitParseException($"Malformed hunk header: '{line}'.");
}
