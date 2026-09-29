// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

[TestClass]
public class GitRestoreBuilderTests
{
	[TestMethod]
	public void BuildsTheResetVector()
	{
		// reset rather than restore --staged: restore reads the index back from HEAD and dies with
		// "could not resolve HEAD" before the first commit, where reset leaves the file untracked.
		RecordingGitProcessRunner runner = new();
		GitRestoreBuilder builder = new(runner, TestPaths.Root, "f.txt".As<RelativeFilePath>());

		string[] arguments = [.. builder.BuildArguments()];

		Assert.AreSequenceEqual(["reset", "-q", "--", "f.txt"], arguments[^4..]);
		Assert.IsFalse(arguments.Contains("restore"));
	}

	[TestMethod]
	public void SeparatesThePathWithABareDoubleDash()
	{
		RecordingGitProcessRunner runner = new();
		GitRestoreBuilder builder = new(runner, TestPaths.Root, "f.txt".As<RelativeFilePath>());

		string[] arguments = [.. builder.BuildArguments()];

		Assert.AreEqual("--", arguments[^2]);
		Assert.IsFalse(
			arguments.Contains("--end-of-options"),
			"A bare -- protects a dash-leading path on every git, while --end-of-options needs 2.24.");
	}

	[TestMethod]
	public void RefusesANullPath()
	{
		RecordingGitProcessRunner runner = new();
		GitRepository repository = new() { LocalPath = TestPaths.Root, ProcessRunner = runner };

		_ = Assert.ThrowsExactly<ArgumentNullException>(() => _ = repository.Unstage(null!));
	}

	[TestMethod]
	public async Task RunsOneCommandWithNoVersionProbeAsync()
	{
		// reset behaves the same on every supported git, so nothing needs to ask which one is
		// installed first.
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: string.Empty);
		GitRestoreBuilder builder = new(runner, TestPaths.Root, "f.txt".As<RelativeFilePath>());

		_ = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		string[] invocation = [.. Assert.ContainsSingle(runner.Invocations)];
		Assert.AreSequenceEqual(["reset", "-q", "--", "f.txt"], invocation[^4..]);
	}

	public TestContext TestContext { get; set; } = null!;
}
