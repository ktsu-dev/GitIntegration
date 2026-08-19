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
}
