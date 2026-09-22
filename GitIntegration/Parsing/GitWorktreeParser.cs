// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Reads <c>git worktree list --porcelain</c>.
/// </summary>
/// <remarks>
/// The porcelain format is one record per worktree, records separated by a blank line and a blank
/// line after the last. Each line is an attribute name, optionally followed by a space and a value.
/// Only <c>worktree</c> is guaranteed present: a bare worktree reports neither <c>HEAD</c> nor
/// <c>branch</c>, and a detached one reports <c>HEAD</c> and <c>detached</c> but no <c>branch</c>.
/// </remarks>
internal static class GitWorktreeParser
{
	private const string RefsHeadsPrefix = "refs/heads/";

	/// <summary>
	/// Parses a porcelain worktree listing.
	/// </summary>
	/// <param name="output">Everything git wrote to standard output.</param>
	/// <returns>The worktrees, in the order git listed them, the main one first.</returns>
	/// <exception cref="GitParseException">A record named no worktree, or a field failed validation.</exception>
	internal static IReadOnlyList<GitWorktree> Parse(string output)
	{
		Ensure.NotNull(output);

		List<GitWorktree> worktrees = [];
		List<string> record = [];

		// Trimmed at the enumeration source rather than inside the loop: the carriage return is a
		// line-ending artefact, not something any record's content means, so nothing below sees it.
		foreach (string entry in output.Split('\n').Select(static line => line.TrimEnd('\r')))
		{
			if (entry.Length != 0)
			{
				record.Add(entry);
				continue;
			}

			// A blank line closes a record. Closing only a non-empty one is what keeps the trailing
			// blank line git always emits from producing a phantom final entry.
			if (record.Count != 0)
			{
				worktrees.Add(ParseRecord(record, isMain: worktrees.Count == 0));
				record.Clear();
			}
		}

		// Output not ending in a blank line still closes its last record, so a listing stays readable
		// if git ever stops emitting the trailing separator.
		if (record.Count != 0)
		{
			worktrees.Add(ParseRecord(record, isMain: worktrees.Count == 0));
		}

		return worktrees;
	}

	/// <summary>
	/// Turns one record's attribute lines into a worktree.
	/// </summary>
	/// <param name="record">The record's lines, in git's order.</param>
	/// <param name="isMain">Whether this is the first record git emitted.</param>
	/// <returns>The parsed worktree.</returns>
	/// <exception cref="GitParseException">The record named no worktree, or a field failed validation.</exception>
	private static GitWorktree ParseRecord(IReadOnlyList<string> record, bool isMain)
	{
		string? path = null;
		GitCommitSha? head = null;
		GitBranchName? branch = null;
		bool isBare = false;
		bool isDetached = false;
		bool isLocked = false;
		bool isPrunable = false;
		string? lockReason = null;
		string? prunableReason = null;

		foreach (string line in record)
		{
			int separator = line.IndexOf(' ', StringComparison.Ordinal);
			string attribute = separator < 0 ? line : line[..separator];
			string value = separator < 0 ? string.Empty : line[(separator + 1)..];

			switch (attribute)
			{
				case "worktree":
					path = value;
					break;
				case "HEAD":
					head = GitParseValues.ToSemantic<GitCommitSha>(value, "commit id");
					break;
				case "branch":
					branch = GitParseValues.ToSemantic<GitBranchName>(StripRefsHeadsPrefix(value), "branch name");
					break;
				case "bare":
					isBare = true;
					break;
				case "detached":
					isDetached = true;
					break;
				case "locked":
					isLocked = true;
					lockReason = value.Length == 0 ? null : value;
					break;
				case "prunable":
					isPrunable = true;
					prunableReason = value.Length == 0 ? null : value;
					break;
				default:
					// Unknown attributes are skipped rather than rejected. Git may add one, and a
					// listing that is otherwise readable should not fail over a field nobody reads.
					break;
			}
		}

		return path is null
			? throw new GitParseException("git reported a worktree record naming no worktree path.")
			: new GitWorktree
			{
				Path = GitParseValues.ToAbsoluteDirectoryPath(path),
				Head = head,
				Branch = branch,
				IsMain = isMain,
				IsBare = isBare,
				IsDetached = isDetached,
				IsLocked = isLocked,
				LockReason = lockReason,
				IsPrunable = isPrunable,
				PrunableReason = prunableReason,
			};
	}

	/// <summary>
	/// Strips a leading <c>refs/heads/</c>, leaving the value untouched when the prefix is absent.
	/// </summary>
	/// <param name="reference">The reference as git printed it.</param>
	/// <returns>The bare branch name.</returns>
	private static string StripRefsHeadsPrefix(string reference) =>
		reference.StartsWith(RefsHeadsPrefix, StringComparison.Ordinal)
			? reference[RefsHeadsPrefix.Length..]
			: reference;
}
