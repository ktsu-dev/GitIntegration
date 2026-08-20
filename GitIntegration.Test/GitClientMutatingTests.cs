// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

using Testably.Abstractions.Testing;

[TestClass]
public class GitClientMutatingTests
{
	private static AbsoluteDirectoryPath Target =>
		(OperatingSystem.IsWindows() ? @"C:\dev\new-repo" : "/dev/new-repo").As<AbsoluteDirectoryPath>();

	private static GitRepositoryRemotePath Url =>
		"https://example.com/repo.git".As<GitRepositoryRemotePath>();

	private static GitClient ClientOn(IGitProcessRunner runner) =>
		new(runner, new FakeFileSystemProvider(new MockFileSystem()));

	[TestMethod]
	public void InitBuildsAVectorTargetingTheGivenPath()
	{
		RecordingGitProcessRunner runner = new();

		string[] arguments = [.. ClientOn(runner).Init(Target).BuildArguments()];

		CollectionAssert.Contains(arguments, "init");
		CollectionAssert.Contains(arguments, Target.WeakString);
		CollectionAssert.DoesNotContain(arguments, "-C");
	}

	[TestMethod]
	public void CloneBuildsAVectorCarryingSourceAndDestination()
	{
		RecordingGitProcessRunner runner = new();

		string[] arguments = [.. ClientOn(runner).Clone(Url, Target).BuildArguments()];

		CollectionAssert.Contains(arguments, "clone");
		CollectionAssert.Contains(arguments, Url.WeakString);
		CollectionAssert.Contains(arguments, Target.WeakString);
	}

	[TestMethod]
	public void CloneFromARepositoryUsesItsRemotePathAndLocalPath()
	{
		// The seam between the two layers: a hosting provider yields a repository with metadata and
		// an intended local path, and this turns it into a working copy.
		RecordingGitProcessRunner runner = new();
		GitRepository metadataOnly = new() { LocalPath = Target, RemotePath = Url };

		string[] arguments = [.. ClientOn(runner).Clone(metadataOnly).BuildArguments()];

		CollectionAssert.Contains(arguments, Url.WeakString);
		CollectionAssert.Contains(arguments, Target.WeakString);
	}

	[TestMethod]
	public void CloneFromARepositoryWithNoRemotePathIsRejectedAtTheCall()
	{
		// Failing here beats failing inside git with a confusing message about an empty argument.
		RecordingGitProcessRunner runner = new();
		GitRepository noRemote = new() { LocalPath = Target };

		Assert.ThrowsExactly<ArgumentException>(() => _ = ClientOn(runner).Clone(noRemote));
	}

	[TestMethod]
	public void RejectsNullArguments()
	{
		RecordingGitProcessRunner runner = new();
		GitClient client = ClientOn(runner);

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = client.Init(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = client.Clone(null!, Target));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = client.Clone(Url, null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = client.Clone(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new GitClient(runner, null!));
	}

	[TestMethod]
	public void TheSingleArgumentConstructorStillWorks()
	{
		// Shipped public in 2.1.0. Adding a required parameter would be source-breaking, so the old
		// signature stays and defaults to the real filesystem.
		RecordingGitProcessRunner runner = new();

		GitClient client = new(runner);

		Assert.IsNotNull(client.Init(Target));
	}

	[TestMethod]
	public async Task InitThroughTheClientReportsAFreshRepositoryAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardError: "fatal: not a git repository\n", exitCode: 128)
			.Then(standardOutput: "Initialized empty Git repository\n");

		GitInitResult result = await ClientOn(runner)
			.Init(Target)
			.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsFalse(result.AlreadyExisted);
		Assert.AreEqual(Target, result.Repository.LocalPath);
	}

	public TestContext TestContext { get; set; } = null!;
}
