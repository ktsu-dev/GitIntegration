// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using ktsu.Semantics.Strings;

[TestClass]
public class GitFetchParserTests
{
	private const string OldSha = "7c85857fc85452150652c289fc94f1f912b96c40";
	private const string NewSha = "6702dd1957dedc4e0f54245bda65f3ddc5f37e64";

	private static string Record(string flag, string oldSha, string newSha, string reference) =>
		flag + " " + oldSha + " " + newSha + " " + reference + "\n";

	[TestMethod]
	public void ReadsAFastForward()
	{
		// The flag for a fast-forward is a space, so the captured line begins with two spaces —
		// the flag, then the separator. Trimming the line before splitting loses the flag.
		GitFetchResult result = GitFetchParser.Parse(
			Record(" ", OldSha, NewSha, "refs/remotes/origin/main"));

		Assert.AreEqual(1, result.Updates.Count);

		GitRefUpdate update = result.Updates[0];
		Assert.AreEqual(GitRefUpdateKind.FastForward, update.Kind);
		Assert.AreEqual("refs/remotes/origin/main".As<GitRefName>(), update.Reference);
		Assert.AreEqual(OldSha.As<GitCommitSha>(), update.OldSha);
		Assert.AreEqual(NewSha.As<GitCommitSha>(), update.NewSha);
		Assert.IsNull(update.Source);
		Assert.AreEqual(string.Empty, update.Summary);
	}

	[TestMethod]
	public void MapsEveryFlagGitDocuments()
	{
		string output =
			Record(" ", OldSha, NewSha, "refs/remotes/origin/ff") +
			Record("+", OldSha, NewSha, "refs/remotes/origin/forced") +
			Record("-", OldSha, NewSha, "refs/remotes/origin/pruned") +
			Record("*", OldSha, NewSha, "refs/remotes/origin/created") +
			Record("!", OldSha, NewSha, "refs/remotes/origin/rejected") +
			Record("=", OldSha, NewSha, "refs/remotes/origin/same") +
			Record("t", OldSha, NewSha, "refs/tags/v1");

		GitFetchResult result = GitFetchParser.Parse(output);

		Assert.AreEqual(GitRefUpdateKind.FastForward, result.Updates[0].Kind);
		Assert.AreEqual(GitRefUpdateKind.Forced, result.Updates[1].Kind);
		Assert.AreEqual(GitRefUpdateKind.Removed, result.Updates[2].Kind);
		Assert.AreEqual(GitRefUpdateKind.Created, result.Updates[3].Kind);
		Assert.AreEqual(GitRefUpdateKind.Rejected, result.Updates[4].Kind);
		Assert.AreEqual(GitRefUpdateKind.UpToDate, result.Updates[5].Kind);
		Assert.AreEqual(GitRefUpdateKind.TagUpdate, result.Updates[6].Kind);
	}

	[TestMethod]
	public void ReportsDetailAsAvailable()
	{
		// This parser only ever runs when git supported --porcelain, so anything it produces is a
		// real account. The false case is set by the builder, not here.
		GitFetchResult result = GitFetchParser.Parse(
			Record(" ", OldSha, NewSha, "refs/remotes/origin/main"));

		Assert.IsTrue(result.DetailAvailable);
	}

	[TestMethod]
	public void TreatsNoOutputAsUpToDate()
	{
		// git prints nothing at all when a fetch changed nothing, and exits 0.
		GitFetchResult result = GitFetchParser.Parse(string.Empty);

		Assert.AreEqual(0, result.Updates.Count);
		Assert.IsTrue(result.IsUpToDate);
	}

	[TestMethod]
	public void ToleratesCarriageReturnLineEndings()
	{
		GitFetchResult result = GitFetchParser.Parse(
			" " + OldSha + " " + NewSha + " refs/remotes/origin/main\r\n");

		Assert.AreEqual("refs/remotes/origin/main".As<GitRefName>(), result.Updates[0].Reference);
	}

	[TestMethod]
	public void ReportsAnUnknownFlagWithoutThrowing()
	{
		GitFetchResult result = GitFetchParser.Parse(
			Record("?", OldSha, NewSha, "refs/remotes/origin/main"));

		Assert.AreEqual(GitRefUpdateKind.Unknown, result.Updates[0].Kind);
	}

	[TestMethod]
	public void RejectsARecordWithTooFewFields() =>
		Assert.ThrowsExactly<GitParseException>(() => GitFetchParser.Parse(" " + OldSha + " " + NewSha + "\n"));

	[TestMethod]
	public void RejectsARecordWhoseObjectIdIsNotValid() =>
		Assert.ThrowsExactly<GitParseException>(
			() => GitFetchParser.Parse(Record(" ", "not-a-sha", NewSha, "refs/remotes/origin/main")));
}
