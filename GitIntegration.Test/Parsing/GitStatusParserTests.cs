// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

[TestClass]
public class GitStatusParserTests
{
	// Fixtures are inline rather than files on disk: git's porcelain v2 format embeds NUL, which
	// makes a fixture file binary, unreviewable in a diff, and exempt from this repo's EOL
	// normalisation. NUL is written as the six-character escape u0000 (backslash-u-0-0-0-0) rather
	// than backslash-zero, so it can never read as an octal escape when followed by a digit.
	private const string Nul = "\u0000";

	private const string CleanOnMain =
		"# branch.oid 94947d6da5c05bf1c86af335b33cff8cee83cb3f" + Nul +
		"# branch.head main" + Nul;

	private const string TrackingAheadOne =
		"# branch.oid 9429d2063d91f1097de51a196cb8203b06335738" + Nul +
		"# branch.head main" + Nul +
		"# branch.upstream origin/main" + Nul +
		"# branch.ab +1 -0" + Nul;

	private const string DivergedFromUpstream =
		"# branch.oid 9429d2063d91f1097de51a196cb8203b06335738" + Nul +
		"# branch.head main" + Nul +
		"# branch.upstream origin/main" + Nul +
		"# branch.ab +3 -7" + Nul;

	private const string DetachedHead =
		"# branch.oid 94947d6da5c05bf1c86af335b33cff8cee83cb3f" + Nul +
		"# branch.head (detached)" + Nul;

	private const string EmptyRepository =
		"# branch.oid (initial)" + Nul +
		"# branch.head main" + Nul;

	// Captured from a real repository: a rename with a working-tree modification on top, a staged
	// add, and an untracked file.
	private const string MixedWorkingTree =
		"# branch.oid 94947d6da5c05bf1c86af335b33cff8cee83cb3f" + Nul +
		"# branch.head main" + Nul +
		"2 RM N... 100644 100644 100644 5626abf0f72e58d7a153368ba57db4c673c0e171 " +
			"5626abf0f72e58d7a153368ba57db4c673c0e171 R100 renamed.txt" + Nul + "a.txt" + Nul +
		"1 A. N... 000000 100644 100644 0000000000000000000000000000000000000000 " +
			"54f9d6da5c91d556e6b54340b1327573073030af staged.txt" + Nul +
		"? untracked.txt" + Nul;

	private const string PathsWithSpacesAndNonAscii =
		"# branch.head main" + Nul +
		"1 .M N... 100644 100644 100644 5626abf0f72e58d7a153368ba57db4c673c0e171 " +
			"5626abf0f72e58d7a153368ba57db4c673c0e171 dir with spaces/ünïcødé.txt" + Nul;

	private const string UnmergedFile =
		"# branch.head main" + Nul +
		"u UU N... 100644 100644 100644 100644 5626abf0f72e58d7a153368ba57db4c673c0e171 " +
			"54f9d6da5c91d556e6b54340b1327573073030af 6f8b1c2d3e4f5a6b7c8d9e0f1a2b3c4d5e6f7a8b " +
			"conflicted.txt" + Nul;

	private const string IgnoredFile =
		"# branch.head main" + Nul +
		"! bin/output.dll" + Nul;

	[TestMethod]
	public void ReadsTheCurrentBranchAndReportsCleanWhenThereAreNoEntries()
	{
		GitStatus status = GitStatusParser.Parse(CleanOnMain);

		Assert.AreEqual("main".As<GitBranchName>(), status.Branch);
		Assert.IsNull(status.Upstream);
		Assert.IsFalse(status.IsDetached);
		Assert.IsTrue(status.IsClean);
		Assert.AreEqual(0, status.Entries.Count);
	}

	[TestMethod]
	public void ReadsTheUpstreamAndAheadCount()
	{
		GitStatus status = GitStatusParser.Parse(TrackingAheadOne);

		Assert.AreEqual("origin/main".As<GitBranchName>(), status.Upstream);
		Assert.AreEqual(1, status.Ahead);
		Assert.AreEqual(0, status.Behind);
	}

	[TestMethod]
	public void ReadsBothSidesOfADivergedBranch()
	{
		GitStatus status = GitStatusParser.Parse(DivergedFromUpstream);

		Assert.AreEqual(3, status.Ahead);
		Assert.AreEqual(7, status.Behind);
	}

	[TestMethod]
	public void ReportsDetachedHeadWithNoBranch()
	{
		GitStatus status = GitStatusParser.Parse(DetachedHead);

		Assert.IsTrue(status.IsDetached);
		Assert.IsNull(status.Branch);
	}

	[TestMethod]
	public void HandlesAnEmptyRepositoryWithNoCommits()
	{
		// "# branch.oid (initial)" is not a sha and must not be treated as one; the parser ignores
		// branch.oid entirely rather than trying to interpret it.
		GitStatus status = GitStatusParser.Parse(EmptyRepository);

		Assert.AreEqual("main".As<GitBranchName>(), status.Branch);
		Assert.IsTrue(status.IsClean);
	}

	[TestMethod]
	public void HandlesCompletelyEmptyOutput()
	{
		GitStatus status = GitStatusParser.Parse(string.Empty);

		Assert.IsNull(status.Branch);
		Assert.IsTrue(status.IsClean);
	}

	[TestMethod]
	public void ReadsARenameWithItsOriginalPathFromTheFollowingField()
	{
		// The load-bearing case: a '2' record's original path is a separate NUL-terminated field,
		// so a parser that splits on NUL and treats each chunk as a record emits a bogus entry here.
		GitStatus status = GitStatusParser.Parse(MixedWorkingTree);

		GitStatusEntry rename = status.Entries[0];
		Assert.AreEqual(GitFileState.Renamed, rename.IndexState);
		Assert.AreEqual(GitFileState.Modified, rename.WorkTreeState);
		Assert.AreEqual("renamed.txt".As<RelativeFilePath>(), rename.Path);
		Assert.AreEqual("a.txt".As<RelativeFilePath>(), rename.OriginalPath);
	}

	[TestMethod]
	public void ReadsAStagedAddAndAnUntrackedFileAlongsideTheRename()
	{
		GitStatus status = GitStatusParser.Parse(MixedWorkingTree);

		Assert.AreEqual(3, status.Entries.Count);

		GitStatusEntry staged = status.Entries[1];
		Assert.AreEqual(GitFileState.Added, staged.IndexState);
		Assert.AreEqual(GitFileState.Unmodified, staged.WorkTreeState);
		Assert.AreEqual("staged.txt".As<RelativeFilePath>(), staged.Path);
		Assert.IsNull(staged.OriginalPath);

		GitStatusEntry untracked = status.Entries[2];
		Assert.AreEqual(GitFileState.Untracked, untracked.IndexState);
		Assert.AreEqual(GitFileState.Untracked, untracked.WorkTreeState);
		Assert.AreEqual("untracked.txt".As<RelativeFilePath>(), untracked.Path);
	}

	[TestMethod]
	public void ReadsAPathContainingSpacesAndNonAsciiCharacters()
	{
		// The path is the last space-separated field, so splitting must be bounded by the field
		// count or a space in the path truncates it. core.quotepath=false is what keeps the
		// non-ASCII characters literal rather than octal-escaped.
		GitStatus status = GitStatusParser.Parse(PathsWithSpacesAndNonAscii);

		Assert.AreEqual(
			"dir with spaces/ünïcødé.txt".As<RelativeFilePath>(),
			status.Entries[0].Path);
	}

	[TestMethod]
	public void ReportsAnUnmergedFileAsUnmergedOnBothSides()
	{
		GitStatus status = GitStatusParser.Parse(UnmergedFile);

		GitStatusEntry entry = status.Entries[0];
		Assert.AreEqual(GitFileState.Unmerged, entry.IndexState);
		Assert.AreEqual(GitFileState.Unmerged, entry.WorkTreeState);
		Assert.AreEqual("conflicted.txt".As<RelativeFilePath>(), entry.Path);
	}

	[TestMethod]
	public void ReadsAnIgnoredFile()
	{
		GitStatus status = GitStatusParser.Parse(IgnoredFile);

		Assert.AreEqual(GitFileState.Ignored, status.Entries[0].IndexState);
		Assert.AreEqual("bin/output.dll".As<RelativeFilePath>(), status.Entries[0].Path);
	}

	[TestMethod]
	public void RejectsAnUnrecognisedRecordPrefix()
	{
		Assert.ThrowsExactly<GitParseException>(
			() => GitStatusParser.Parse("x something" + Nul));
	}

	[TestMethod]
	public void RejectsATruncatedOrdinaryRecord()
	{
		Assert.ThrowsExactly<GitParseException>(
			() => GitStatusParser.Parse("1 .M N... 100644" + Nul));
	}

	[TestMethod]
	public void RejectsARenameRecordWithNoFollowingOriginalPath()
	{
		string truncated =
			"2 R. N... 100644 100644 100644 5626abf0f72e58d7a153368ba57db4c673c0e171 " +
			"5626abf0f72e58d7a153368ba57db4c673c0e171 R100 renamed.txt" + Nul;

		Assert.ThrowsExactly<GitParseException>(() => GitStatusParser.Parse(truncated));
	}

	[TestMethod]
	public void IgnoresAHeaderItDoesNotRecognise()
	{
		// Forward compatibility: a future git adding a header must not break every caller.
		GitStatus status = GitStatusParser.Parse(
			"# branch.head main" + Nul + "# something.new whatever" + Nul);

		Assert.AreEqual("main".As<GitBranchName>(), status.Branch);
	}
}
