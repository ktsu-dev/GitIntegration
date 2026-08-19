// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Threading;
using System.Threading.Tasks;

[TestClass]
public class RunCommandGitProcessRunnerTests
{
	[TestMethod]
	public async Task CapturesStandardOutputAndExitCodeAsync()
	{
		// Uses the host's own dotnet executable rather than git, so this test does not
		// require git to be installed.
		RunCommandGitProcessRunner runner = new(new GitOptions { ExecutablePath = "dotnet" });

		GitProcessResult result = await runner.RunAsync(["--version"], TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(0, result.ExitCode);
		Assert.IsTrue(result.Success);
		Assert.IsFalse(string.IsNullOrWhiteSpace(result.StandardOutput));
	}

	[TestMethod]
	public async Task EchoesArgumentVectorBackOnTheResultAsync()
	{
		RunCommandGitProcessRunner runner = new(new GitOptions { ExecutablePath = "dotnet" });

		GitProcessResult result = await runner.RunAsync(["--version"], TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		string[] expectedArguments = ["--version"];
		CollectionAssert.AreEqual(expectedArguments, result.Arguments.ToArray());
	}

	[TestMethod]
	public async Task ThrowsExecutableNotFoundWhenBinaryIsMissingAsync()
	{
		RunCommandGitProcessRunner runner = new(new GitOptions
		{
			ExecutablePath = "definitely-not-a-real-executable-9f3a2b",
		});

		await Assert.ThrowsExactlyAsync<GitExecutableNotFoundException>(
			async () => await runner.RunAsync(["--version"], TestContext.CancellationTokenSource.Token).ConfigureAwait(false)).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task ReportsNonZeroExitCodeWithoutThrowingAsync()
	{
		RunCommandGitProcessRunner runner = new(new GitOptions { ExecutablePath = "dotnet" });

		GitProcessResult result = await runner.RunAsync(
			["--this-flag-does-not-exist"],
			TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreNotEqual(0, result.ExitCode);
		Assert.IsFalse(result.Success);
	}

	[TestMethod]
	public async Task CallerCancellationSurfacesAsOperationCanceledAsync()
	{
		// A generous timeout, so that only the caller's own cancellation can be responsible for
		// whatever exception surfaces.
		RunCommandGitProcessRunner runner = new(new GitOptions
		{
			ExecutablePath = "dotnet",
			Timeout = TimeSpan.FromMinutes(5),
		});

		using CancellationTokenSource alreadyCancelled = new();
		await alreadyCancelled.CancelAsync().ConfigureAwait(false);

		OperationCanceledException exception = await Assert.ThrowsExactlyAsync<OperationCanceledException>(
			async () => await runner.RunAsync(["--version"], alreadyCancelled.Token).ConfigureAwait(false)).ConfigureAwait(false);

		Assert.IsNotInstanceOfType<GitTimeoutException>(exception);
	}

	[TestMethod]
	public async Task TimeoutSurfacesAsGitTimeoutExceptionAsync()
	{
		// 50ms rather than the tightest possible bound: at 1ms the timer can fire before
		// ktsu.RunCommand starts the process, which races a different, non-throwing internal path
		// that returns exit code -1 without ever surfacing a cancellation. 50ms is comfortably
		// shorter than a `dotnet --version` invocation while reliably landing on the code path
		// that cancels the awaited task, so this test is deterministic. See task-5-report.md for
		// the measurements behind this choice.
		TimeSpan timeout = TimeSpan.FromMilliseconds(50);
		RunCommandGitProcessRunner runner = new(new GitOptions
		{
			ExecutablePath = "dotnet",
			Timeout = timeout,
		});

		GitTimeoutException exception = await Assert.ThrowsExactlyAsync<GitTimeoutException>(
			async () => await runner.RunAsync(["--version"], TestContext.CancellationTokenSource.Token).ConfigureAwait(false)).ConfigureAwait(false);

		Assert.AreEqual(timeout, exception.Timeout);
	}

	[TestMethod]
	public async Task TimeoutNeverReturnsSilentlyWhenCancellationRacesProcessExitAsync()
	{
		// 1ms is deliberate, not a typo to "fix" upward: it is short enough that the kill-the-process
		// registration frequently wins the race against WaitForExitAsync observing the token, which is
		// exactly the silent-return path this test exists to close off. Raising this value to something
		// comfortable (e.g. the 50ms used above) would stop the race from firing and silently gut the
		// test's ability to catch a regression here.
		RunCommandGitProcessRunner runner = new(new GitOptions
		{
			ExecutablePath = "dotnet",
			Timeout = TimeSpan.FromMilliseconds(1),
		});

		for (int iteration = 0; iteration < 20; iteration++)
		{
			await Assert.ThrowsExactlyAsync<GitTimeoutException>(
				async () => await runner.RunAsync(["--version"], TestContext.CancellationTokenSource.Token).ConfigureAwait(false)).ConfigureAwait(false);
		}
	}

	public TestContext TestContext { get; set; } = null!;
}
