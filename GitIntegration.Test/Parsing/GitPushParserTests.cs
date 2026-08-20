// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using ktsu.Semantics.Strings;

[TestClass]
public class GitPushParserTests
{
	private const string Tab = "\t";
	private const string To = "To C:/dev/origin.git\n";
	private const string Done = "Done\n";

	private static string Record(string flag, string refs, string summary) =>
		flag + Tab + refs + Tab + summary + "\n";

	[TestMethod]
	public void ReadsANewBranch()
	{
		GitPushResult result = GitPushParser.Parse(
			To + Record("*", "refs/heads/main:refs/heads/main", "[new branch]") + Done);

		Assert.AreEqual(1, result.Updates.Count);
		Assert.AreEqual(GitRefUpdateKind.Created, result.Updates[0].Kind);
		Assert.AreEqual("refs/heads/main".As<GitRefName>(), result.Updates[0].Reference);
		Assert.AreEqual("refs/heads/main".As<GitRefName>(), result.Updates[0].Source);
		Assert.AreEqual("[new branch]", result.Updates[0].Summary);
		Assert.IsFalse(result.HasRejections);
	}

	[TestMethod]
	public void ReadsAFastForwardAndItsShaRange()
	{
		// A space is a real flag value, not padding, so the record cannot be trimmed before
		// splitting or the flag disappears.
		GitPushResult result = GitPushParser.Parse(
			To + Record(" ", "refs/heads/main:refs/heads/main", "66afe49..7c85857") + Done);

		GitRefUpdate update = result.Updates[0];
		Assert.AreEqual(GitRefUpdateKind.FastForward, update.Kind);
		Assert.AreEqual("66afe49".As<GitCommitSha>(), update.OldSha);
		Assert.AreEqual("7c85857".As<GitCommitSha>(), update.NewSha);
	}

	[TestMethod]
	public void ReadsAForcedUpdateAndItsThreeDotShaRange()
	{
		// Captured from git 2.50.1: a non-fast-forward update separates the ids with THREE dots and
		// appends " (forced update)". Reading two dots and taking the rest of the summary left the
		// trailing id as ".e60966f (forced update)", which is not a valid object id, so both ids
		// came back null on exactly the push where knowing what was overwritten matters most.
		GitPushResult result = GitPushParser.Parse(
			To + Record("+", "refs/heads/main:refs/heads/main", "8fabc01...e60966f (forced update)") + Done);

		GitRefUpdate update = result.Updates[0];
		Assert.AreEqual(GitRefUpdateKind.Forced, update.Kind);
		Assert.AreEqual("8fabc01".As<GitCommitSha>(), update.OldSha);
		Assert.AreEqual("e60966f".As<GitCommitSha>(), update.NewSha);
	}

	[TestMethod]
	public void ReadsAnUpToDateRefWithNoShaRange()
	{
		GitPushResult result = GitPushParser.Parse(
			To + Record("=", "refs/heads/main:refs/heads/main", "[up to date]") + Done);

		Assert.AreEqual(GitRefUpdateKind.UpToDate, result.Updates[0].Kind);
		Assert.IsNull(result.Updates[0].OldSha);
		Assert.IsNull(result.Updates[0].NewSha);
	}

	[TestMethod]
	public void ReadsARejectionAndReportsItOnTheResult()
	{
		GitPushResult result = GitPushParser.Parse(
			To + Record("!", "refs/heads/main:refs/heads/main", "[rejected] (fetch first)") + Done);

		Assert.AreEqual(GitRefUpdateKind.Rejected, result.Updates[0].Kind);
		Assert.IsTrue(result.Updates[0].IsRejected);
		Assert.IsTrue(result.HasRejections);
		Assert.AreEqual("[rejected] (fetch first)", result.Updates[0].Summary);
	}

	[TestMethod]
	public void ReadsADeletionWhoseLocalSideIsEmpty()
	{
		// git writes ":refs/heads/gone" for a delete — nothing is being sent, so the local side is
		// blank. Splitting on the colon and requiring both halves would throw here.
		GitPushResult result = GitPushParser.Parse(
			To + Record("-", ":refs/heads/gone", "[deleted]") + Done);

		GitRefUpdate update = result.Updates[0];
		Assert.AreEqual(GitRefUpdateKind.Removed, update.Kind);
		Assert.AreEqual("refs/heads/gone".As<GitRefName>(), update.Reference);
		Assert.IsNull(update.Source);
	}

	[TestMethod]
	public void ReadsSeveralRecordsInOrder()
	{
		GitPushResult result = GitPushParser.Parse(
			To +
			Record("*", "refs/heads/a:refs/heads/a", "[new branch]") +
			Record("!", "refs/heads/b:refs/heads/b", "[rejected] (non-fast-forward)") +
			Done);

		Assert.AreEqual(2, result.Updates.Count);
		Assert.AreEqual("refs/heads/a".As<GitRefName>(), result.Updates[0].Reference);
		Assert.AreEqual("refs/heads/b".As<GitRefName>(), result.Updates[1].Reference);
		Assert.IsTrue(result.HasRejections);
	}

	[TestMethod]
	public void SkipsTheNonRecordLinesGitPrints()
	{
		// The "To <url>" header, the trailing "Done", and the tracking notice a -u push emits are
		// all on standard output alongside the records.
		GitPushResult result = GitPushParser.Parse(
			To +
			Record("*", "refs/heads/main:refs/heads/main", "[new branch]") +
			"branch 'main' set up to track 'origin/main'.\n" +
			Done);

		Assert.AreEqual(1, result.Updates.Count);
	}

	[TestMethod]
	public void ToleratesCarriageReturnLineEndings()
	{
		GitPushResult result = GitPushParser.Parse(
			"To C:/dev/origin.git\r\n*\trefs/heads/main:refs/heads/main\t[new branch]\r\nDone\r\n");

		Assert.AreEqual(1, result.Updates.Count);
		Assert.AreEqual("[new branch]", result.Updates[0].Summary);
	}

	[TestMethod]
	public void ReportsAnUnknownFlagWithoutThrowing()
	{
		// Unlike the status codes, git's push flags are not a closed set this library can rely on
		// never growing, and failing an entire push report over one character would be worse than
		// naming the reference with an unknown kind.
		GitPushResult result = GitPushParser.Parse(
			To + Record("?", "refs/heads/main:refs/heads/main", "[something new]") + Done);

		Assert.AreEqual(GitRefUpdateKind.Unknown, result.Updates[0].Kind);
	}

	[TestMethod]
	public void ReturnsNoUpdatesForEmptyOutput() =>
		Assert.AreEqual(0, GitPushParser.Parse(string.Empty).Updates.Count);

	[TestMethod]
	public void RejectsARecordWithTooFewFields() =>
		Assert.ThrowsExactly<GitParseException>(
			() => GitPushParser.Parse(To + "*\trefs/heads/main:refs/heads/main\n" + Done));

	[TestMethod]
	public void RejectsARecordWhoseRefsFieldHasNoColon() =>
		Assert.ThrowsExactly<GitParseException>(
			() => GitPushParser.Parse(To + Record("*", "refs/heads/main", "[new branch]") + Done));
}
