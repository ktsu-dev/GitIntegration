// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;

using ktsu.Semantics.Strings;

[TestClass]
public class GitRepositoryMutatingVerbTests
{
	private static GitRepository RepositoryOn(IGitProcessRunner runner) =>
		new() { LocalPath = TestPaths.Root, ProcessRunner = runner };

	private static GitRemoteName Origin => "origin".As<GitRemoteName>();

	private static GitRepositoryRemotePath Url =>
		"https://example.com/repo.git".As<GitRepositoryRemotePath>();

	[TestMethod]
	public void EveryMutatingVerbIsScopedToTheRepositoryPath()
	{
		RecordingGitProcessRunner runner = new();
		GitRepository repository = RepositoryOn(runner);

		string[][] vectors =
		[
			[.. repository.Add().BuildArguments()],
			[.. repository.Commit("m".As<GitCommitMessage>()).BuildArguments()],
			[.. repository.CreateBranch("b".As<GitBranchName>()).BuildArguments()],
			[.. repository.DeleteBranch("b".As<GitBranchName>()).BuildArguments()],
			[.. repository.Checkout("main".As<GitRefName>()).BuildArguments()],
			[.. repository.AddRemote(Origin, Url).BuildArguments()],
			[.. repository.RemoveRemote(Origin).BuildArguments()],
			[.. repository.SetRemoteUrl(Origin, Url).BuildArguments()],
		];

		foreach (string[] vector in vectors)
		{
			Assert.AreEqual("-C", vector[0]);
			Assert.AreEqual(TestPaths.Root.WeakString, vector[1]);
		}
	}

	[TestMethod]
	public void EachCallReturnsAFreshBuilder()
	{
		RecordingGitProcessRunner runner = new();
		GitRepository repository = RepositoryOn(runner);

		Assert.AreNotSame(repository.Add(), repository.Add());
		Assert.AreNotSame(repository.Checkout("main".As<GitRefName>()), repository.Checkout("main".As<GitRefName>()));
	}

	[TestMethod]
	public void EveryMutatingVerbRequiresAProcessRunner()
	{
		// A repository carrying hosting metadata only describes something that may not exist on
		// disk yet. Deleting the guard from any one of these must fail a test.
		GitRepository repository = new() { LocalPath = TestPaths.Root };

		Assert.ThrowsExactly<InvalidOperationException>(() => _ = repository.Add());
		Assert.ThrowsExactly<InvalidOperationException>(() => _ = repository.Commit("m".As<GitCommitMessage>()));
		Assert.ThrowsExactly<InvalidOperationException>(() => _ = repository.CreateBranch("b".As<GitBranchName>()));
		Assert.ThrowsExactly<InvalidOperationException>(() => _ = repository.DeleteBranch("b".As<GitBranchName>()));
		Assert.ThrowsExactly<InvalidOperationException>(() => _ = repository.Checkout("main".As<GitRefName>()));
		Assert.ThrowsExactly<InvalidOperationException>(() => _ = repository.AddRemote(Origin, Url));
		Assert.ThrowsExactly<InvalidOperationException>(() => _ = repository.RemoveRemote(Origin));
		Assert.ThrowsExactly<InvalidOperationException>(() => _ = repository.SetRemoteUrl(Origin, Url));
	}

	[TestMethod]
	public void RejectsNullArguments()
	{
		RecordingGitProcessRunner runner = new();
		GitRepository repository = RepositoryOn(runner);

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = repository.Commit(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = repository.CreateBranch(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = repository.DeleteBranch(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = repository.Checkout(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = repository.AddRemote(null!, Url));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = repository.AddRemote(Origin, null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = repository.RemoveRemote(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = repository.SetRemoteUrl(null!, Url));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = repository.SetRemoteUrl(Origin, null!));
	}

	[TestMethod]
	public void ANullArgumentIsReportedBeforeAMissingProcessRunner()
	{
		// A metadata-only repository has no runner, so RequireRunner() would throw
		// InvalidOperationException. Pairing that with a null argument pins the documented ordering:
		// argument validation runs first, so the caller learns what they got wrong rather than being
		// told the repository has no runner.
		GitRepository repository = new() { LocalPath = TestPaths.Root };

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = repository.Commit(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = repository.CreateBranch(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = repository.Checkout(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = repository.AddRemote(null!, Url));
	}
}
