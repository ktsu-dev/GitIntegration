// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.IO;
using System.Linq;

[TestClass]
public class GitPatchParserTests
{
	// Fixtures captured from git version 2.54.0 (Apple Git-157) on macOS, with
	// `git -c color.ui=false diff --no-ext-diff --no-textconv -U3`. patch-new-file.txt and
	// patch-deleted-file.txt add `--cached` to that, because `new file mode` and `deleted file
	// mode` headers only appear for a change that is already in the index.

	/// <summary>Reads a captured fixture's raw text from the test output's Fixtures directory.</summary>
	private static string Fixture(string name) =>
		File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

	[TestMethod]
	public void ParsesTwoHunksWithTheirLineNumbers()
	{
		GitPatch patch = GitPatchParser.Parse(Fixture("patch-two-hunks.txt"));

		GitFilePatch file = patch.Files.Single();

		Assert.AreEqual("f.txt", file.Path.WeakString);
		Assert.AreEqual(GitChangeKind.Modified, file.Kind);
		Assert.AreEqual(2, file.Hunks.Count);

		GitHunk first = file.Hunks[0];
		Assert.AreEqual(1, first.OldStart);
		Assert.IsTrue(first.Text.StartsWith("@@", StringComparison.Ordinal));
		Assert.IsTrue(
			first.Lines.Any(line => line.Kind == GitPatchLineKind.Added),
			"A modification has at least one added line.");
	}

	[TestMethod]
	public void KeepsTheNoNewlineMarkerInsideTheHunkText()
	{
		GitPatch patch = GitPatchParser.Parse(Fixture("patch-no-newline.txt"));

		StringAssert.Contains(
			patch.Files.Single().Hunks.Single().Text,
			"\\ No newline at end of file",
			StringComparison.Ordinal);
	}

	[TestMethod]
	public void ReadsARenamedFileThatAlsoChanged()
	{
		GitPatch patch = GitPatchParser.Parse(Fixture("patch-rename-modified.txt"));

		GitFilePatch file = patch.Files.Single();

		Assert.AreEqual(GitChangeKind.Renamed, file.Kind);
		Assert.IsNotNull(file.OriginalPath);
		Assert.AreNotEqual(file.OriginalPath!.WeakString, file.Path.WeakString);
		Assert.IsTrue(
			file.Hunks.Count > 0,
			"A rename can carry content changes too, and treating rename as hunkless drops them.");
	}

	[TestMethod]
	public void FlagsABinaryFileAndGivesItNoHunks()
	{
		GitFilePatch file = GitPatchParser.Parse(Fixture("patch-binary.txt")).Files.Single();

		Assert.IsTrue(file.IsBinary);
		Assert.AreEqual(0, file.Hunks.Count);
	}

	[TestMethod]
	public void FlagsAConflictedFileAndGivesItNoHunks()
	{
		GitFilePatch file = GitPatchParser.Parse(Fixture("patch-conflict.txt")).Files.Single();

		Assert.IsTrue(
			file.IsConflicted,
			"Combined format is not an applyable patch, so it must not be parsed into ordinary hunks.");
		Assert.AreEqual(0, file.Hunks.Count);
	}

	[TestMethod]
	public void ParsesCarriageReturnContentWithoutStrippingIt()
	{
		GitFilePatch file = GitPatchParser.Parse(Fixture("patch-crlf.txt")).Files.Single();

		StringAssert.Contains(
			file.Hunks.Single().Text,
			"\r",
			StringComparison.Ordinal,
			"A patch is byte-sensitive, so content line endings survive parsing.");
	}

	[TestMethod]
	public void ReadsAnAddedFileAsAdded()
	{
		GitFilePatch file = GitPatchParser.Parse(Fixture("patch-new-file.txt")).Files.Single();

		Assert.AreEqual(GitChangeKind.Added, file.Kind);
		Assert.AreEqual("added.txt", file.Path.WeakString);
		Assert.AreEqual(1, file.Hunks.Count);
		Assert.IsTrue(
			file.Hunks.Single().Lines.All(line => line.Kind == GitPatchLineKind.Added),
			"A new file's only hunk is every one of its lines added, with no context to anchor it.");
	}

	[TestMethod]
	public void ReadsADeletedFileAsDeleted()
	{
		GitFilePatch file = GitPatchParser.Parse(Fixture("patch-deleted-file.txt")).Files.Single();

		Assert.AreEqual(GitChangeKind.Deleted, file.Kind);
		Assert.AreEqual("gone.txt", file.Path.WeakString);
		Assert.AreEqual(1, file.Hunks.Count);
		Assert.IsTrue(
			file.Hunks.Single().Lines.All(line => line.Kind == GitPatchLineKind.Removed),
			"A deleted file's only hunk is every one of its lines removed.");
	}

	[TestMethod]
	public void ReadsAConflictedFileAsUnmerged() =>
		Assert.AreEqual(
			GitChangeKind.Unmerged,
			GitPatchParser.Parse(Fixture("patch-conflict.txt")).Files.Single().Kind,
			"GitDiffParser reports the same path as Unmerged, and a caller switching on one enum must not get two answers for it.");

	[TestMethod]
	public void StopsAtALineThatIsNotAHunkHeader()
	{
		// diff.suppressBlankEmpty writes an empty context line as a bare newline. The hunk body
		// ends there, and what follows must not reach the header parser, which would index past
		// the end of a short line.
		string output =
			"diff --git a/f.txt b/f.txt\nindex 111..222 100644\n--- a/f.txt\n+++ b/f.txt\n" +
			"@@ -1,3 +1,3 @@\n a\n\n-b\n+B\n";

		GitFilePatch file = GitPatchParser.Parse(output).Files.Single();

		Assert.AreEqual(1, file.Hunks.Count);
	}

	[TestMethod]
	public void ParsesAnEmptyDiffAsNoFiles() =>
		Assert.AreEqual(0, GitPatchParser.Parse(string.Empty).Files.Count);
}
