// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Threading.Tasks;

using ktsu.Semantics.Strings;

[TestClass]
public class GitPullBuilderTests
{
	private const string ConflictOutput =
		"Auto-merging c.txt\n" +
		"CONFLICT (content): Merge conflict in c.txt\n" +
		"Automatic merge failed; fix conflicts and then commit the result.\n";

	[TestMethod]
	public void BuildsTheDefaultPullVector()
	{
		RecordingGitProcessRunner runner = new();
		GitPullBuilder builder = new(runner, TestPaths.Root);

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"pull",
		];
		CollectionAssert.AreEqual(expectedArguments, builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void MapsTheOptionFlags()
	{
		RecordingGitProcessRunner runner = new();

		GitPullBuilder ffOnly = new(runner, TestPaths.Root);
		_ = ffOnly.FastForwardOnly();
		CollectionAssert.Contains(ffOnly.BuildArguments().ToArray(), "--ff-only");

		GitPullBuilder rebase = new(runner, TestPaths.Root);
		_ = rebase.Rebase();
		CollectionAssert.Contains(rebase.BuildArguments().ToArray(), "--rebase");

		GitPullBuilder prune = new(runner, TestPaths.Root);
		_ = prune.Prune();
		CollectionAssert.Contains(prune.BuildArguments().ToArray(), "--prune");
	}

	[TestMethod]
	public void RejectsAskingForBothFastForwardOnlyAndRebase()
	{
		// git accepts both and lets one win silently. Since they mean opposite things about
		// history — refuse to merge, versus rewrite to avoid merging — a caller who asked for both
		// has a bug, and reporting it beats guessing which they meant.
		RecordingGitProcessRunner runner = new();
		GitPullBuilder builder = new(runner, TestPaths.Root);

		_ = builder.FastForwardOnly().Rebase();

		Assert.ThrowsExactly<InvalidOperationException>(() => _ = builder.BuildArguments());
	}

	[TestMethod]
	public void PutsTheRemoteAndBranchBehindTheMarkerInOrder()
	{
		RecordingGitProcessRunner runner = new();
		GitPullBuilder builder = new(runner, TestPaths.Root);

		_ = builder.FromRemote("origin".As<GitRemoteName>()).WithBranch("main".As<GitBranchName>());

		string[] arguments = [.. builder.BuildArguments()];
		int marker = Array.IndexOf(arguments, "--end-of-options");

		Assert.AreEqual("origin", arguments[marker + 1]);
		Assert.AreEqual("main", arguments[marker + 2]);
	}

	[TestMethod]
	public void RejectsABranchWithoutARemote()
	{
		RecordingGitProcessRunner runner = new();
		GitPullBuilder builder = new(runner, TestPaths.Root);

		_ = builder.WithBranch("main".As<GitBranchName>());

		Assert.ThrowsExactly<InvalidOperationException>(() => _ = builder.BuildArguments());
	}

	[TestMethod]
	public void RejectsNullArguments()
	{
		RecordingGitProcessRunner runner = new();
		GitPullBuilder builder = new(runner, TestPaths.Root);

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.FromRemote(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.WithBranch(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.ReportingProgress(null!));
	}

	[TestMethod]
	public async Task ExecuteSucceedsOnACleanPullAsync()
	{
		RecordingGitProcessRunner runner = new()
		{
			StandardOutput = "Updating 4bafe6a..0631bf6\nFast-forward\n c.txt | 1 +\n",
		};
		GitPullBuilder builder = new(runner, TestPaths.Root);

		GitCompleted completed = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		CollectionAssert.AreEqual(builder.BuildArguments().ToArray(), completed.Arguments.ToArray());
	}

	[TestMethod]
	public async Task ThrowsConflictWhenTheMergeLeavesConflictsAsync()
	{
		// Captured from git 2.50: the conflict is reported on STANDARD OUTPUT with exit 128, so the
		// base classifier — which reads standard error — cannot see it.
		RecordingGitProcessRunner runner = new()
		{
			ExitCode = 128,
			StandardOutput = ConflictOutput,
			StandardError = "From C:/dev/origin\n * branch main -> FETCH_HEAD\n",
		};
		GitPullBuilder builder = new(runner, TestPaths.Root);

		GitPullConflictException exception = await Assert.ThrowsExactlyAsync<GitPullConflictException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.AreEqual(128, exception.ExitCode);
		StringAssert.Contains(exception.Message, "c.txt");
	}

	[TestMethod]
	public async Task RecognisesARebaseConflictTooAsync()
	{
		// A rebase reports its conflicts with different prose but the same "CONFLICT" marker, and
		// leaves the repository mid-rebase rather than mid-merge. Both are conflicts to a caller.
		RecordingGitProcessRunner runner = new()
		{
			ExitCode = 1,
			StandardOutput = "CONFLICT (content): Merge conflict in c.txt\n",
			StandardError = "error: could not apply 0631bf6... mine\n",
		};
		GitPullBuilder builder = new(runner, TestPaths.Root);

		await Assert.ThrowsExactlyAsync<GitPullConflictException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task AnOrdinaryPullFailureStaysAGenericCommandExceptionAsync()
	{
		RecordingGitProcessRunner runner = new()
		{
			ExitCode = 128,
			StandardError = "fatal: 'nosuch' does not appear to be a git repository\n",
		};
		GitPullBuilder builder = new(runner, TestPaths.Root);

		await Assert.ThrowsExactlyAsync<GitCommandException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task TryExecuteReportsAConflictWithItsDiagnosticTextAsync()
	{
		// The conflict text is on standard output, so an error built only from standard error would
		// carry the fetch progress and nothing about the conflict.
		RecordingGitProcessRunner runner = new()
		{
			ExitCode = 128,
			StandardOutput = ConflictOutput,
			StandardError = string.Empty,
		};
		GitPullBuilder builder = new(runner, TestPaths.Root);

		GitResult<GitCompleted> result =
			await builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsFalse(result.Success);
		StringAssert.Contains(result.Error?.StandardError ?? string.Empty, "CONFLICT");
	}

	[TestMethod]
	public async Task TryExecuteJoinsStandardErrorAndStandardOutputWithASingleNewlineAsync()
	{
		// The join itself is what the correction was about: two present parts must land separated
		// by exactly one newline, not concatenated raw. Asserting only that both substrings appear
		// would still pass if the join regressed to plain concatenation, so this checks the exact
		// joined string instead.
		const string FetchProgress = "From C:/dev/origin\n * branch main -> FETCH_HEAD\n";
		RecordingGitProcessRunner runner = new()
		{
			ExitCode = 128,
			StandardOutput = ConflictOutput,
			StandardError = FetchProgress,
		};
		GitPullBuilder builder = new(runner, TestPaths.Root);

		GitResult<GitCompleted> result =
			await builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		string expected = "From C:/dev/origin\n * branch main -> FETCH_HEAD" + "\n" + ConflictOutput;
		Assert.AreEqual(expected, result.Error?.StandardError);
	}

	[TestMethod]
	public async Task TryExecuteReportsStandardErrorAloneWithNoTrailingNewlineAsync()
	{
		// The common plain failure: standard output is empty, so joining unconditionally would leave
		// a trailing blank line after the trimmed standard error, defeating the trim's own purpose.
		RecordingGitProcessRunner runner = new()
		{
			ExitCode = 128,
			StandardOutput = string.Empty,
			StandardError = "fatal: 'nosuch' does not appear to be a git repository\n",
		};
		GitPullBuilder builder = new(runner, TestPaths.Root);

		GitResult<GitCompleted> result =
			await builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(
			"fatal: 'nosuch' does not appear to be a git repository",
			result.Error?.StandardError);
	}

	[TestMethod]
	public async Task ForwardsProgressToTheRequestAsync()
	{
		// A pull writes its fetch phase's transfer progress to standard error as it runs, so the
		// sink has to reach the request rather than being accepted and dropped.
		RecordingGitProcessRunner runner = new();
		GitPullBuilder builder = new(runner, TestPaths.Root);
		Progress<string> progress = new(static _ => { });

		_ = builder.ReportingProgress(progress);
		_ = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsNotNull(runner.LastRequest);
		Assert.AreSame(progress, runner.LastRequest.Progress);
	}

	public TestContext TestContext { get; set; } = null!;
}
