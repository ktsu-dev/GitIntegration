// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Threading.Tasks;

using ktsu.Semantics.Strings;

[TestClass]
public class GitRepositoryVerbTests
{
	private static GitRepository RepositoryOn(IGitProcessRunner runner) =>
		new() { LocalPath = TestPaths.Root, ProcessRunner = runner };

	[TestMethod]
	public void EveryVerbIsScopedToTheRepositoryPath()
	{
		RecordingGitProcessRunner runner = new();
		GitRepository repository = RepositoryOn(runner);

		string[][] vectors =
		[
			[.. repository.Status().BuildArguments()],
			[.. repository.Log().BuildArguments()],
			[.. repository.Diff().BuildArguments()],
			[.. repository.Branches().BuildArguments()],
			[.. repository.Remotes().BuildArguments()],
			[.. repository.RevParse("HEAD".As<GitRefName>()).BuildArguments()],
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
		// Builders are mutable and single-use, so handing the same instance back would let one
		// caller's options leak into another's command.
		RecordingGitProcessRunner runner = new();
		GitRepository repository = RepositoryOn(runner);

		Assert.AreNotSame(repository.Status(), repository.Status());
		Assert.AreNotSame(repository.Log(), repository.Log());
	}

	[TestMethod]
	public void VerbsOnAMetadataOnlyRepositoryExplainWhatIsMissing()
	{
		// A repository produced by a hosting provider carries metadata for something that may not
		// exist on disk yet, so it has no runner.
		GitRepository repository = new()
		{
			LocalPath = TestPaths.Root,
			Name = "GitIntegration".As<GitRepositoryName>(),
		};

		InvalidOperationException exception =
			Assert.ThrowsExactly<InvalidOperationException>(() => _ = repository.Status());

		StringAssert.Contains(exception.Message, nameof(IGitClient.OpenAsync));
	}

	[TestMethod]
	public void RevParseRejectsANullRevision()
	{
		RecordingGitProcessRunner runner = new();
		GitRepository repository = RepositoryOn(runner);

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = repository.RevParse(null!));
	}

	[TestMethod]
	public async Task IsClonedReportsTrueWhenGitFindsAWorkingTreeAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner().Then(standardOutput: "true\n");
		GitRepository repository = RepositoryOn(runner);

		bool isCloned = await repository.IsClonedAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsTrue(isCloned);
	}

	[TestMethod]
	public async Task IsClonedReportsFalseForAPathThatIsNotAWorkingTreeAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardError: "fatal: not a git repository (or any of the parent directories): .git\n", exitCode: 128);
		GitRepository repository = RepositoryOn(runner);

		bool isCloned = await repository.IsClonedAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsFalse(isCloned);
	}

	public TestContext TestContext { get; set; } = null!;
}
