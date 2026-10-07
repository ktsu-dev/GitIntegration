// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

[TestClass]
public class GitPatchTests
{
	private const string Header =
		"diff --git a/f.txt b/f.txt\nindex 92dfa21..81f8caa 100644\n--- a/f.txt\n+++ b/f.txt\n";

	private static GitHunk HunkOne => new()
	{
		OldStart = 1,
		OldCount = 3,
		NewStart = 1,
		NewCount = 3,
		Heading = string.Empty,
		Lines = [],
		Text = "@@ -1,3 +1,3 @@\n a\n-b\n+B\n c\n",
	};

	private static GitHunk HunkTwo => new()
	{
		OldStart = 8,
		OldCount = 3,
		NewStart = 8,
		NewCount = 3,
		Heading = string.Empty,
		Lines = [],
		Text = "@@ -8,3 +8,3 @@\n h\n-i\n+I\n j\n",
	};

	private static GitFilePatch FileWithTwoHunks => new()
	{
		Path = "f.txt".As<RelativeFilePath>(),
		OriginalPath = null,
		Kind = GitChangeKind.Modified,
		IsBinary = false,
		IsConflicted = false,
		Header = Header,
		Hunks = [HunkOne, HunkTwo],
	};

	private const string RenameHeader =
		"diff --git a/a.txt b/b.txt\nsimilarity index 93%\nrename from a.txt\nrename to b.txt\n"
		+ "index 92dfa21..81f8caa 100644\n--- a/a.txt\n+++ b/b.txt\n";

	private static GitFilePatch RenamedFileWithTwoHunks => FileWithTwoHunks with
	{
		Path = "b.txt".As<RelativeFilePath>(),
		OriginalPath = "a.txt".As<RelativeFilePath>(),
		Header = RenameHeader,
	};

	[TestMethod]
	public void PatchForSomeHunksOfARenameWritesAPlainChangeToTheNewPath()
	{
		string patch = RenamedFileWithTwoHunks.PatchFor([HunkOne]);

		Assert.AreEqual(
			"diff --git a/b.txt b/b.txt\nindex 92dfa21..81f8caa 100644\n--- a/b.txt\n+++ b/b.txt\n" + HunkOne.Text,
			patch);
	}

	[TestMethod]
	public void PatchForEveryHunkOfARenameKeepsTheRenameHeader() =>
		Assert.AreEqual(
			RenameHeader + HunkOne.Text + HunkTwo.Text,
			RenamedFileWithTwoHunks.PatchFor([HunkOne, HunkTwo]));

	[TestMethod]
	public void PatchForSomeHunksOfAQuotedRenameKeepsTheQuoting()
	{
		GitFilePatch file = RenamedFileWithTwoHunks with
		{
			Path = "b \"q\".txt".As<RelativeFilePath>(),
			Header = "diff --git a/a.txt \"b/b \\\"q\\\".txt\"\nsimilarity index 93%\nrename from a.txt\n"
				+ "rename to \"b \\\"q\\\".txt\"\nindex 92dfa21..81f8caa 100644\n--- a/a.txt\n+++ \"b/b \\\"q\\\".txt\"\n",
		};

		Assert.AreEqual(
			"diff --git \"a/b \\\"q\\\".txt\" \"b/b \\\"q\\\".txt\"\nindex 92dfa21..81f8caa 100644\n"
				+ "--- \"a/b \\\"q\\\".txt\"\n+++ \"b/b \\\"q\\\".txt\"\n" + HunkTwo.Text,
			file.PatchFor([HunkTwo]));
	}

	[TestMethod]
	public void PatchForOneHunkCarriesTheHeaderAndThatHunkOnly()
	{
		string patch = FileWithTwoHunks.PatchFor([HunkOne]);

		Assert.AreEqual(Header + HunkOne.Text, patch);
	}

	[TestMethod]
	public void PatchForEveryHunkKeepsThemInFileOrder()
	{
		string patch = FileWithTwoHunks.PatchFor([HunkTwo, HunkOne]);

		Assert.AreEqual(
			Header + HunkOne.Text + HunkTwo.Text,
			patch,
			"Hunks are emitted in the order the file holds them, not the order the caller asked for, because git reads a patch top to bottom.");
	}

	[TestMethod]
	public void PatchForNoHunksRefuses() =>
		Assert.ThrowsExactly<ArgumentException>(
			() => _ = FileWithTwoHunks.PatchFor([]),
			"A header with no body is a patch git rejects as corrupt, and the caller learns nothing from that message.");

	[TestMethod]
	public void PatchForNullRefuses() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = FileWithTwoHunks.PatchFor(null!));

	[TestMethod]
	public void PatchForAHunkFromAnotherFileRefuses()
	{
		GitHunk foreign = HunkOne with { Text = "@@ -1,3 +1,3 @@\n x\n-y\n+Y\n z\n" };

		ArgumentException thrown = Assert.ThrowsExactly<ArgumentException>(
			() => _ = FileWithTwoHunks.PatchFor([foreign]),
			"Filtering silently would return a header with no body, which reaches git as the corrupt-patch error the empty check exists to prevent.");

		StringAssert.Contains(thrown.Message, "f.txt", StringComparison.Ordinal);
	}

	[TestMethod]
	public void PatchForAMixOfOwnAndForeignHunksRefuses()
	{
		GitHunk foreign = HunkOne with { Text = "@@ -1,3 +1,3 @@\n x\n-y\n+Y\n z\n" };

		_ = Assert.ThrowsExactly<ArgumentException>(
			() => _ = FileWithTwoHunks.PatchFor([HunkOne, foreign]),
			"A multi-file selection that staged some hunks and dropped the rest without a word is the worse half of this failure.");
	}

	[TestMethod]
	public void PatchPreservesAHunkVerbatimIncludingTheNoNewlineMarker()
	{
		GitHunk hunk = new()
		{
			OldStart = 1,
			OldCount = 1,
			NewStart = 1,
			NewCount = 1,
			Heading = string.Empty,
			Lines = [],
			Text = "@@ -1 +1 @@\n-a\n+b\n\\ No newline at end of file\n",
		};

		GitFilePatch file = FileWithTwoHunks with { Hunks = [hunk] };

		StringAssert.Contains(
			file.PatchFor([hunk]),
			"\\ No newline at end of file",
			StringComparison.Ordinal,
			"Regenerating a hunk from its parsed lines loses this, and apply then rejects the patch.");
	}
}
