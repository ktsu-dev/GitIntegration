// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Threading.Tasks;

using ktsu.Semantics.Strings;

[TestClass]
public class GitBranchWriteBuilderTests
{
	private static GitBranchName Feature => "feature/x".As<GitBranchName>();

	[TestMethod]
	public void BuildsTheBranchCreateVector()
	{
		RecordingGitProcessRunner runner = new();
		GitBranchCreateBuilder builder = new(runner, TestPaths.Root, Feature);

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"branch",
			"--end-of-options",
			"feature/x",
		];
		CollectionAssert.AreEqual(expectedArguments, builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void PutsTheStartPointAfterTheBranchName()
	{
		// git branch <name> [<start-point>] is positional: reversing them creates a branch with the
		// wrong name pointing at the wrong place, and git reports no error.
		RecordingGitProcessRunner runner = new();
		GitBranchCreateBuilder builder = new(runner, TestPaths.Root, Feature);

		_ = builder.StartingAt("main".As<GitRefName>());

		string[] arguments = [.. builder.BuildArguments()];
		int marker = Array.IndexOf(arguments, "--end-of-options");

		Assert.AreEqual("feature/x", arguments[marker + 1]);
		Assert.AreEqual("main", arguments[marker + 2]);
	}

	[TestMethod]
	public void MapsForceOnCreateToTheResetFlag()
	{
		RecordingGitProcessRunner runner = new();
		GitBranchCreateBuilder builder = new(runner, TestPaths.Root, Feature);

		_ = builder.Force();

		CollectionAssert.Contains(builder.BuildArguments().ToArray(), "--force");
	}

	[TestMethod]
	public void BuildsTheBranchDeleteVector()
	{
		RecordingGitProcessRunner runner = new();
		GitBranchDeleteBuilder builder = new(runner, TestPaths.Root, Feature);

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"branch",
			"--delete",
			"--end-of-options",
			"feature/x",
		];
		CollectionAssert.AreEqual(expectedArguments, builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void MapsForceOnDeleteToTheForceFlag()
	{
		// --delete --force is the long form of -D, which deletes a branch that is not fully merged.
		RecordingGitProcessRunner runner = new();
		GitBranchDeleteBuilder builder = new(runner, TestPaths.Root, Feature);

		_ = builder.Force();

		string[] arguments = [.. builder.BuildArguments()];
		CollectionAssert.Contains(arguments, "--delete");
		CollectionAssert.Contains(arguments, "--force");
	}

	[TestMethod]
	public void ConfigurationMethodsReturnTheSameBuilderForChaining()
	{
		RecordingGitProcessRunner runner = new();

		GitBranchCreateBuilder create = new(runner, TestPaths.Root, Feature);
		Assert.AreSame(create, create.Force().StartingAt("main".As<GitRefName>()));

		GitBranchDeleteBuilder delete = new(runner, TestPaths.Root, Feature);
		Assert.AreSame(delete, delete.Force());
	}

	[TestMethod]
	public void RejectsNullArguments()
	{
		RecordingGitProcessRunner runner = new();

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new GitBranchCreateBuilder(runner, TestPaths.Root, null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new GitBranchDeleteBuilder(runner, TestPaths.Root, null!));

		GitBranchCreateBuilder builder = new(runner, TestPaths.Root, Feature);
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.StartingAt(null!));
	}

	[TestMethod]
	public async Task CreateThrowsWhenTheBranchAlreadyExistsAsync()
	{
		// Captured from git 2.50: creating a duplicate branch exits 128.
		RecordingGitProcessRunner runner = new()
		{
			ExitCode = 128,
			StandardError = "fatal: a branch named 'feature/x' already exists\n",
		};
		GitBranchCreateBuilder builder = new(runner, TestPaths.Root, Feature);

		GitCommandException exception = await Assert.ThrowsExactlyAsync<GitCommandException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.AreEqual(128, exception.ExitCode);
	}

	[TestMethod]
	public async Task DeleteReportsAnUnmergedBranchAsAFailureAsync()
	{
		// Captured from git 2.50: deleting an unmerged branch exits 1, not 128 — a caller probing
		// for this must not assume every git failure is 128.
		RecordingGitProcessRunner runner = new()
		{
			ExitCode = 1,
			StandardError = "error: the branch 'feature/x' is not fully merged\n",
		};
		GitBranchDeleteBuilder builder = new(runner, TestPaths.Root, Feature);

		GitResult<GitCompleted> result =
			await builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsFalse(result.Success);
		Assert.AreEqual(1, result.Error?.ExitCode);
	}

	public TestContext TestContext { get; set; } = null!;
}
