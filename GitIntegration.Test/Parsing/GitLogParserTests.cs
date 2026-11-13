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

	// Shaped as git emits LogFormat for a real repository. The second record is the root commit, so
	// its parents field is empty, and it carries a two-paragraph body ending in the newline %b appends.
	private const string TwoCommits =
		FirstSha + Us + FirstTree + Us + SecondSha + Us +
		"2026-08-20T00:05:20+10:00" + Us + "2026-08-20T00:05:20+10:00" + Us +
		"Fixture Author<fixture@example.com>Fixture Author<fixture@example.com>" +
		"Second commit\n" + Nul +
		SecondSha + Us + SecondTree + Us + Us +
		"2026-08-20T00:04:59+10:00" + Us + "2026-08-20T00:04:59+10:00" + Us +
		"Fixture Author<fixture@example.com>Fixture Author<fixture@example.com>" +
		"Initial commit\n" + "A body line.\n\nAnd a second paragraph.\n" + Nul;

	private const string MergeCommit =
		FirstSha + Us + FirstTree + Us + SecondSha + " " + MergedInSha + Us +
		"2026-08-20T00:05:20+10:00" + Us + "2026-08-21T09:00:00-04:00" + Us +
		"Fixture Author<fixture@example.com>Other Committer<other@example.com>" +
		"Merge branch 'feature/x'\n" + Nul;

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
	public void ReadsAnEmptySignatureEmailAsNull()
	{
		// A commit made with an empty user.email records "<>", which %ae prints as nothing. One such
		// commit must not make the whole history unreadable.
		string record =
			FirstSha + Us + FirstTree + Us + Us +
			"2026-08-20T00:05:20+10:00" + Us + "2026-08-21T09:00:00-04:00" + Us +
			"Fixture Author<>Other Committer<>" +
			"Imported commit\n" + Nul;

		IReadOnlyList<GitCommit> commits = GitLogParser.Parse(record);

		Assert.AreEqual(1, commits.Count);
		Assert.AreEqual("Fixture Author".As<GitAuthorName>(), commits[0].Author.Name);
		Assert.IsNull(commits[0].Author.Email);
		Assert.AreEqual("Other Committer".As<GitAuthorName>(), commits[0].Committer.Name);
		Assert.IsNull(commits[0].Committer.Email);
	}

	[TestMethod]
	public void ReadsAnEmptySignatureNameAsNull()
	{
		// git commit refuses an empty name, but commit-tree and fast-import will still write one.
		string record =
			FirstSha + Us + FirstTree + Us + Us +
			"2026-08-20T00:05:20+10:00" + Us + "2026-08-21T09:00:00-04:00" + Us +
			"<fixture@example.com><other@example.com>" +
			"Imported commit\n" + Nul;

		IReadOnlyList<GitCommit> commits = GitLogParser.Parse(record);

		Assert.AreEqual(1, commits.Count);
		Assert.IsNull(commits[0].Author.Name);
		Assert.AreEqual("fixture@example.com".As<GitAuthorEmail>(), commits[0].Author.Email);
		Assert.IsNull(commits[0].Committer.Name);
		Assert.AreEqual("other@example.com".As<GitAuthorEmail>(), commits[0].Committer.Email);
	}

	[TestMethod]
	public void PreservesTheCommittedTimeZoneOffset()
	{
		// %aI is strict ISO-8601 with the offset the commit was made in. Normalizing to UTC would
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
	[DataRow("2023-11-15T13:13:20+15:00", DisplayName = "+15:00")]
	[DataRow("2023-11-14T07:13:20-15:00", DisplayName = "-15:00")]
	[DataRow("2023-11-14T22:13:20+00:00", DisplayName = "In range, for comparison")]
	public void ReadsATimestampWithAnOffsetBeyondFourteenHours(string timestamp)
	{
		// git accepts any ±hhmm zone (GIT_AUTHOR_DATE="1700000000 +1500"), and DateTimeOffset only
		// holds ±14:00. One such commit must not fail the whole log (#262).
		string record = MergeCommit
			.Replace("2026-08-20T00:05:20+10:00", timestamp, StringComparison.Ordinal)
			.Replace("2026-08-21T09:00:00-04:00", timestamp, StringComparison.Ordinal);

		IReadOnlyList<GitCommit> commits = GitLogParser.Parse(record);

		DateTime expected = DateTimeOffset.FromUnixTimeSeconds(1700000000).UtcDateTime;
		Assert.AreEqual(expected, commits[0].Author.Timestamp.UtcDateTime);
		Assert.AreEqual(expected, commits[0].Committer.Timestamp.UtcDateTime);
	}

	[TestMethod]
	[DataRow("2023-11-14T23:52:20+00:99", DisplayName = "+00:99")]
	[DataRow("2023-11-15T11:13:20+12:60", DisplayName = "+12:60")]
	[DataRow("2023-11-14T09:13:20-12:60", DisplayName = "-12:60")]
	public void ReadsATimestampWhoseOffsetMinutesAreSixtyOrMore(string timestamp)
	{
		// git reads a "+0099" zone as 99 minutes and prints it unchanged as "+00:99", which
		// DateTimeOffset refuses. One such commit must not fail the whole log (#287).
		string record = MergeCommit
			.Replace("2026-08-20T00:05:20+10:00", timestamp, StringComparison.Ordinal)
			.Replace("2026-08-21T09:00:00-04:00", timestamp, StringComparison.Ordinal);

		IReadOnlyList<GitCommit> commits = GitLogParser.Parse(record);

		DateTime expected = DateTimeOffset.FromUnixTimeSeconds(1700000000).UtcDateTime;
		Assert.AreEqual(expected, commits[0].Author.Timestamp.UtcDateTime);
		Assert.AreEqual(expected, commits[0].Committer.Timestamp.UtcDateTime);
	}

	[TestMethod]
	[DataRow("2023-11-15T13:13:20+15:0x", DisplayName = "Malformed offset")]
	[DataRow("2023-13-15T13:13:20+15:00", DisplayName = "Invalid month")]
	[DataRow("+15:00", DisplayName = "Offset only")]
	public void RejectsAMalformedTimestampWithAnOutOfRangeOffset(string timestamp)
	{
		string broken = TwoCommits.Replace("2026-08-20T00:05:20+10:00", timestamp, StringComparison.Ordinal);

		Assert.ThrowsExactly<GitParseException>(() => GitLogParser.Parse(broken));
	}

	[TestMethod]
	public void RejectsAnUnparseableTimestamp()
	{
		string broken = TwoCommits.Replace("2026-08-20T00:05:20+10:00", "not-a-date", StringComparison.Ordinal);

		Assert.ThrowsExactly<GitParseException>(() => GitLogParser.Parse(broken));
	}

	[TestMethod]
	public void ReadsABodyContainingTheUnitSeparator()
	{
		string bodyWithSeparator = "body before" + Us + "body after";
		string record = Record("Fixture Author", "fixture@example.com", "Subject line", bodyWithSeparator + "\n");

		IReadOnlyList<GitCommit> commits = GitLogParser.Parse(record);

		Assert.AreEqual("Subject line", commits[0].Subject);
		Assert.AreEqual(bodyWithSeparator, commits[0].Body);
	}

	[TestMethod]
	public void ReadsASubjectContainingTheUnitSeparator()
	{
		// git commit -m $'sub\x1fject' keeps the byte in %s (#196).
		string subject = "sub" + Us + "ject";
		string record = Record("Fixture Author", "fixture@example.com", subject, "body\n");

		IReadOnlyList<GitCommit> commits = GitLogParser.Parse(record);

		Assert.AreEqual(subject, commits[0].Subject);
		Assert.AreEqual("body", commits[0].Body);
	}

	[TestMethod]
	public void ReadsSignatureNamesAndEmailsContainingTheUnitSeparator()
	{
		// git -c user.name=$'A\x1fB' commit is accepted and %an prints the byte. Before #196 it shifted
		// every later field and failed the whole log.
		string name = "A" + Us + "B";
		string email = "a" + Us + "b@example.com";
		string record = Record(name, email, "Subject", string.Empty);

		IReadOnlyList<GitCommit> commits = GitLogParser.Parse(record);

		Assert.AreEqual(name, commits[0].Author.Name?.WeakString);
		Assert.AreEqual(email, commits[0].Author.Email?.WeakString);
		Assert.AreEqual(name, commits[0].Committer.Name?.WeakString);
		Assert.AreEqual(email, commits[0].Committer.Email?.WeakString);
		Assert.AreEqual("Subject", commits[0].Subject);
		Assert.AreEqual(string.Empty, commits[0].Body);
	}

	[TestMethod]
	public void ReadsANameContainingAClosingAngleBracket()
	{
		// git reads a name as everything before the first '<', so an imported name may hold a '>'.
		string record = Record("A>B", "a@example.com", "Subject", string.Empty);

		IReadOnlyList<GitCommit> commits = GitLogParser.Parse(record);

		Assert.AreEqual("A>B", commits[0].Author.Name?.WeakString);
		Assert.AreEqual("a@example.com", commits[0].Author.Email?.WeakString);
	}

	[TestMethod]
	public void RejectsARecordWhoseTextFieldsAreTruncated()
	{
		string broken = FirstSha + Us + FirstTree + Us + Us +
			"2026-08-20T00:05:20+10:00" + Us + "2026-08-20T00:05:20+10:00" + Us +
			"Fixture Author<fixture@example.com>" + Nul;

		Assert.ThrowsExactly<GitParseException>(() => GitLogParser.Parse(broken));
	}

	private static string Record(string name, string email, string subject, string body) =>
		FirstSha + Us + FirstTree + Us + Us +
		"2026-08-20T00:05:20+10:00" + Us + "2026-08-20T00:05:20+10:00" + Us +
		name + "<" + email + ">" + name + "<" + email + ">" +
		subject + "\n" + body + Nul;
}
