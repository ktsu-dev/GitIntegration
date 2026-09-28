// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

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
				// body is skipped rather than misread as ordinary hunks. Kind follows the same
				// enum member GitDiffParser reports for the path, so a caller switching on
				// GitChangeKind gets one answer from both verbs.
				isConflicted = true;
				kind = GitChangeKind.Unmerged;
				index++;

				while (index < lines.Count && !IsFileStart(Line(output, lines, index)))
				{
					index++;
				}
			}
			else if (!isBinary)
			{
				// Only a hunk header may reach ParseHunk. Stopping at anything else keeps a content
				// line git emitted in a shape this parser does not recognize out of a range parser,
				// where it would fail as an index exception rather than as GitParseException.
				while (index < lines.Count &&
					Line(output, lines, index).StartsWith(HunkPrefix, StringComparison.Ordinal))
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
	/// <remarks>
	/// Without a rename or copy, both sides of <c>a/P b/P</c> name the same path, so the header is
	/// split at its midpoint, as git's own <c>git_header_name</c> does. Searching for <c>" b/"</c>
	/// cannot work there, because the path itself may contain that text. A rename's header is
	/// ambiguous in the same way, and its <c>rename to</c> line replaces the guess made here.
	/// </remarks>
	/// <param name="line">The <c>diff --git</c> or <c>diff --cc</c> line that starts the file.</param>
	/// <returns>The path found on that line.</returns>
	/// <exception cref="GitParseException">The line does not carry a recognizable path.</exception>
	private static string ReadPathFromFileStart(string line)
	{
		if (line.StartsWith(CombinedHeaderPrefix, StringComparison.Ordinal))
		{
			return UnquotePath(line[CombinedHeaderPrefix.Length..], line);
		}

		string remainder = line[GitHeaderPrefix.Length..];

		// A path git had to C-quote puts the whole operand, prefix included, inside the quotes.
		if (remainder.StartsWith('"'))
		{
			int end = QuotedOperandEnd(remainder, 0, line);
			return StripNewSidePrefix(UnquotePath(remainder[(end + 1)..].TrimStart(' '), line), line);
		}

		if (remainder.EndsWith('"'))
		{
			int start = remainder.LastIndexOf(" \"b/", StringComparison.Ordinal);

			return start < 0
				? throw new GitParseException($"A diff header has no recognizable new-side path: '{line}'.")
				: StripNewSidePrefix(UnquotePath(remainder[(start + 1)..], line), line);
		}

		if (remainder.Length % 2 == 1)
		{
			int middle = remainder.Length / 2;
			string oldSide = remainder[..middle];
			string newSide = remainder[(middle + 1)..];

			if (remainder[middle] == ' ' &&
				oldSide.StartsWith("a/", StringComparison.Ordinal) &&
				newSide.StartsWith("b/", StringComparison.Ordinal) &&
				oldSide.AsSpan(2).SequenceEqual(newSide.AsSpan(2)))
			{
				return newSide[2..];
			}
		}

		// The two sides differ, so this is a rename or copy whose own header line names the
		// new path. The guess only has to be a valid path until that line replaces it.
		int split = remainder.LastIndexOf(BSidePathMarker, StringComparison.Ordinal);

		return split < 0
			? throw new GitParseException($"A diff header has no recognizable new-side path: '{line}'.")
			: remainder[(split + BSidePathMarker.Length)..];
	}

	private static string StripNewSidePrefix(string operand, string line) =>
		operand.StartsWith("b/", StringComparison.Ordinal)
			? operand[2..]
			: throw new GitParseException($"A diff header has no recognizable new-side path: '{line}'.");

	/// <summary>
	/// Finds the closing quote of a C-quoted operand that opens at <paramref name="start"/>.
	/// </summary>
	private static int QuotedOperandEnd(string text, int start, string line)
	{
		for (int position = start + 1; position < text.Length; position++)
		{
			if (text[position] == '\\')
			{
				position++;
			}
			else if (text[position] == '"')
			{
				return position;
			}
		}

		throw new GitParseException($"A quoted path in a diff header is not closed: '{line}'.");
	}

	/// <summary>
	/// Decodes a path git printed with C-style quoting, or returns it unchanged when it is not
	/// quoted. <c>core.quotepath=false</c> stops git quoting non-ASCII bytes, but a name holding a
	/// double quote, a backslash, a tab or another control character is quoted regardless.
	/// </summary>
	/// <param name="value">The path as git printed it.</param>
	/// <param name="line">The header line, for the error message.</param>
	/// <returns>The path as it is named on disk.</returns>
	/// <exception cref="GitParseException">The quoting is malformed.</exception>
	internal static string UnquotePath(string value, string line)
	{
		if (!value.StartsWith('"'))
		{
			return value;
		}

		if (QuotedOperandEnd(value, 0, line) != value.Length - 1)
		{
			throw new GitParseException($"A quoted path in a diff header has trailing text: '{line}'.");
		}

		// Octal escapes carry raw bytes, so the name is rebuilt as UTF-8 and decoded once at the end.
		List<byte> bytes = [];
		Span<byte> encoded = stackalloc byte[4];

		for (int position = 1; position < value.Length - 1; position++)
		{
			char character = value[position];

			if (character != '\\')
			{
				int length = char.IsHighSurrogate(character) && position + 1 < value.Length - 1
					? Encoding.UTF8.GetBytes(value.AsSpan(position++, 2), encoded)
					: Encoding.UTF8.GetBytes(value.AsSpan(position, 1), encoded);

				for (int offset = 0; offset < length; offset++)
				{
					bytes.Add(encoded[offset]);
				}

				continue;
			}

			char escape = value[++position];

			if (escape is >= '0' and <= '3' &&
				position + 2 < value.Length - 1 &&
				value[position + 1] is >= '0' and <= '7' &&
				value[position + 2] is >= '0' and <= '7')
			{
				bytes.Add((byte)(((escape - '0') << 6) | ((value[position + 1] - '0') << 3) | (value[position + 2] - '0')));
				position += 2;
				continue;
			}

			bytes.Add(escape switch
			{
				'a' => (byte)'\a',
				'b' => (byte)'\b',
				't' => (byte)'\t',
				'n' => (byte)'\n',
				'v' => (byte)'\v',
				'f' => (byte)'\f',
				'r' => (byte)'\r',
				'"' => (byte)'"',
				'\\' => (byte)'\\',
				_ => throw new GitParseException($"A quoted path in a diff header has an unknown escape '\\{escape}': '{line}'."),
			});
		}

		return Encoding.UTF8.GetString([.. bytes]);
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
			originalPath = GitParseValues.ToRelativeFilePath(UnquotePath(line[RenameFromPrefix.Length..], line));
			kind = GitChangeKind.Renamed;
		}
		else if (line.StartsWith(RenameToPrefix, StringComparison.Ordinal))
		{
			path = GitParseValues.ToRelativeFilePath(UnquotePath(line[RenameToPrefix.Length..], line));
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
