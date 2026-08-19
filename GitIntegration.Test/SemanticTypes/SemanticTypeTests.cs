// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Diagnostics.CodeAnalysis;

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
	[SuppressMessage("Assertions", "MSTEST0065:Do not assert on IEnumerable<T> with AreEqual/AreNotEqual", Justification = "The <object> type argument is deliberate: this checks reference/value inequality between two distinct semantic string types, not element-wise sequence content.")]
	public void SemanticTypesAreDistinctAtCompileTime()
	{
		GitBranchName branch = "main".As<GitBranchName>();
		GitRemoteName remote = "origin".As<GitRemoteName>();

		Assert.AreNotEqual<object>(branch, remote);
	}
}
