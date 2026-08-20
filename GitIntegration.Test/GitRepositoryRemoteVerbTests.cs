// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;

[TestClass]
public class GitRepositoryRemoteVerbTests
{
	private static GitRepository RepositoryOn(IGitProcessRunner runner) =>
		new() { LocalPath = TestPaths.Root, ProcessRunner = runner };

	[TestMethod]
	public void EveryRemoteVerbIsScopedToTheRepositoryPath()
	{
		RecordingGitProcessRunner runner = new();
		GitRepository repository = RepositoryOn(runner);

		string[][] vectors =
		[
			[.. repository.Fetch().BuildArguments()],
			[.. repository.Pull().BuildArguments()],
			[.. repository.Push().BuildArguments()],
		];

		foreach (string[] vector in vectors)
		{
			Assert.AreEqual("-C", vector[0]);
			Assert.AreEqual(TestPaths.Root.WeakString, vector[1]);
		}
	}

	[TestMethod]
	public void EachRemoteVerbEmitsItsOwnCommand()
	{
		RecordingGitProcessRunner runner = new();
		GitRepository repository = RepositoryOn(runner);

		CollectionAssert.Contains(repository.Fetch().BuildArguments().ToArray(), "fetch");
		CollectionAssert.Contains(repository.Pull().BuildArguments().ToArray(), "pull");
		CollectionAssert.Contains(repository.Push().BuildArguments().ToArray(), "push");
	}

	[TestMethod]
	public void EachCallReturnsAFreshBuilder()
	{
		RecordingGitProcessRunner runner = new();
		GitRepository repository = RepositoryOn(runner);

		Assert.AreNotSame(repository.Fetch(), repository.Fetch());
		Assert.AreNotSame(repository.Pull(), repository.Pull());
		Assert.AreNotSame(repository.Push(), repository.Push());
	}

	[TestMethod]
	public void EveryRemoteVerbRequiresAProcessRunner()
	{
		// A repository carrying hosting metadata only describes something that may not exist on
		// disk. Deleting the guard from any one of these must fail this test.
		GitRepository repository = new() { LocalPath = TestPaths.Root };

		Assert.ThrowsExactly<InvalidOperationException>(() => _ = repository.Fetch());
		Assert.ThrowsExactly<InvalidOperationException>(() => _ = repository.Pull());
		Assert.ThrowsExactly<InvalidOperationException>(() => _ = repository.Push());
	}
}
