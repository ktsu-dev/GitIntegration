// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Collections.Generic;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

[TestClass]
public class GitDiffParserTests
{
	private const string Nul = "\u0000";

	// Captured verbatim. A delete and an add are two tokens each; the rename in the middle is
	// three — status, source, destination — which is why this cannot be parsed pairwise.
	private const string DeleteRenameAdd =
		"D" + Nul + "a.txt" + Nul +
		"R100" + Nul + "b.txt" + Nul + "b-renamed.txt" + Nul +
		"A" + Nul + "copy.txt" + Nul;

	private const string CopyWithPartialSimilarity =
		"C75" + Nul + "source.txt" + Nul + "destination.txt" + Nul;

	private const string PathWithSpacesAndNonAscii =
		"M" + Nul + "dir with spaces/ünïcødé.txt" + Nul;

	[TestMethod]
	public void ReadsATwoTokenRecord()
	{
		IReadOnlyList<GitDiffEntry> entries = GitDiffParser.Parse(DeleteRenameAdd);

		Assert.AreEqual(3, entries.Count);
		Assert.AreEqual(GitChangeKind.Deleted, entries[0].Kind);
		Assert.AreEqual("a.txt".As<RelativeFilePath>(), entries[0].Path);
		Assert.IsNull(entries[0].OriginalPath);
		Assert.IsNull(entries[0].SimilarityPercent);
	}

	[TestMethod]
	public void ReadsAThreeTokenRenameWithoutConsumingTheNextRecord()
	{
		// The failure this guards against is subtle: a pairwise parser reads "R100"/"b.txt" as one
		// entry and then "b-renamed.txt"/"A" as another, so the count still looks plausible.
		IReadOnlyList<GitDiffEntry> entries = GitDiffParser.Parse(DeleteRenameAdd);

		GitDiffEntry rename = entries[1];
		Assert.AreEqual(GitChangeKind.Renamed, rename.Kind);
		Assert.AreEqual("b-renamed.txt".As<RelativeFilePath>(), rename.Path);
		Assert.AreEqual("b.txt".As<RelativeFilePath>(), rename.OriginalPath);
		Assert.AreEqual(100, rename.SimilarityPercent);

		Assert.AreEqual(GitChangeKind.Added, entries[2].Kind);
		Assert.AreEqual("copy.txt".As<RelativeFilePath>(), entries[2].Path);
	}

	[TestMethod]
	public void ReadsACopyWithItsSimilarityScore()
	{
		IReadOnlyList<GitDiffEntry> entries = GitDiffParser.Parse(CopyWithPartialSimilarity);

		Assert.AreEqual(GitChangeKind.Copied, entries[0].Kind);
		Assert.AreEqual(75, entries[0].SimilarityPercent);
		Assert.AreEqual("source.txt".As<RelativeFilePath>(), entries[0].OriginalPath);
		Assert.AreEqual("destination.txt".As<RelativeFilePath>(), entries[0].Path);
	}

	[TestMethod]
	public void ReadsAPathContainingSpacesAndNonAsciiCharacters()
	{
		IReadOnlyList<GitDiffEntry> entries = GitDiffParser.Parse(PathWithSpacesAndNonAscii);

		Assert.AreEqual("dir with spaces/ünïcødé.txt".As<RelativeFilePath>(), entries[0].Path);
	}

	[TestMethod]
	public void MapsEveryDocumentedStatusLetter()
	{
		string output =
			"A" + Nul + "added.txt" + Nul +
			"D" + Nul + "deleted.txt" + Nul +
			"M" + Nul + "modified.txt" + Nul +
			"T" + Nul + "typechanged.txt" + Nul +
			"U" + Nul + "unmerged.txt" + Nul;

		IReadOnlyList<GitDiffEntry> entries = GitDiffParser.Parse(output);

		Assert.AreEqual(GitChangeKind.Added, entries[0].Kind);
		Assert.AreEqual(GitChangeKind.Deleted, entries[1].Kind);
		Assert.AreEqual(GitChangeKind.Modified, entries[2].Kind);
		Assert.AreEqual(GitChangeKind.TypeChanged, entries[3].Kind);
		Assert.AreEqual(GitChangeKind.Unmerged, entries[4].Kind);
	}

	[TestMethod]
	public void ReportsAnUnrecognisedStatusLetterAsUnknownRatherThanThrowing()
	{
		// git emits 'B' for a broken pairing and 'X' for a state it calls a bug. Neither is worth
		// failing an entire diff over, and unlike the status format the set is not closed, so an
		// unknown letter degrades to Unknown instead of throwing.
		IReadOnlyList<GitDiffEntry> entries = GitDiffParser.Parse("B" + Nul + "broken.txt" + Nul);

		Assert.AreEqual(GitChangeKind.Unknown, entries[0].Kind);
		Assert.AreEqual("broken.txt".As<RelativeFilePath>(), entries[0].Path);
	}

	[TestMethod]
	public void ReturnsAnEmptyListWhenNothingChanged()
	{
		Assert.AreEqual(0, GitDiffParser.Parse(string.Empty).Count);
	}

	[TestMethod]
	public void RejectsAStatusWithNoFollowingPath()
	{
		Assert.ThrowsExactly<GitParseException>(() => GitDiffParser.Parse("M"));
	}

	[TestMethod]
	public void RejectsARenameWithNoDestinationPath()
	{
		Assert.ThrowsExactly<GitParseException>(() => GitDiffParser.Parse("R100" + Nul + "only-source.txt"));
	}

	[TestMethod]
	public void ParsesTheRawAndNumstatSectionsRunTogether()
	{
		// Captured verbatim from git 2.43. The two sections are emitted back to back with no
		// delimiter: the raw section's last path token ("n.txt") runs straight into the numstat
		// section's first record ("-\t-\tb.bin"). Telling them apart is the whole job of this parser.
		string output =
			":100644 100644 366fd40 fbeb5f4 M\0b.bin\0" +
			":100644 000000 abaddc0 0000000 D\0d.txt\0" +
			":100644 100644 de98044 d68dd40 R075\0f.txt\0g.txt\0" +
			":000000 100644 0000000 8ba3a16 A\0n.txt\0" +
			"-\t-\tb.bin\0" +
			"0\t1\td.txt\0" +
			"1\t0\t\0f.txt\0g.txt\0" +
			"1\t0\tn.txt\0";

		IReadOnlyList<GitDiffEntry> entries = GitDiffParser.ParseWithLineCounts(output);

		Assert.AreEqual(4, entries.Count);

		// A binary file: git prints "-" for both counts rather than measuring it, so the counts stay
		// null. Reporting 0 would state that nothing changed in a file git declined to measure.
		Assert.AreEqual(GitChangeKind.Modified, entries[0].Kind);
		Assert.AreEqual("b.bin", entries[0].Path.WeakString);
		Assert.IsNull(entries[0].Insertions);
		Assert.IsNull(entries[0].Deletions);

		Assert.AreEqual(GitChangeKind.Deleted, entries[1].Kind);
		Assert.AreEqual(0, entries[1].Insertions);
		Assert.AreEqual(1, entries[1].Deletions);

		// A rename is spelled differently in each section — two path tokens after the status in the
		// raw section, an empty path field then two tokens in the numstat one — which is why
		// correlation is positional rather than by path.
		Assert.AreEqual(GitChangeKind.Renamed, entries[2].Kind);
		Assert.AreEqual("g.txt", entries[2].Path.WeakString);
		Assert.AreEqual("f.txt", entries[2].OriginalPath?.WeakString);
		Assert.AreEqual(75, entries[2].SimilarityPercent);
		Assert.AreEqual(1, entries[2].Insertions);
		Assert.AreEqual(0, entries[2].Deletions);

		Assert.AreEqual(GitChangeKind.Added, entries[3].Kind);
		Assert.AreEqual(1, entries[3].Insertions);
		Assert.AreEqual(0, entries[3].Deletions);
	}

	[TestMethod]
	public void ReadsTheStatusFromTheEndOfARawHeader()
	{
		// The status is the last space-separated field of ":<oldmode> <newmode> <oldsha> <newsha>
		// <status>". Taken from the end rather than by field index, so a future git adding a field
		// ahead of it would not shift what is read.
		string output = ":100644 100755 8ba3a16 8ba3a16 M\0n.txt\0" + "0\t0\tn.txt\0";

		IReadOnlyList<GitDiffEntry> entries = GitDiffParser.ParseWithLineCounts(output);

		Assert.AreEqual(1, entries.Count);
		Assert.AreEqual(GitChangeKind.Modified, entries[0].Kind);

		// A mode-only change really is zero lines either way, which is a different statement from a
		// binary file's null.
		Assert.AreEqual(0, entries[0].Insertions);
		Assert.AreEqual(0, entries[0].Deletions);
	}

	[TestMethod]
	public void ReadsAPathBeginningWithAColonWithoutMistakingItForARawHeader()
	{
		// The section test looks at a record's first token only, and a path is consumed by the record
		// that owns it, so a file named ":weird" never reaches that check. POSIX permits such a name.
		string output = ":000000 100644 0000000 8ba3a16 A\0:weird\0" + "2\t0\t:weird\0";

		IReadOnlyList<GitDiffEntry> entries = GitDiffParser.ParseWithLineCounts(output);

		Assert.AreEqual(1, entries.Count);
		Assert.AreEqual(":weird", entries[0].Path.WeakString);
		Assert.AreEqual(2, entries[0].Insertions);
	}

	[TestMethod]
	public void ReportsAnEmptyListForAnEmptyDiff()
	{
		Assert.AreEqual(0, GitDiffParser.ParseWithLineCounts(string.Empty).Count);
	}

	[TestMethod]
	public void ThrowsWhenTheTwoSectionsDisagreeOnHowManyChangesThereAre()
	{
		// One numstat record per raw record is what git emits, so a mismatch means the section
		// boundary was read wrongly rather than that a count is genuinely missing. Reporting nulls
		// would present a parser bug as an absent measurement.
		string output =
			":100644 000000 abaddc0 0000000 D\0d.txt\0" +
			":000000 100644 0000000 8ba3a16 A\0n.txt\0" +
			"0\t1\td.txt\0";

		_ = Assert.ThrowsExactly<GitParseException>(() => _ = GitDiffParser.ParseWithLineCounts(output));
	}

	[TestMethod]
	public void ThrowsForAMalformedRawHeader()
	{
		_ = Assert.ThrowsExactly<GitParseException>(
			() => _ = GitDiffParser.ParseWithLineCounts(":nospaces\0d.txt\0" + "0\t1\td.txt\0"));
	}

	[TestMethod]
	public void ThrowsForAMalformedNumstatRecord()
	{
		_ = Assert.ThrowsExactly<GitParseException>(
			() => _ = GitDiffParser.ParseWithLineCounts(":100644 000000 abaddc0 0000000 D\0d.txt\0" + "0 1 d.txt\0"));
	}

	[TestMethod]
	public void ThrowsForANumstatCountThatIsNeitherANumberNorADash()
	{
		_ = Assert.ThrowsExactly<GitParseException>(
			() => _ = GitDiffParser.ParseWithLineCounts(":100644 000000 abaddc0 0000000 D\0d.txt\0" + "x\t1\td.txt\0"));
	}
}
