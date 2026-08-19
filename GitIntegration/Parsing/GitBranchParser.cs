// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;

/// <summary>
/// Reads <c>git for-each-ref</c> emitted with <see cref="GitOutputFormats.ForEachRefFormat"/>.
/// </summary>
/// <remarks>
/// Records are newline-separated rather than NUL-separated, which is safe here and only here: git
/// forbids control characters in a reference name, so a line break can never occur inside a record.
/// </remarks>
internal static class GitBranchParser
{
	private const string RemotePrefix = "refs/remotes/";
	private const string HeadSuffix = "/HEAD";
	private const int FieldCount = 5;

	/// <summary>
	/// Parses unit-separated reference records.
	/// </summary>
	/// <param name="output">Everything git wrote to standard output.</param>
	/// <returns>The branches, in the order git listed them.</returns>
	/// <exception cref="GitParseException">A record did not have the expected shape.</exception>
	internal static IReadOnlyList<GitBranch> Parse(string output)
	{
		Ensure.NotNull(output);

		List<GitBranch> branches = [];

		foreach (string line in output.Split('\n'))
		{
			string record = line.TrimEnd('\r');

			if (record.Length == 0)
			{
				continue;
			}

			string[] fields = record.Split(GitOutputFormats.UnitSeparator);

			if (fields.Length < FieldCount)
			{
				throw new GitParseException($"Malformed for-each-ref record: '{record}'.");
			}

			string fullRefName = fields[0];
			bool isRemote = fullRefName.StartsWith(RemotePrefix, StringComparison.Ordinal);

			// refs/remotes/<remote>/HEAD is a symbolic reference naming the remote's default
			// branch, not a branch of its own. Its short name is the bare remote name, so leaving
			// it in reports a branch called "origin" in every clone.
			if (isRemote && fullRefName.EndsWith(HeadSuffix, StringComparison.Ordinal))
			{
				continue;
			}

			branches.Add(new GitBranch
			{
				Name = GitParseValues.ToSemantic<GitBranchName>(fields[1], "branch name"),
				Sha = GitParseValues.ToSemantic<GitCommitSha>(fields[2], "branch object id"),

				// %(upstream:short) is empty when the branch tracks nothing.
				Upstream = fields[3].Length == 0
					? null
					: GitParseValues.ToSemantic<GitBranchName>(fields[3], "upstream branch name"),

				// %(HEAD) is "*" for the checked-out branch and a single space otherwise, never an
				// empty field.
				IsCurrent = string.Equals(fields[4], "*", StringComparison.Ordinal),
				IsRemote = isRemote,
			});
		}

		return branches;
	}
}
