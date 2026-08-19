// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using ktsu.Semantics.Strings;

[TestClass]
public class SemanticTypeTests
{
	[TestMethod]
	public void GitCommitShaAcceptsFullSha()
	{
		GitCommitSha sha = GitCommitSha.Create("3f2a1b4c5d6e7f8091a2b3c4d5e6f708192a3b4c");

		Assert.AreEqual("3f2a1b4c5d6e7f8091a2b3c4d5e6f708192a3b4c", sha.WeakString);
	}

	[TestMethod]
	public void GitCommitShaAcceptsSha256ObjectId()
	{
		// A repository created with --object-format=sha256 emits 64 hex characters.
		const string sha256 = "5b4c3d2e1f009a8b7c6d5e4f3a2b1c0d9e8f7a6b5c4d3e2f1a0b9c8d7e6f5a4b";

		GitCommitSha sha = GitCommitSha.Create(sha256);

		Assert.AreEqual(sha256, sha.WeakString);
	}

	[TestMethod]
	public void GitCommitShaRejectsValueLongerThanSha256()
	{
		Assert.IsFalse(GitCommitSha.TryCreate(new string('a', 65), out GitCommitSha? result));
		Assert.IsNull(result);
	}

	[TestMethod]
	public void GitCommitShaAcceptsAbbreviatedSha()
	{
		GitCommitSha sha = GitCommitSha.Create("3f2a1b4");

		Assert.AreEqual("3f2a1b4", sha.WeakString);
	}

	[TestMethod]
	public void GitCommitShaLowercasesUppercaseInput()
	{
		GitCommitSha sha = GitCommitSha.Create("3F2A1B4C");

		Assert.AreEqual("3f2a1b4c", sha.WeakString);
	}

	[TestMethod]
	public void GitCommitShaRejectsNonHexadecimal()
	{
		Assert.IsFalse(GitCommitSha.TryCreate("zzzzzzz", out GitCommitSha? result));
		Assert.IsNull(result);
	}

	[TestMethod]
	public void GitCommitShaRejectsTooShortValue()
	{
		Assert.IsFalse(GitCommitSha.TryCreate("3f2", out GitCommitSha? result));
		Assert.IsNull(result);
	}

	[TestMethod]
	public void GitBranchNameRejectsWhitespaceOnlyValue()
	{
		Assert.IsFalse(GitBranchName.TryCreate("   ", out GitBranchName? result));
		Assert.IsNull(result);
	}

	[TestMethod]
	public void GitBranchNameAcceptsSlashSeparatedName()
	{
		GitBranchName branch = GitBranchName.Create("feature/git-v2");

		Assert.AreEqual("feature/git-v2", branch.WeakString);
	}

	[TestMethod]
	[DataRow("-f")]
	[DataRow("--upload-pack=calc.exe")]
	[DataRow("-")]
	public void GitBranchNameRejectsLeadingDash(string value)
	{
		Assert.IsFalse(GitBranchName.TryCreate(value, out GitBranchName? result));
		Assert.IsNull(result);
	}

	[TestMethod]
	[DataRow("-f")]
	[DataRow("--upload-pack=calc.exe")]
	public void GitRefNameRejectsLeadingDash(string value)
	{
		Assert.IsFalse(GitRefName.TryCreate(value, out GitRefName? result));
		Assert.IsNull(result);
	}

	[TestMethod]
	[DataRow("-f")]
	[DataRow("--upload-pack=calc.exe")]
	public void GitRemoteNameRejectsLeadingDash(string value)
	{
		Assert.IsFalse(GitRemoteName.TryCreate(value, out GitRemoteName? result));
		Assert.IsNull(result);
	}

	[TestMethod]
	[DataRow("-f")]
	[DataRow("--upload-pack=calc.exe")]
	public void GitRepositoryRemotePathRejectsLeadingDash(string value)
	{
		Assert.IsFalse(GitRepositoryRemotePath.TryCreate(value, out GitRepositoryRemotePath? result));
		Assert.IsNull(result);
	}

	[TestMethod]
	public void LeadingDashRejectionDoesNotAffectOrdinaryValues()
	{
		Assert.IsTrue(GitBranchName.TryCreate("feature/git-v2", out GitBranchName? branch));
		Assert.IsNotNull(branch);
		Assert.IsTrue(GitRefName.TryCreate("HEAD~1", out GitRefName? reference));
		Assert.IsNotNull(reference);
		Assert.IsTrue(GitRemoteName.TryCreate("origin", out GitRemoteName? remote));
		Assert.IsNotNull(remote);
		Assert.IsTrue(GitRepositoryRemotePath.TryCreate("https://github.com/ktsu-dev/GitIntegration.git", out GitRepositoryRemotePath? remotePath));
		Assert.IsNotNull(remotePath);
	}

	[TestMethod]
	public void SemanticTypesAreDistinctAtCompileTime()
	{
		GitBranchName branch = "main".As<GitBranchName>();

		// Asserting inequality of the two values would pass against any implementation, including
		// one where both types were aliases for the same thing. The type identity is the invariant
		// worth checking, so check that instead.
		Assert.IsNotInstanceOfType<GitRemoteName>(branch);
	}
}
