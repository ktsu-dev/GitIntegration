// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Threading.Tasks;

using ktsu.Semantics.Strings;

[TestClass]
public class GitFetchBuilderTests
{
	private const string OldSha = "7c85857fc85452150652c289fc94f1f912b96c40";
	private const string NewSha = "6702dd1957dedc4e0f54245bda65f3ddc5f37e64";

	private const string PorcelainOutput =
		" " + OldSha + " " + NewSha + " refs/remotes/origin/main\n";

	private static ScriptedGitProcessRunner RunnerOn(string version, string fetchOutput) =>
		new ScriptedGitProcessRunner()
			.Then(standardOutput: "git version " + version + "\n")
			.Then(standardOutput: fetchOutput);

	[TestMethod]
	public void BuildsThePorcelainVectorByDefault()
	{
		// BuildArguments is documented as pure with no I/O, so it cannot probe. It emits the
		// porcelain form until an execution path tells it otherwise.
		RecordingGitProcessRunner runner = new();
		GitFetchBuilder builder = new(runner, TestPaths.Root);

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"fetch",
			"--porcelain",
		];
		CollectionAssert.AreEqual(expectedArguments, builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void MapsTheOptionFlags()
	{
		RecordingGitProcessRunner runner = new();
		GitFetchBuilder builder = new(runner, TestPaths.Root);

		_ = builder.AllRemotes().Prune().WithTags().WithDepth(1);

		string[] arguments = [.. builder.BuildArguments()];
		CollectionAssert.Contains(arguments, "--all");
		CollectionAssert.Contains(arguments, "--prune");
		CollectionAssert.Contains(arguments, "--tags");
		CollectionAssert.Contains(arguments, "--depth=1");
	}

	[TestMethod]
	public void PutsTheRemoteBehindTheEndOfOptionsMarker()
	{
		RecordingGitProcessRunner runner = new();
		GitFetchBuilder builder = new(runner, TestPaths.Root);

		_ = builder.FromRemote("origin".As<GitRemoteName>());

		string[] arguments = [.. builder.BuildArguments()];
		int marker = Array.IndexOf(arguments, "--end-of-options");

		Assert.AreNotEqual(-1, marker);
		Assert.AreEqual("origin", arguments[marker + 1]);
	}

	[TestMethod]
	public void RejectsANonPositiveDepth()
	{
		RecordingGitProcessRunner runner = new();
		GitFetchBuilder builder = new(runner, TestPaths.Root);

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = builder.WithDepth(0));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = builder.WithDepth(-1));
	}

	[TestMethod]
	public void RejectsNullArguments()
	{
		RecordingGitProcessRunner runner = new();
		GitFetchBuilder builder = new(runner, TestPaths.Root);

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.FromRemote(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.ReportingProgress(null!));
	}

	[TestMethod]
	public async Task ProbesTheVersionThenFetchesWithPorcelainOnModernGitAsync()
	{
		ScriptedGitProcessRunner runner = RunnerOn("2.50.1.windows.1", PorcelainOutput);
		GitFetchBuilder builder = new(runner, TestPaths.Root);

		GitFetchResult result = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsTrue(result.DetailAvailable);
		Assert.AreEqual(1, result.Updates.Count);

		Assert.AreEqual(2, runner.Invocations.Count);
		CollectionAssert.Contains(runner.Invocations[0].ToArray(), "--version");
		CollectionAssert.Contains(runner.Invocations[1].ToArray(), "--porcelain");
	}

	[TestMethod]
	public async Task OmitsPorcelainAndReportsNoDetailOnOlderGitAsync()
	{
		// 2.40 predates fetch --porcelain. The fetch must still happen; only the itemised report
		// is unavailable, and DetailAvailable is what tells the caller which of the two empty-list
		// meanings applies.
		ScriptedGitProcessRunner runner = RunnerOn("2.40.1", string.Empty);
		GitFetchBuilder builder = new(runner, TestPaths.Root);

		GitFetchResult result = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsFalse(result.DetailAvailable);
		Assert.AreEqual(0, result.Updates.Count);
		Assert.IsFalse(result.IsUpToDate);

		CollectionAssert.DoesNotContain(runner.Invocations[1].ToArray(), "--porcelain");
	}

	[TestMethod]
	public async Task TreatsExactlyTwoFortyOneAsSupportedAsync()
	{
		// The documented floor, asserted exactly: an off-by-one here silently disables porcelain
		// for every user on the first version that supports it.
		ScriptedGitProcessRunner runner = RunnerOn("2.41.0", PorcelainOutput);
		GitFetchBuilder builder = new(runner, TestPaths.Root);

		GitFetchResult result = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsTrue(result.DetailAvailable);
		CollectionAssert.Contains(runner.Invocations[1].ToArray(), "--porcelain");
	}

	[TestMethod]
	public async Task ReportsAnUpToDateFetchOnModernGitAsync()
	{
		// git prints nothing when a fetch changed nothing, which is a genuine "up to date" — as
		// distinct from the empty list an old git produces.
		ScriptedGitProcessRunner runner = RunnerOn("2.50.1", string.Empty);
		GitFetchBuilder builder = new(runner, TestPaths.Root);

		GitFetchResult result = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsTrue(result.IsUpToDate);
	}

	[TestMethod]
	public async Task ThrowsWhenTheFetchItselfFailsAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: "git version 2.50.1\n")
			.Then(standardError: "fatal: 'nosuch' does not appear to be a git repository\n", exitCode: 128);
		GitFetchBuilder builder = new(runner, TestPaths.Root);

		GitCommandException exception = await Assert.ThrowsExactlyAsync<GitCommandException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.AreEqual(128, exception.ExitCode);
	}

	[TestMethod]
	public async Task TryExecuteReportsAFetchFailureAsAResultAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: "git version 2.50.1\n")
			.Then(standardError: "fatal: could not read from remote\n", exitCode: 128);
		GitFetchBuilder builder = new(runner, TestPaths.Root);

		GitResult<GitFetchResult> result =
			await builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsFalse(result.Success);
		Assert.AreEqual(128, result.Error?.ExitCode);
	}

	[TestMethod]
	public async Task ForwardsProgressToTheFetchRequestAsync()
	{
		ScriptedGitProcessRunner runner = RunnerOn("2.50.1", PorcelainOutput);
		GitFetchBuilder builder = new(runner, TestPaths.Root);

		_ = builder.ReportingProgress(new Progress<string>(static _ => { }));
		_ = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(2, runner.Invocations.Count);
	}

	public TestContext TestContext { get; set; } = null!;
}
