// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;

using ktsu.Semantics.Strings;

[TestClass]
public sealed class GitWorktreeParserTests
{
	private static readonly string MainOnly =
		$"worktree {AbsolutePath("project")}\n" +
		"HEAD 7f3c9a1b2d4e6f8a0c2e4a6b8d0f2a4c6e8b0d2f\n" +
		"branch refs/heads/main\n" +
		"\n";

	private static readonly string ThreeWorktrees =
		$"worktree {AbsolutePath("project")}\n" +
		"HEAD 7f3c9a1b2d4e6f8a0c2e4a6b8d0f2a4c6e8b0d2f\n" +
		"branch refs/heads/main\n" +
		"\n" +
		$"worktree {AbsolutePath("project-feature")}\n" +
		"HEAD 2b8e4d0f6a8c0e2a4c6e8b0d2f4a6c8e0b2d4f6a\n" +
		"branch refs/heads/feature\n" +
		"locked contains uncommitted experiment\n" +
		"\n" +
		$"worktree {AbsolutePath("project-review")}\n" +
		"HEAD 9c1a7f2e4b6d8a0c2e4f6a8b0d2c4e6f8a0b2d4c\n" +
		"detached\n" +
		"prunable gitdir file points to non-existent location\n" +
		"\n";

	/// <summary>Builds a platform-appropriate absolute directory path from a simple name, for fixtures.</summary>
	private static string AbsolutePath(string name) =>
		OperatingSystem.IsWindows() ? $@"C:\{name}" : $"/{name}";

	[TestMethod]
	public void ReadsTheOnlyWorktreeAsTheMainOne()
	{
		IReadOnlyList<GitWorktree> worktrees = GitWorktreeParser.Parse(MainOnly);

		Assert.AreEqual(1, worktrees.Count);
		Assert.AreEqual(AbsolutePath("project"), worktrees[0].Path.WeakString);
		Assert.AreEqual("7f3c9a1b2d4e6f8a0c2e4a6b8d0f2a4c6e8b0d2f".As<GitCommitSha>(), worktrees[0].Head);
		Assert.AreEqual("main".As<GitBranchName>(), worktrees[0].Branch);
		Assert.IsTrue(worktrees[0].IsMain);
		Assert.IsFalse(worktrees[0].IsBare);
		Assert.IsFalse(worktrees[0].IsDetached);
		Assert.IsFalse(worktrees[0].IsLocked);
		Assert.IsFalse(worktrees[0].IsPrunable);
	}

	[TestMethod]
	public void StripsTheRefsHeadsPrefixFromTheBranch()
	{
		// The hosting half of this library already hands back bare branch names. A caller should not
		// have to know which half produced a name to know its shape.
		IReadOnlyList<GitWorktree> worktrees = GitWorktreeParser.Parse(MainOnly);

		Assert.AreEqual("main".As<GitBranchName>(), worktrees[0].Branch);
	}

	[TestMethod]
	public void MarksOnlyTheFirstRecordAsMain()
	{
		IReadOnlyList<GitWorktree> worktrees = GitWorktreeParser.Parse(ThreeWorktrees);

		Assert.AreEqual(3, worktrees.Count);
		Assert.IsTrue(worktrees[0].IsMain);
		Assert.IsFalse(worktrees[1].IsMain);
		Assert.IsFalse(worktrees[2].IsMain);
	}

	[TestMethod]
	public void ReadsALockReasonWhenGitGivesOne()
	{
		IReadOnlyList<GitWorktree> worktrees = GitWorktreeParser.Parse(ThreeWorktrees);

		Assert.IsTrue(worktrees[1].IsLocked);
		Assert.AreEqual("contains uncommitted experiment", worktrees[1].LockReason);
	}

	[TestMethod]
	public void ReportsALockWithNoReasonAsLockedWithoutOne()
	{
		// "locked" alone and "locked <reason>" are different facts, and a caller showing the reason
		// must be able to tell "no reason recorded" from "not locked".
		string output =
			$"worktree {AbsolutePath("project")}\n" +
			"HEAD 7f3c9a1b2d4e6f8a0c2e4a6b8d0f2a4c6e8b0d2f\n" +
			"branch refs/heads/main\n" +
			"locked\n" +
			"\n";

		IReadOnlyList<GitWorktree> worktrees = GitWorktreeParser.Parse(output);

		Assert.IsTrue(worktrees[0].IsLocked);
		Assert.IsNull(worktrees[0].LockReason);
	}

	[TestMethod]
	public void ReadsADetachedWorktreeWithNoBranch()
	{
		IReadOnlyList<GitWorktree> worktrees = GitWorktreeParser.Parse(ThreeWorktrees);

		Assert.IsTrue(worktrees[2].IsDetached);
		Assert.IsNull(worktrees[2].Branch);
		Assert.AreEqual("9c1a7f2e4b6d8a0c2e4f6a8b0d2c4e6f8a0b2d4c".As<GitCommitSha>(), worktrees[2].Head);
	}

	[TestMethod]
	public void ReadsAPrunableReason()
	{
		IReadOnlyList<GitWorktree> worktrees = GitWorktreeParser.Parse(ThreeWorktrees);

		Assert.IsTrue(worktrees[2].IsPrunable);
		Assert.AreEqual("gitdir file points to non-existent location", worktrees[2].PrunableReason);
	}

	[TestMethod]
	public void ReadsABareWorktreeWithNeitherHeadNorBranch()
	{
		string output =
			$"worktree {AbsolutePath("project-bare")}\n" +
			"bare\n" +
			"\n";

		IReadOnlyList<GitWorktree> worktrees = GitWorktreeParser.Parse(output);

		Assert.AreEqual(1, worktrees.Count);
		Assert.IsTrue(worktrees[0].IsBare);
		Assert.IsNull(worktrees[0].Head);
		Assert.IsNull(worktrees[0].Branch);
	}

	[TestMethod]
	public void DoesNotInventARecordFromTheTrailingBlankLine()
	{
		// --porcelain emits a blank line after the last record as well as between records. A naive
		// split on the separator turns that into a phantom entry.
		IReadOnlyList<GitWorktree> worktrees = GitWorktreeParser.Parse(ThreeWorktrees);

		Assert.AreEqual(3, worktrees.Count);
	}

	[TestMethod]
	public void ReadsRecordsSeparatedByCarriageReturnLineFeeds()
	{
		IReadOnlyList<GitWorktree> worktrees = GitWorktreeParser.Parse(MainOnly.Replace("\n", "\r\n", StringComparison.Ordinal));

		Assert.AreEqual(1, worktrees.Count);
		Assert.AreEqual("main".As<GitBranchName>(), worktrees[0].Branch);
	}

	[TestMethod]
	public void ReadsNothingFromEmptyOutput()
	{
		Assert.AreEqual(0, GitWorktreeParser.Parse(string.Empty).Count);
	}

	[TestMethod]
	public void RejectsARecordWithNoWorktreePath()
	{
		// Every attribute is optional except the one naming what the record describes.
		string output =
			"HEAD 7f3c9a1b2d4e6f8a0c2e4a6b8d0f2a4c6e8b0d2f\n" +
			"branch refs/heads/main\n" +
			"\n";

		_ = Assert.ThrowsExactly<GitParseException>(() => GitWorktreeParser.Parse(output));
	}

	[TestMethod]
	public void IgnoresAnAttributeItDoesNotRecognise()
	{
		// Git may add attributes. An unknown one must not fail a listing that is otherwise readable.
		string output =
			$"worktree {AbsolutePath("project")}\n" +
			"HEAD 7f3c9a1b2d4e6f8a0c2e4a6b8d0f2a4c6e8b0d2f\n" +
			"branch refs/heads/main\n" +
			"somethingnew value\n" +
			"\n";

		IReadOnlyList<GitWorktree> worktrees = GitWorktreeParser.Parse(output);

		Assert.AreEqual(1, worktrees.Count);
		Assert.AreEqual("main".As<GitBranchName>(), worktrees[0].Branch);
	}

	[TestMethod]
	public void RejectsAWorktreePathThatIsNotAbsolute()
	{
		// AbsoluteDirectoryPath is expected to refuse a relative path, but ToAbsoluteDirectoryPath
		// must hold that contract itself rather than merely trust the semantic type to enforce it.
		string output =
			"worktree relative/project\n" +
			"HEAD 7f3c9a1b2d4e6f8a0c2e4a6b8d0f2a4c6e8b0d2f\n" +
			"branch refs/heads/main\n" +
			"\n";

		_ = Assert.ThrowsExactly<GitParseException>(() => GitWorktreeParser.Parse(output));
	}
}
