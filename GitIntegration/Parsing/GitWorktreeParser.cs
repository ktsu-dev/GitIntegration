// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Reads <c>git worktree list --porcelain</c>, with or without <c>-z</c>.
/// </summary>
/// <remarks>
/// The porcelain format is one record per worktree, records separated by a blank line and a blank
/// line after the last. Each line is an attribute name, optionally followed by a space and a value.
/// Only <c>worktree</c> is guaranteed present: a bare worktree reports neither <c>HEAD</c> nor
/// <c>branch</c>, and a detached one reports <c>HEAD</c> and <c>detached</c> but no <c>branch</c>.
/// <para>
/// Under <c>-z</c> every line ends in NUL instead of a newline, so an empty field closes a record,
/// and every value is printed raw. Without it, git C-quotes a <c>locked</c> or <c>prunable</c>
/// reason that contains a quote, a backslash or a newline, and prints the <c>worktree</c> path raw,
/// so a newline in a path cannot be told apart from the next line.
/// </para>
/// </remarks>
internal static class GitWorktreeParser
{
	private const string RefsHeadsPrefix = "refs/heads/";

	/// <summary>
	/// Parses a porcelain worktree listing.
	/// </summary>
	/// <param name="output">Everything git wrote to standard output.</param>
	/// <param name="nulTerminated">Whether git was asked for <c>-z</c>.</param>
	/// <returns>The worktrees, in the order git listed them, the main one first.</returns>
	/// <exception cref="GitParseException">A record named no worktree, or a field failed validation.</exception>
	internal static IReadOnlyList<GitWorktree> Parse(string output, bool nulTerminated = false)
	{
		Ensure.NotNull(output);

		List<GitWorktree> worktrees = [];
		List<string> record = [];

		// Under -z a field is taken exactly as git wrote it: a carriage return or newline in it is part
		// of a path or reason. Otherwise the carriage return is a line-ending artefact, trimmed at the
		// enumeration source so nothing below sees it.
		IEnumerable<string> entries = nulTerminated
			? output.Split('\0')
			: output.Split('\n').Select(static line => line.TrimEnd('\r'));

		foreach (string entry in entries)
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
				worktrees.Add(ParseRecord(record, isMain: worktrees.Count == 0, nulTerminated));
				record.Clear();
			}
		}

		// Output not ending in a blank line still closes its last record, so a listing stays readable
		// if git ever stops emitting the trailing separator.
		if (record.Count != 0)
		{
			worktrees.Add(ParseRecord(record, isMain: worktrees.Count == 0, nulTerminated));
		}

		return worktrees;
	}

	/// <summary>
	/// Turns one record's attribute lines into a worktree.
	/// </summary>
	/// <param name="record">The record's lines, in git's order.</param>
	/// <param name="isMain">Whether this is the first record git emitted.</param>
	/// <param name="nulTerminated">Whether the values are raw, as <c>-z</c> prints them.</param>
	/// <returns>The parsed worktree.</returns>
	/// <exception cref="GitParseException">The record named no worktree, or a field failed validation.</exception>
	private static GitWorktree ParseRecord(IReadOnlyList<string> record, bool isMain, bool nulTerminated)
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
					lockReason = ToReason(value, line, nulTerminated);
					break;
				case "prunable":
					isPrunable = true;
					prunableReason = ToReason(value, line, nulTerminated);
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
	/// Turns a <c>locked</c> or <c>prunable</c> value into the reason it carries.
	/// </summary>
	/// <param name="value">The value as git printed it.</param>
	/// <param name="line">The whole line, for the error message.</param>
	/// <param name="nulTerminated">Whether git printed the value raw, as it does under <c>-z</c>.</param>
	/// <returns>The reason, or null when git gave none.</returns>
	/// <exception cref="GitParseException">A quoted reason was malformed.</exception>
	private static string? ToReason(string value, string line, bool nulTerminated)
	{
		if (value.Length == 0)
		{
			return null;
		}

		// Raw under -z, so a reason that merely starts with a quote is left as it is. Without -z, git
		// C-quotes exactly the reasons that need it and never begins an unquoted one with a quote.
		return nulTerminated ? value : GitPatchParser.UnquotePath(value, line);
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
