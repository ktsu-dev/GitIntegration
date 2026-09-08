// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

[TestClass]
public class GitRevListBuilderTests
{
	private static GitRefName Range => "a..b".As<GitRefName>();

	[TestMethod]
	public void BuildsTheDefaultCountVector()
	{
		RecordingGitProcessRunner runner = new();
		GitRevListBuilder builder = new(runner, TestPaths.Root, Range);

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"rev-list",
			"--count",
			"--end-of-options",
			"a..b",
		];
		CollectionAssert.AreEqual(expectedArguments, builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void PutsAPathspecAfterTheRevision()
	{
		// git reads everything after -- as a filename, so a revision emitted there would count
		// nothing rather than failing — the same trap GitLogBuilder documents for its own pathspec.
		RecordingGitProcessRunner runner = new();
		GitRevListBuilder builder = new(runner, TestPaths.Root, Range);

		_ = builder.FirstParentOnly().ForPath("src/a.txt".As<RelativeFilePath>());

		string[] arguments = [.. builder.BuildArguments()];
		int separator = Array.IndexOf(arguments, "--");

		Assert.IsTrue(Array.IndexOf(arguments, "--first-parent") < separator);
		Assert.IsTrue(Array.IndexOf(arguments, "a..b") < separator);
		Assert.AreEqual("src/a.txt", arguments[separator + 1]);
	}

	[TestMethod]
	public void EmitsEveryPathThatWasAdded()
	{
		RecordingGitProcessRunner runner = new();
		GitRevListBuilder builder = new(runner, TestPaths.Root, Range);

		_ = builder.ForPath("a.txt".As<RelativeFilePath>()).ForPath("b.txt".As<RelativeFilePath>());

		string[] arguments = [.. builder.BuildArguments()];

		CollectionAssert.Contains(arguments, "a.txt");
		CollectionAssert.Contains(arguments, "b.txt");
	}

	[TestMethod]
	public async Task ParsesTheCountAsync()
	{
		RecordingGitProcessRunner runner = new() { StandardOutput = "42\n" };
		GitRevListBuilder builder = new(runner, TestPaths.Root, Range);

		int count = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(42, count);
	}

	[TestMethod]
	public async Task ParsesAZeroCountAsync()
	{
		// An empty range is an ordinary answer, not a failure, and zero has to survive as a value
		// rather than being confused with "nothing was reported".
		RecordingGitProcessRunner runner = new() { StandardOutput = "0\n" };
		GitRevListBuilder builder = new(runner, TestPaths.Root, Range);

		int count = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(0, count);
	}

	[TestMethod]
	public async Task ThrowsWhenTheOutputIsNotACountAsync()
	{
		RecordingGitProcessRunner runner = new() { StandardOutput = "not a number\n" };
		GitRevListBuilder builder = new(runner, TestPaths.Root, Range);

		await Assert.ThrowsExactlyAsync<GitParseException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	[TestMethod]
	public void RejectsNullArguments()
	{
		RecordingGitProcessRunner runner = new();
		GitRevListBuilder builder = new(runner, TestPaths.Root, Range);

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new GitRevListBuilder(runner, TestPaths.Root, null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.ForPath(null!));
	}

	[TestMethod]
	public void ConfigurationMethodsReturnTheSameBuilderForChaining()
	{
		RecordingGitProcessRunner runner = new();
		GitRevListBuilder builder = new(runner, TestPaths.Root, Range);

		Assert.AreSame(builder, builder.FirstParentOnly().ForPath("a.txt".As<RelativeFilePath>()));
	}

	public TestContext TestContext { get; set; } = null!;
}

[TestClass]
public class GitRevListDivergenceBuilderTests
{
	private static GitRefName Upstream => "origin/main".As<GitRefName>();

	private static GitRefName Local => "HEAD".As<GitRefName>();

	[TestMethod]
	public void BuildsTheThreeDotExpression()
	{
		// Three dots, not two. "a..b" names the commits b has and a does not, which is one number;
		// "a...b" names the commits either has and the other does not, which is the pair
		// --left-right splits.
		RecordingGitProcessRunner runner = new();
		GitRevListDivergenceBuilder builder = new(runner, TestPaths.Root, Upstream, Local);

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"rev-list",
			"--count",
			"--left-right",
			"--end-of-options",
			"origin/main...HEAD",
		];
		CollectionAssert.AreEqual(expectedArguments, builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public async Task MapsTheLeftCountToBehindAndTheRightToAheadAsync()
	{
		// The mapping is the part that is easy to get backwards, so it is pinned rather than assumed.
		// git prints "<left>\t<right>", and the expression is always built as upstream...local, so
		// left counts what only the upstream has (behind) and right what only the local has (ahead).
		RecordingGitProcessRunner runner = new() { StandardOutput = "3\t5\n" };
		GitRevListDivergenceBuilder builder = new(runner, TestPaths.Root, Upstream, Local);

		GitDivergence divergence = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(3, divergence.Behind);
		Assert.AreEqual(5, divergence.Ahead);
		Assert.IsFalse(divergence.IsInSync);
	}

	[TestMethod]
	public async Task ReportsInSyncWhenNeitherSideHasAnExclusiveCommitAsync()
	{
		RecordingGitProcessRunner runner = new() { StandardOutput = "0\t0\n" };
		GitRevListDivergenceBuilder builder = new(runner, TestPaths.Root, Upstream, Local);

		GitDivergence divergence = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsTrue(divergence.IsInSync);
	}

	[TestMethod]
	public async Task ThrowsWhenTheOutputIsNotAPairOfCountsAsync()
	{
		// A single number is what a plain --count prints, so reaching this parser with one means the
		// --left-right flag did not take effect. Silently reading it as one side of the pair would
		// report a confident zero for the other.
		RecordingGitProcessRunner runner = new() { StandardOutput = "7\n" };
		GitRevListDivergenceBuilder builder = new(runner, TestPaths.Root, Upstream, Local);

		await Assert.ThrowsExactlyAsync<GitParseException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	[TestMethod]
	public void RejectsNullArguments()
	{
		RecordingGitProcessRunner runner = new();

		Assert.ThrowsExactly<ArgumentNullException>(
			() => _ = new GitRevListDivergenceBuilder(runner, TestPaths.Root, null!, Local));
		Assert.ThrowsExactly<ArgumentNullException>(
			() => _ = new GitRevListDivergenceBuilder(runner, TestPaths.Root, Upstream, null!));
	}

	public TestContext TestContext { get; set; } = null!;
}
