// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Reads <c>git for-each-ref</c> emitted with <see cref="GitOutputFormats.ForEachTagFormat"/>.
/// </summary>
/// <remarks>
/// Records are newline-separated rather than NUL-separated, which is safe for the same reason it is
/// safe in <see cref="GitBranchParser"/>: git forbids control characters in a reference name, so a
/// line break can never occur inside one.
/// <para>
/// The message subject is the one field that is not a reference name and so is not covered by that
/// guarantee. It is the last field of the record, so an embedded unit separator can only extend it
/// rather than shift the field count — the same reasoning
/// <see cref="GitOutputFormats.UnitSeparator"/> records for a commit body. An embedded newline would
/// split the record, which is why the format asks for the message's <em>subject</em> rather than its
/// whole body: a subject is a single line by construction.
/// </para>
/// </remarks>
internal static class GitTagParser
{
	private const string AnnotatedObjectType = "tag";
	private const int FieldCount = 5;

	/// <summary>
	/// Parses unit-separated tag records.
	/// </summary>
	/// <param name="output">Everything git wrote to standard output.</param>
	/// <returns>The tags, in the order git listed them.</returns>
	/// <exception cref="GitParseException">A record did not have the expected shape.</exception>
	internal static IReadOnlyList<GitTag> Parse(string output)
	{
		Ensure.NotNull(output);

		List<GitTag> tags = [];

		foreach (string record in output.Split('\n').Select(static line => line.TrimEnd('\r')))
		{
			if (record.Length == 0)
			{
				continue;
			}

			// Bounded so an embedded separator in the trailing subject is absorbed by that field
			// rather than shifting the count, matching how GitLogParser bounds its own split.
			string[] fields = record.Split(GitOutputFormats.UnitSeparator, FieldCount);

			if (fields.Length < FieldCount)
			{
				throw new GitParseException($"Malformed for-each-ref tag record: '{record}'.");
			}

			bool isAnnotated = string.Equals(fields[2], AnnotatedObjectType, StringComparison.Ordinal);

			// %(*objectname) dereferences a tag object to the commit beneath it, and is empty when
			// there is no tag object to dereference. Falling back to %(objectname) is what makes Sha
			// mean "the commit this tag names" for both kinds of tag rather than only for one.
			string commitSha = fields[3].Length == 0 ? fields[1] : fields[3];

			tags.Add(new GitTag
			{
				Name = GitParseValues.ToSemantic<GitTagName>(fields[0], "tag name"),
				Sha = GitParseValues.ToSemantic<GitCommitSha>(commitSha, "tag target object id"),
				ObjectSha = GitParseValues.ToSemantic<GitCommitSha>(fields[1], "tag object id"),
				IsAnnotated = isAnnotated,

				// Gated on the object type rather than on emptiness. git falls through to the
				// commit's own subject when the reference names no tag object, so a lightweight tag
				// arrives here carrying text that reads like a tag message and is not one. Reporting
				// null is the only honest answer for a tag that has no message.
				Message = isAnnotated ? fields[4] : null,
			});
		}

		return tags;
	}
}
