// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Collections.Generic;
using System.Linq;

using ktsu.Semantics.Strings;

[TestClass]
public class GitBranchParserTests
{
	private const string Us = "\u001f";

	private const string LocalSha = "9429d2063d91f1097de51a196cb8203b06335738";
	private const string RemoteSha = "94947d6da5c05bf1c86af335b33cff8cee83cb3f";

	// Captured from a fresh clone. Note refs/remotes/origin/HEAD, whose short name is the bare
	// remote name, and the single space %(HEAD) emits for a branch that is not checked out.
	private const string FreshClone =
		"refs/heads/main" + Us + "main" + Us + LocalSha + Us + "origin/main" + Us + "*\n" +
		"refs/remotes/origin/HEAD" + Us + "origin" + Us + LocalSha + Us + Us + " \n" +
		"refs/remotes/origin/feature/x" + Us + "origin/feature/x" + Us + RemoteSha + Us + Us + " \n" +
		"refs/remotes/origin/main" + Us + "origin/main" + Us + RemoteSha + Us + Us + " \n";

	private const string LocalBranchNamedLikeARemote =
		"refs/heads/origin/main" + Us + "origin/main" + Us + LocalSha + Us + Us + " \n";

	[TestMethod]
	public void ReadsTheCurrentBranchWithItsUpstream()
	{
		IReadOnlyList<GitBranch> branches = GitBranchParser.Parse(FreshClone);

		GitBranch main = branches[0];
		Assert.AreEqual("main".As<GitBranchName>(), main.Name);
		Assert.AreEqual(LocalSha.As<GitCommitSha>(), main.Sha);
		Assert.AreEqual("origin/main".As<GitBranchName>(), main.Upstream);
		Assert.IsTrue(main.IsCurrent);
		Assert.IsFalse(main.IsRemote);
	}

	[TestMethod]
	public void SkipsTheRemoteHeadSymbolicRef()
	{
		// refs/remotes/origin/HEAD shortens to the bare remote name, so without this filter every
		// clone reports a phantom branch called "origin".
		IReadOnlyList<GitBranch> branches = GitBranchParser.Parse(FreshClone);

		Assert.AreEqual(3, branches.Count);
		Assert.IsFalse(branches.Any(branch => branch.Name == "origin".As<GitBranchName>()));
	}

	[TestMethod]
	public void MarksBranchesUnderRefsRemotesAsRemote()
	{
		IReadOnlyList<GitBranch> branches = GitBranchParser.Parse(FreshClone);

		Assert.IsTrue(branches[1].IsRemote);
		Assert.AreEqual("origin/feature/x".As<GitBranchName>(), branches[1].Name);
		Assert.IsTrue(branches[2].IsRemote);
		Assert.IsNull(branches[1].Upstream);
		Assert.IsFalse(branches[1].IsCurrent);
	}

	[TestMethod]
	public void TreatsALocalBranchWithASlashAsLocal()
	{
		// The whole reason the format leads with %(refname): "origin/main" as a short name is
		// ambiguous, and only the full reference name settles it.
		IReadOnlyList<GitBranch> branches = GitBranchParser.Parse(LocalBranchNamedLikeARemote);

		Assert.AreEqual(1, branches.Count);
		Assert.IsFalse(branches[0].IsRemote);
		Assert.AreEqual("origin/main".As<GitBranchName>(), branches[0].Name);
	}

	[TestMethod]
	public void ReturnsAnEmptyListForNoOutput()
	{
		// An empty repository has no branch references at all.
		Assert.AreEqual(0, GitBranchParser.Parse(string.Empty).Count);
	}

	[TestMethod]
	public void ToleratesCarriageReturnLineEndings()
	{
		string output = "refs/heads/main" + Us + "main" + Us + LocalSha + Us + Us + "*\r\n";

		IReadOnlyList<GitBranch> branches = GitBranchParser.Parse(output);

		Assert.AreEqual("main".As<GitBranchName>(), branches[0].Name);
		Assert.IsTrue(branches[0].IsCurrent);
	}

	[TestMethod]
	public void RejectsARecordWithTooFewFields()
	{
		Assert.ThrowsExactly<GitParseException>(
			() => GitBranchParser.Parse("refs/heads/main" + Us + "main\n"));
	}
}
