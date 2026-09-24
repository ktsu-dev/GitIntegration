// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

[TestClass]
public class GitRestoreBuilderTests
{
	[TestMethod]
	public void BuildsTheRestoreVectorOnAModernGit()
	{
		RecordingGitProcessRunner runner = new();
		GitRestoreBuilder builder = new(runner, TestPaths.Root, "f.txt".As<RelativeFilePath>());

		IReadOnlyList<string> arguments = builder.BuildArguments();

		Assert.IsTrue(arguments.Contains("restore"));
		Assert.IsTrue(arguments.Contains("--staged"));
		Assert.IsTrue(arguments.Contains("f.txt"));
	}

	[TestMethod]
	public void RefusesANullPath()
	{
		RecordingGitProcessRunner runner = new();
		GitRepository repository = new() { LocalPath = TestPaths.Root, ProcessRunner = runner };

		_ = Assert.ThrowsExactly<ArgumentNullException>(() => _ = repository.Unstage(null!));
	}

	[TestMethod]
	public async Task FallsBackToResetOnAGitOlderThanRestoreAsync()
	{
		// git restore arrived in 2.23. Below that, unstaging goes through reset HEAD instead, which
		// every supported git understands.
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: "git version 2.22.0\n")
			.Then(standardOutput: string.Empty);
		GitRestoreBuilder builder = new(runner, TestPaths.Root, "f.txt".As<RelativeFilePath>());

		_ = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		string[] arguments = [.. runner.Invocations[1]];
		Assert.AreSequenceEqual(["reset", "HEAD", "--end-of-options", "f.txt"], arguments[^4..]);
	}

	[TestMethod]
	public async Task TreatsExactlyTwoTwentyThreeAsSupportedAsync()
	{
		// The documented floor, asserted exactly: an off-by-one here silently falls back to reset
		// for every user on the first version that supports restore.
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: "git version 2.23.0\n")
			.Then(standardOutput: string.Empty);
		GitRestoreBuilder builder = new(runner, TestPaths.Root, "f.txt".As<RelativeFilePath>());

		_ = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		string[] arguments = [.. runner.Invocations[1]];
		Assert.AreSequenceEqual(["restore", "--staged", "--end-of-options", "f.txt"], arguments[^4..]);
	}

	public TestContext TestContext { get; set; } = null!;
}
