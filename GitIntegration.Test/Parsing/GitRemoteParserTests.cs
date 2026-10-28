// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Collections.Generic;
using System.Linq;

using ktsu.Semantics.Strings;

[TestClass]
public class GitRemoteParserTests
{
	private const string Tab = "\t";

	private const string SingleRemote =
		"origin" + Tab + "https://github.com/ktsu-dev/GitIntegration.git (fetch)\n" +
		"origin" + Tab + "https://github.com/ktsu-dev/GitIntegration.git (push)\n";

	private const string TwoRemotes =
		"origin" + Tab + "https://github.com/ktsu-dev/GitIntegration.git (fetch)\n" +
		"origin" + Tab + "https://github.com/ktsu-dev/GitIntegration.git (push)\n" +
		"upstream" + Tab + "git@github.com:someone/GitIntegration.git (fetch)\n" +
		"upstream" + Tab + "git@github.com:someone/GitIntegration.git (push)\n";

	private const string SeparatePushUrl =
		"origin" + Tab + "https://github.com/ktsu-dev/GitIntegration.git (fetch)\n" +
		"origin" + Tab + "git@github.com:ktsu-dev/GitIntegration.git (push)\n";

	// A local remote is a filesystem path, which may contain spaces, which is why the parser
	// anchors on the trailing marker rather than on the last space.
	private const string LocalPathWithSpaces =
		"origin" + Tab + "C:/dev/my repos/upstream.git (fetch)\n" +
		"origin" + Tab + "C:/dev/my repos/upstream.git (push)\n";

	[TestMethod]
	public void ReadsARemoteWithMatchingFetchAndPushUrls()
	{
		IReadOnlyList<GitRemote> remotes = GitRemoteParser.Parse(SingleRemote);

		Assert.AreEqual(1, remotes.Count);
		Assert.AreEqual("origin".As<GitRemoteName>(), remotes[0].Name);
		Assert.AreEqual(
			"https://github.com/ktsu-dev/GitIntegration.git".As<GitRepositoryRemotePath>(),
			remotes[0].FetchUrl);
		Assert.AreEqual(remotes[0].FetchUrl, remotes[0].PushUrl);
	}

	[TestMethod]
	public void CollapsesTheFetchAndPushLinesIntoOneRemote()
	{
		IReadOnlyList<GitRemote> remotes = GitRemoteParser.Parse(TwoRemotes);

		Assert.AreEqual(2, remotes.Count);
		Assert.AreEqual("origin".As<GitRemoteName>(), remotes[0].Name);
		Assert.AreEqual("upstream".As<GitRemoteName>(), remotes[1].Name);
	}

	[TestMethod]
	public void KeepsAPushUrlThatDiffersFromTheFetchUrl()
	{
		IReadOnlyList<GitRemote> remotes = GitRemoteParser.Parse(SeparatePushUrl);

		Assert.AreEqual(
			"https://github.com/ktsu-dev/GitIntegration.git".As<GitRepositoryRemotePath>(),
			remotes[0].FetchUrl);
		Assert.AreEqual(
			"git@github.com:ktsu-dev/GitIntegration.git".As<GitRepositoryRemotePath>(),
			remotes[0].PushUrl);
	}

	[TestMethod]
	public void KeepsEveryPushUrlInGitsOrder()
	{
		// One (push) line per remote.origin.pushurl; git push sends to all of them.
		IReadOnlyList<GitRemote> remotes = GitRemoteParser.Parse(
			"origin" + Tab + "https://example.com/a.git (fetch)\n" +
			"origin" + Tab + "https://example.com/p1.git (push)\n" +
			"origin" + Tab + "https://example.com/p2.git (push)\n");

		Assert.AreEqual(1, remotes.Count);
		Assert.AreEqual("https://example.com/p1.git".As<GitRepositoryRemotePath>(), remotes[0].PushUrl);
		CollectionAssert.AreEqual(
			new[]
			{
				"https://example.com/p1.git".As<GitRepositoryRemotePath>(),
				"https://example.com/p2.git".As<GitRepositoryRemotePath>(),
			},
			remotes[0].PushUrls.ToArray());
	}

	[TestMethod]
	public void ReportsTheFetchUrlAsTheOnlyPushUrlWhenNoPushUrlIsConfigured()
	{
		IReadOnlyList<GitRemote> remotes = GitRemoteParser.Parse(SingleRemote);

		CollectionAssert.AreEqual(new[] { remotes[0].FetchUrl }, remotes[0].PushUrls.ToArray());
	}

	[TestMethod]
	public void DefaultsPushUrlsToPushUrlWhenConstructedWithoutThem()
	{
		GitRemote remote = new()
		{
			Name = "origin".As<GitRemoteName>(),
			FetchUrl = "https://example.com/a.git".As<GitRepositoryRemotePath>(),
			PushUrl = "https://example.com/p.git".As<GitRepositoryRemotePath>(),
		};

		CollectionAssert.AreEqual(new[] { remote.PushUrl }, remote.PushUrls.ToArray());
	}

	[TestMethod]
	public void ReadsALocalPathContainingSpaces()
	{
		IReadOnlyList<GitRemote> remotes = GitRemoteParser.Parse(LocalPathWithSpaces);

		Assert.AreEqual(
			"C:/dev/my repos/upstream.git".As<GitRepositoryRemotePath>(),
			remotes[0].FetchUrl);
	}

	[TestMethod]
	public void ReturnsAnEmptyListWhenThereAreNoRemotes()
	{
		Assert.AreEqual(0, GitRemoteParser.Parse(string.Empty).Count);
	}

	[TestMethod]
	public void SkipsARemoteSectionWithNoUrl()
	{
		IReadOnlyList<GitRemote> remotes = GitRemoteParser.Parse(
			"origin" + Tab + "\n" +
			"upstream" + Tab + "https://x (fetch)\n" +
			"upstream" + Tab + "https://x (push)\n");

		Assert.AreEqual(1, remotes.Count);
		Assert.AreEqual("upstream".As<GitRemoteName>(), remotes[0].Name);
	}

	[TestMethod]
	[DataRow("blob:none")]
	[DataRow("tree:0")]
	[DataRow("blob:limit=1m")]
	public void ReadsAFetchLineEndingInAPartialCloneFilter(string filter)
	{
		string output =
			"origin" + Tab + "C:/dev/my repos/upstream.git (fetch) [" + filter + "]\n" +
			"origin" + Tab + "git@github.com:ktsu-dev/GitIntegration.git (push)\n";

		IReadOnlyList<GitRemote> remotes = GitRemoteParser.Parse(output);

		Assert.AreEqual(1, remotes.Count);
		Assert.AreEqual("C:/dev/my repos/upstream.git".As<GitRepositoryRemotePath>(), remotes[0].FetchUrl);
		Assert.AreEqual("git@github.com:ktsu-dev/GitIntegration.git".As<GitRepositoryRemotePath>(), remotes[0].PushUrl);
	}

	[TestMethod]
	public void RejectsABracketedSuffixWithNoDirectionMarker()
	{
		Assert.ThrowsExactly<GitParseException>(
			() => GitRemoteParser.Parse("origin" + Tab + "https://example.com/repo.git [blob:none]\n"));
	}

	[TestMethod]
	public void RejectsALineWithNoTabSeparator()
	{
		Assert.ThrowsExactly<GitParseException>(
			() => GitRemoteParser.Parse("origin https://example.com/repo.git (fetch)\n"));
	}

	[TestMethod]
	public void RejectsALineWithNoDirectionMarker()
	{
		Assert.ThrowsExactly<GitParseException>(
			() => GitRemoteParser.Parse("origin" + Tab + "https://example.com/repo.git\n"));
	}
}
