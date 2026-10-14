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
	private const string UpdatedReference = "refs/remotes/origin/main";

	// A record is <flag><separator-space><old-id> <new-id> <local-ref>. Built through Record, as
	// GitFetchParserTests does, so the flag and its separator are both present — writing the
	// leading space by hand would shift every field one character left, letting a fast-forward
	// record's object ids silently parse one digit short while a count-only assertion still passed.
	private static string Record(string flag, string oldSha, string newSha, string reference) =>
		flag + " " + oldSha + " " + newSha + " " + reference + "\n";

	private static readonly string PorcelainOutput = Record(" ", OldSha, NewSha, UpdatedReference);

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
	public void RejectsAskingForBothAllRemotesAndFromRemote()
	{
		// Real git dies at the remote with "fatal: fetch --all does not take a repository argument"
		// (exit 128) rather than rejecting this at parse time. Pull rejects its own equivalent
		// contradiction before spawning a process, and fetch should be no less consistent.
		RecordingGitProcessRunner runner = new();
		GitFetchBuilder builder = new(runner, TestPaths.Root);

		_ = builder.AllRemotes().FromRemote("origin".As<GitRemoteName>());

		Assert.ThrowsExactly<InvalidOperationException>(() => _ = builder.BuildArguments());
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

		// Asserted on the parsed fields, not just the count: a fixture or parser that shifted a
		// field by one character would still produce exactly one update and pass a count-only check.
		GitRefUpdate update = result.Updates[0];
		Assert.AreEqual(UpdatedReference.As<GitRefName>(), update.Reference);
		Assert.AreEqual(OldSha.As<GitCommitSha>(), update.OldSha);
		Assert.AreEqual(NewSha.As<GitCommitSha>(), update.NewSha);

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

	private const string TagReference = "refs/tags/v1";

	// What git 2.43 prints for "fetch --porcelain origin --tags" when the remote moved a tag the
	// local clone already has: the branch updates normally, the tag is refused with "!", the exit
	// code is 1, and nothing at all goes to standard error.
	private static readonly string RejectedTagOutput =
		Record(" ", OldSha, NewSha, UpdatedReference) + Record("!", OldSha, NewSha, TagReference);

	[TestMethod]
	public async Task ThrowsFetchRejectedCarryingEveryUpdateWhenAReferenceIsRefusedAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: "git version 2.50.1\n")
			.Then(standardOutput: RejectedTagOutput, exitCode: 1);
		GitFetchBuilder builder = new(runner, TestPaths.Root);

		GitFetchRejectedException exception = await Assert.ThrowsExactlyAsync<GitFetchRejectedException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.AreEqual(1, exception.ExitCode);
		Assert.IsNotNull(exception.Result);
		Assert.IsTrue(exception.Result.HasRejections);
		Assert.AreEqual(2, exception.Result.Updates.Count);
		Assert.AreEqual(GitRefUpdateKind.FastForward, exception.Result.Updates[0].Kind);
		Assert.AreEqual(GitRefUpdateKind.Rejected, exception.Result.Updates[1].Kind);
		StringAssert.Contains(exception.Message, TagReference);
	}

	[TestMethod]
	public async Task TryExecuteReturnsARefusedReferenceAsAValueAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: "git version 2.50.1\n")
			.Then(standardOutput: RejectedTagOutput, exitCode: 1);
		GitFetchBuilder builder = new(runner, TestPaths.Root);

		GitResult<GitFetchResult> result =
			await builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsTrue(result.Success);
		Assert.IsTrue(result.Value!.HasRejections);
		Assert.AreEqual(UpdatedReference.As<GitRefName>(), result.Value.Updates[0].Reference);
		Assert.AreEqual(TagReference.As<GitRefName>(), result.Value.Updates[1].Reference);
	}

	[TestMethod]
	public async Task AFailureThatRefusedNothingStaysAnErrorAsync()
	{
		// Updates listed before an unrelated failure, such as one of several remotes being
		// unreachable, must not turn that failure into a value.
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: "git version 2.50.1\n")
			.Then(standardOutput: PorcelainOutput, standardError: "fatal: could not read from remote\n", exitCode: 1)
			.Then(standardOutput: "git version 2.50.1\n")
			.Then(standardOutput: PorcelainOutput, standardError: "fatal: could not read from remote\n", exitCode: 1);
		GitFetchBuilder builder = new(runner, TestPaths.Root);

		GitCommandException exception = await Assert.ThrowsExactlyAsync<GitCommandException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);
		StringAssert.Contains(exception.Message, "could not read from remote");

		GitResult<GitFetchResult> result =
			await builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);
		Assert.IsFalse(result.Success);
	}

	[TestMethod]
	public async Task AFailureWithUnreadablePorcelainStaysAnErrorAsync()
	{
		// Whatever git printed alongside a failure, the failure is what gets reported, not a
		// GitParseException about the output that came with it.
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: "git version 2.50.1\n")
			.Then(standardOutput: "! x\n", standardError: "fatal: broken\n", exitCode: 1)
			.Then(standardOutput: "git version 2.50.1\n")
			.Then(standardOutput: "! x\n", standardError: "fatal: broken\n", exitCode: 1);
		GitFetchBuilder builder = new(runner, TestPaths.Root);

		GitCommandException exception = await Assert.ThrowsExactlyAsync<GitCommandException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);
		StringAssert.Contains(exception.Message, "broken");

		GitResult<GitFetchResult> result =
			await builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);
		Assert.IsFalse(result.Success);
	}

	[TestMethod]
	public async Task FallsBackToStandardOutputWhenStandardErrorIsEmptyAsync()
	{
		// No fetch failure should end in an empty diagnostic. Below git 2.41 nothing is parsed, so
		// this reaches the plain error path with the explanation only on standard output.
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: "git version 2.40.1\n")
			.Then(standardOutput: "error: some reference was refused\n", exitCode: 1)
			.Then(standardOutput: "git version 2.40.1\n")
			.Then(standardOutput: "error: some reference was refused\n", exitCode: 1);
		GitFetchBuilder builder = new(runner, TestPaths.Root);

		GitCommandException exception = await Assert.ThrowsExactlyAsync<GitCommandException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);
		StringAssert.Contains(exception.Message, "some reference was refused");

		GitResult<GitFetchResult> result =
			await builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);
		Assert.IsFalse(result.Success);
		StringAssert.Contains(result.Error!.StandardError, "some reference was refused");
	}

	[TestMethod]
	public async Task TryExecuteDegradesToUnsupportedWhenTheVersionProbeFailsAsync()
	{
		// The probe goes through TryExecuteAsync so a broken git here cannot throw a version
		// exception out from under a caller that chose the non-throwing entry point. A failed probe
		// means the version is genuinely unknown, so degrading to unsupported is the truthful
		// answer, and the fetch itself still runs immediately after.
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardError: "fatal: could not determine version\n", exitCode: 1)
			.Then(standardOutput: string.Empty);
		GitFetchBuilder builder = new(runner, TestPaths.Root);

		GitResult<GitFetchResult> result =
			await builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsTrue(result.Success);
		Assert.IsFalse(result.Value!.DetailAvailable);
		CollectionAssert.DoesNotContain(runner.Invocations[1].ToArray(), "--porcelain");
	}

	[TestMethod]
	public async Task ForwardsProgressToTheFetchRequestAsync()
	{
		ScriptedGitProcessRunner runner = RunnerOn("2.50.1", PorcelainOutput);
		GitFetchBuilder builder = new(runner, TestPaths.Root);
		Progress<string> progress = new(static _ => { });

		_ = builder.ReportingProgress(progress);
		_ = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(2, runner.Requests.Count);

		// The version probe runs without the caller's sink on purpose: it is an implementation
		// detail of the fetch, and reporting "git version 2.50.1" into a stream the caller asked to
		// be about transfer progress would be noise. Asserting the fetch request alone would leave
		// that deliberate asymmetry unpinned.
		Assert.IsNull(runner.Requests[0].Progress);
		Assert.AreSame(progress, runner.Requests[1].Progress);
	}

	public TestContext TestContext { get; set; } = null!;
}
