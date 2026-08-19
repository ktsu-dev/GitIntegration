// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;

using ktsu.Semantics.Strings;

[TestClass]
public class GitLogParserTests
{
	private const string Nul = "\u0000";
	private const string Us = "\u001f";

	private const string FirstSha = "9429d2063d91f1097de51a196cb8203b06335738";
	private const string SecondSha = "94947d6da5c05bf1c86af335b33cff8cee83cb3f";
	private const string FirstTree = "f3758b7757b1f9bfe8c8e05fc5ac51bf3650c7d5";
	private const string SecondTree = "50997ba19ef5248ac46e7ec0992ec0b07d7c8f8b";
	private const string MergedInSha = "6f8b1c2d3e4f5a6b7c8d9e0f1a2b3c4d5e6f7a8b";

	// Captured verbatim from a real repository. The second record is the root commit, so its
	// parents field is empty, and it carries a two-paragraph body ending in the newline %b appends.
	private const string TwoCommits =
		FirstSha + Us + FirstTree + Us + SecondSha + Us +
		"Fixture Author" + Us + "fixture@example.com" + Us + "2026-08-20T00:05:20+10:00" + Us +
		"Fixture Author" + Us + "fixture@example.com" + Us + "2026-08-20T00:05:20+10:00" + Us +
		"Second commit" + Us + Nul +
		SecondSha + Us + SecondTree + Us + Us +
		"Fixture Author" + Us + "fixture@example.com" + Us + "2026-08-20T00:04:59+10:00" + Us +
		"Fixture Author" + Us + "fixture@example.com" + Us + "2026-08-20T00:04:59+10:00" + Us +
		"Initial commit" + Us + "A body line.\n\nAnd a second paragraph.\n" + Nul;

	private const string MergeCommit =
		FirstSha + Us + FirstTree + Us + SecondSha + " " + MergedInSha + Us +
		"Fixture Author" + Us + "fixture@example.com" + Us + "2026-08-20T00:05:20+10:00" + Us +
		"Other Committer" + Us + "other@example.com" + Us + "2026-08-21T09:00:00-04:00" + Us +
		"Merge branch 'feature/x'" + Us + Nul;

	[TestMethod]
	public void ReadsEveryCommitInOrder()
	{
		IReadOnlyList<GitCommit> commits = GitLogParser.Parse(TwoCommits);

		Assert.AreEqual(2, commits.Count);
		Assert.AreEqual(FirstSha.As<GitCommitSha>(), commits[0].Sha);
		Assert.AreEqual(SecondSha.As<GitCommitSha>(), commits[1].Sha);
	}

	[TestMethod]
	public void ReadsTheTreeAndParentShas()
	{
		IReadOnlyList<GitCommit> commits = GitLogParser.Parse(TwoCommits);

		Assert.AreEqual(FirstTree.As<GitCommitSha>(), commits[0].TreeSha);
		CollectionAssert.AreEqual(
			new[] { SecondSha.As<GitCommitSha>() },
			commits[0].ParentShas.ToArray());
	}

	[TestMethod]
	public void ReportsARootCommitAsHavingNoParents()
	{
		IReadOnlyList<GitCommit> commits = GitLogParser.Parse(TwoCommits);

		Assert.AreEqual(0, commits[1].ParentShas.Count);
	}

	[TestMethod]
	public void ReadsBothParentsOfAMergeCommit()
	{
		IReadOnlyList<GitCommit> commits = GitLogParser.Parse(MergeCommit);

		Assert.AreEqual(2, commits[0].ParentShas.Count);
		Assert.AreEqual(SecondSha.As<GitCommitSha>(), commits[0].ParentShas[0]);
		Assert.AreEqual(MergedInSha.As<GitCommitSha>(), commits[0].ParentShas[1]);
	}

	[TestMethod]
	public void ReadsTheAuthorAndCommitterSeparately()
	{
		IReadOnlyList<GitCommit> commits = GitLogParser.Parse(MergeCommit);

		Assert.AreEqual("Fixture Author".As<GitAuthorName>(), commits[0].Author.Name);
		Assert.AreEqual("fixture@example.com".As<GitAuthorEmail>(), commits[0].Author.Email);
		Assert.AreEqual("Other Committer".As<GitAuthorName>(), commits[0].Committer.Name);
		Assert.AreEqual("other@example.com".As<GitAuthorEmail>(), commits[0].Committer.Email);
	}

	[TestMethod]
	public void PreservesTheCommittedTimeZoneOffset()
	{
		// %aI is strict ISO-8601 with the offset the commit was made in. Normalising to UTC would
		// throw away the local time, which is information a caller may want.
		IReadOnlyList<GitCommit> commits = GitLogParser.Parse(MergeCommit);

		Assert.AreEqual(TimeSpan.FromHours(10), commits[0].Author.Timestamp.Offset);
		Assert.AreEqual(TimeSpan.FromHours(-4), commits[0].Committer.Timestamp.Offset);
		Assert.AreEqual(
			new DateTimeOffset(2026, 8, 20, 0, 5, 20, TimeSpan.FromHours(10)),
			commits[0].Author.Timestamp);
	}

	[TestMethod]
	public void ReadsAMultiLineBodyWithoutSplittingTheRecord()
	{
		// -z is what makes this work: the blank line inside the body would otherwise be
		// indistinguishable from a record boundary in a newline-delimited format.
		IReadOnlyList<GitCommit> commits = GitLogParser.Parse(TwoCommits);

		Assert.AreEqual("Initial commit", commits[1].Subject);
		Assert.AreEqual("A body line.\n\nAnd a second paragraph.", commits[1].Body);
	}

	[TestMethod]
	public void ReportsAnEmptyBodyAsAnEmptyString()
	{
		IReadOnlyList<GitCommit> commits = GitLogParser.Parse(TwoCommits);

		Assert.AreEqual(string.Empty, commits[0].Body);
	}

	[TestMethod]
	public void ReturnsAnEmptyListForNoOutput()
	{
		// An empty repository, or a log filtered down to nothing, produces no output at all.
		Assert.AreEqual(0, GitLogParser.Parse(string.Empty).Count);
	}

	[TestMethod]
	public void RejectsARecordWithTooFewFields()
	{
		Assert.ThrowsExactly<GitParseException>(
			() => GitLogParser.Parse(FirstSha + Us + FirstTree + Nul));
	}

	[TestMethod]
	public void RejectsAnUnparseableTimestamp()
	{
		string broken = TwoCommits.Replace("2026-08-20T00:05:20+10:00", "not-a-date", StringComparison.Ordinal);

		Assert.ThrowsExactly<GitParseException>(() => GitLogParser.Parse(broken));
	}
}
