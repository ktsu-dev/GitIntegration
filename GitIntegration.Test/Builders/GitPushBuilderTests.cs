// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Threading.Tasks;

using ktsu.Semantics.Strings;

[TestClass]
public class GitPushBuilderTests
{
	private const string RejectedOutput =
		"To C:/dev/origin.git\n" +
		"!\trefs/heads/main:refs/heads/main\t[rejected] (fetch first)\n" +
		"Done\n";

	private const string AcceptedOutput =
		"To C:/dev/origin.git\n" +
		"*\trefs/heads/main:refs/heads/main\t[new branch]\n" +
		"Done\n";

	[TestMethod]
	public void BuildsTheDefaultPushVector()
	{
		RecordingGitProcessRunner runner = new();
		GitPushBuilder builder = new(runner, TestPaths.Root);

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"push",
			"--porcelain",
		];
		CollectionAssert.AreEqual(expectedArguments, builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void MapsTheOptionFlags()
	{
		RecordingGitProcessRunner runner = new();
		GitPushBuilder builder = new(runner, TestPaths.Root);

		_ = builder.SettingUpstream().Force().DryRun().DeletingRemoteBranch();

		string[] arguments = [.. builder.BuildArguments()];
		CollectionAssert.Contains(arguments, "--set-upstream");
		CollectionAssert.Contains(arguments, "--force");
		CollectionAssert.Contains(arguments, "--dry-run");
		CollectionAssert.Contains(arguments, "--delete");
	}

	[TestMethod]
	public void ForceWithLeaseReplacesAPlainForce()
	{
		// --force and --force-with-lease both overwrite, but the lease refuses when the remote
		// moved since it was last fetched. Emitting both would let the blunter one win silently.
		RecordingGitProcessRunner runner = new();
		GitPushBuilder builder = new(runner, TestPaths.Root);

		_ = builder.Force().ForceWithLease();

		string[] arguments = [.. builder.BuildArguments()];
		CollectionAssert.Contains(arguments, "--force-with-lease");
		CollectionAssert.DoesNotContain(arguments, "--force");
	}

	[TestMethod]
	public void PutsTheRemoteAndBranchBehindTheEndOfOptionsMarkerInOrder()
	{
		// git push <remote> <refspec> is positional; reversed, git looks for a remote named after
		// the branch.
		RecordingGitProcessRunner runner = new();
		GitPushBuilder builder = new(runner, TestPaths.Root);

		_ = builder.ToRemote("origin".As<GitRemoteName>()).WithBranch("main".As<GitBranchName>());

		string[] arguments = [.. builder.BuildArguments()];
		int marker = Array.IndexOf(arguments, "--end-of-options");

		Assert.AreNotEqual(-1, marker);
		Assert.AreEqual("origin", arguments[marker + 1]);
		Assert.AreEqual("main", arguments[marker + 2]);
	}

	[TestMethod]
	public void PutsTheRemoteAloneBehindTheEndOfOptionsMarkerWhenNoBranchIsGiven()
	{
		// The remote-only refspec path: distinct from remote+branch (marker followed by two operands)
		// and from neither (no marker at all).
		RecordingGitProcessRunner runner = new();
		GitPushBuilder builder = new(runner, TestPaths.Root);

		_ = builder.ToRemote("origin".As<GitRemoteName>());

		string[] arguments = [.. builder.BuildArguments()];
		int marker = Array.IndexOf(arguments, "--end-of-options");

		Assert.AreNotEqual(-1, marker);
		Assert.AreEqual("origin", arguments[marker + 1]);
		Assert.AreEqual(marker + 2, arguments.Length);
	}

	[TestMethod]
	public void EmitsNoMarkerWhenNeitherRemoteNorBranchIsGiven()
	{
		RecordingGitProcessRunner runner = new();
		GitPushBuilder builder = new(runner, TestPaths.Root);

		CollectionAssert.DoesNotContain(builder.BuildArguments().ToArray(), "--end-of-options");
	}

	[TestMethod]
	public void RejectsABranchWithoutARemote()
	{
		// git push <refspec> with no remote pushes to the configured upstream and reads the first
		// operand as the remote, so a branch alone would be silently misread.
		RecordingGitProcessRunner runner = new();
		GitPushBuilder builder = new(runner, TestPaths.Root);

		_ = builder.WithBranch("main".As<GitBranchName>());

		Assert.ThrowsExactly<InvalidOperationException>(() => _ = builder.BuildArguments());
	}

	[TestMethod]
	public void RejectsNullArguments()
	{
		RecordingGitProcessRunner runner = new();
		GitPushBuilder builder = new(runner, TestPaths.Root);

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.ToRemote(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.WithBranch(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.ReportingProgress(null!));
	}

	[TestMethod]
	public async Task ExecuteReturnsTheParsedResultOnSuccessAsync()
	{
		RecordingGitProcessRunner runner = new() { StandardOutput = AcceptedOutput };
		GitPushBuilder builder = new(runner, TestPaths.Root);

		GitPushResult result = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(1, result.Updates.Count);
		Assert.IsFalse(result.HasRejections);
	}

	[TestMethod]
	public async Task ExecuteThrowsRejectedAndCarriesTheParsedResultAsync()
	{
		// The heart of this verb: git exits 1 and still prints a full account, so the exception
		// has to carry it or the caller learns nothing they could act on.
		RecordingGitProcessRunner runner = new() { ExitCode = 1, StandardOutput = RejectedOutput };
		GitPushBuilder builder = new(runner, TestPaths.Root);

		GitPushRejectedException exception = await Assert.ThrowsExactlyAsync<GitPushRejectedException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.IsNotNull(exception.Result);
		Assert.IsTrue(exception.Result.HasRejections);
		Assert.AreEqual("[rejected] (fetch first)", exception.Result.Updates[0].Summary);
		Assert.AreEqual(1, exception.ExitCode);
	}

	[TestMethod]
	public async Task TryExecuteReturnsTheResultRatherThanAnErrorForARejectionAsync()
	{
		// The deliberate divergence: git ran and told us exactly what happened, so "try" reports it
		// as a value the caller inspects rather than as an opaque failure.
		RecordingGitProcessRunner runner = new() { ExitCode = 1, StandardOutput = RejectedOutput };
		GitPushBuilder builder = new(runner, TestPaths.Root);

		GitResult<GitPushResult> result =
			await builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsTrue(result.Success);
		Assert.IsNotNull(result.Value);
		Assert.IsTrue(result.Value.HasRejections);
	}

	[TestMethod]
	public async Task AGenuineFailureStillThrowsACommandExceptionAsync()
	{
		// No porcelain records at all means git never got as far as talking about references —
		// a bad remote, a network failure, an auth failure. That is an ordinary command failure.
		RecordingGitProcessRunner runner = new()
		{
			ExitCode = 128,
			StandardError = "fatal: 'nosuch' does not appear to be a git repository\n",
		};
		GitPushBuilder builder = new(runner, TestPaths.Root);

		GitCommandException exception = await Assert.ThrowsExactlyAsync<GitCommandException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.AreEqual(128, exception.ExitCode);
	}

	[TestMethod]
	public async Task TryExecuteReportsAGenuineFailureAsAnErrorAsync()
	{
		RecordingGitProcessRunner runner = new()
		{
			ExitCode = 128,
			StandardError = "fatal: 'nosuch' does not appear to be a git repository\n",
		};
		GitPushBuilder builder = new(runner, TestPaths.Root);

		GitResult<GitPushResult> result =
			await builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsFalse(result.Success);
		Assert.AreEqual(128, result.Error?.ExitCode);
	}

	[TestMethod]
	public async Task ForwardsProgressToTheRequestAsync()
	{
		RecordingGitProcessRunner runner = new() { StandardOutput = AcceptedOutput };
		GitPushBuilder builder = new(runner, TestPaths.Root);

		_ = builder.ReportingProgress(new Progress<string>(static _ => { }));
		_ = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsNotNull(runner.LastRequest);
		Assert.IsNotNull(runner.LastRequest.Progress);
	}

	public TestContext TestContext { get; set; } = null!;
}
