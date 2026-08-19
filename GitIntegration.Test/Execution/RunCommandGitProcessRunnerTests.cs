// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Text;
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

		GitProcessResult result = await runner.RunAsync(new GitProcessRequest { Arguments = ["--version"] }, TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(0, result.ExitCode);
		Assert.IsTrue(result.Success);
		Assert.IsFalse(string.IsNullOrWhiteSpace(result.StandardOutput));
	}

	[TestMethod]
	public async Task EchoesArgumentVectorBackOnTheResultAsync()
	{
		RunCommandGitProcessRunner runner = new(new GitOptions { ExecutablePath = "dotnet" });

		GitProcessResult result = await runner.RunAsync(new GitProcessRequest { Arguments = ["--version"] }, TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

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
			async () => await runner.RunAsync(new GitProcessRequest { Arguments = ["--version"] }, TestContext.CancellationTokenSource.Token).ConfigureAwait(false)).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task ReportsNonZeroExitCodeWithoutThrowingAsync()
	{
		RunCommandGitProcessRunner runner = new(new GitOptions { ExecutablePath = "dotnet" });

		GitProcessResult result = await runner.RunAsync(
			new GitProcessRequest { Arguments = ["--this-flag-does-not-exist"] },
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
			async () => await runner.RunAsync(new GitProcessRequest { Arguments = ["--version"] }, alreadyCancelled.Token).ConfigureAwait(false)).ConfigureAwait(false);

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
			async () => await runner.RunAsync(new GitProcessRequest { Arguments = ["--version"] }, TestContext.CancellationTokenSource.Token).ConfigureAwait(false)).ConfigureAwait(false);

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
				async () => await runner.RunAsync(new GitProcessRequest { Arguments = ["--version"] }, TestContext.CancellationTokenSource.Token).ConfigureAwait(false)).ConfigureAwait(false);
		}
	}

	[TestMethod]
	public async Task CallerCancellationMidRunSurfacesAsOperationCanceledAsync()
	{
		// CallerCancellationSurfacesAsOperationCanceledAsync uses a pre-cancelled token, so
		// ktsu.RunCommand throws at its own entry point and the post-return guard in RunAsync is
		// never reached. This test cancels while the invocation is in flight, which is what can
		// drive execution into that guard, where the caller's cancellation must be re-raised as a
		// plain OperationCanceledException rather than being misreported as a timeout.
		//
		// 1ms and 50 iterations, both measured rather than guessed. Which of the two delivery
		// paths wins is a race, and the guard is only reached when the kill-the-process
		// registration wins — which in practice happens around process startup, the same window
		// TimeoutNeverReturnsSilentlyWhenCancellationRacesProcessExitAsync targets with its 1ms
		// timeout. A delay sweep over 1-250ms hit the guard only at 1ms and 6ms (3 of 500
		// attempts); at a fixed 20ms it was never hit in 60 attempts. At 1ms, 50 iterations hit it
		// in 5 of 5 runs (1 to 11 hits per run), while 20 iterations missed entirely in 1 of 4.
		// Do not raise the delay or lower the iteration count: either silently stops this test
		// exercising the branch it exists for.
		RunCommandGitProcessRunner runner = new(new GitOptions
		{
			ExecutablePath = "dotnet",
			Timeout = TimeSpan.FromMinutes(5),
		});

		for (int iteration = 0; iteration < 50; iteration++)
		{
			using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(1));

			// ThrowsAsync, not ThrowsExactlyAsync: the two delivery paths produce different derived
			// types. When ktsu.RunCommand's awaited WaitForExitAsync observes the token first, the
			// TaskCanceledException it raises propagates as-is; when the kill-the-process
			// registration wins instead, the post-return guard re-raises the caller's cancellation
			// as a plain OperationCanceledException. Both are correct; neither may be a timeout.
			OperationCanceledException exception = await Assert.ThrowsAsync<OperationCanceledException>(
				async () => await runner.RunAsync(
					new GitProcessRequest { Arguments = ["--info"] },
					cancellation.Token).ConfigureAwait(false)).ConfigureAwait(false);

			// The generous timeout cannot have elapsed, so a GitTimeoutException here would mean
			// the caller's cancellation was misclassified.
			Assert.IsNotInstanceOfType<GitTimeoutException>(exception);
		}
	}

	[TestMethod]
	public async Task ProgressReceivesOutputWhileTheProcessIsStillRunningAsync()
	{
		RunCommandGitProcessRunner runner = new(new GitOptions { ExecutablePath = "dotnet" });

		int returned = 0;
		bool reportedAfterReturn = false;
		StringBuilder reported = new();

		// A synchronous sink rather than System.Progress<T>, which marshals its callback onto the
		// thread pool and would make the ordering this test asserts non-deterministic.
		SynchronousProgress progress = new(chunk =>
		{
			if (Volatile.Read(ref returned) != 0)
			{
				reportedAfterReturn = true;
			}

			lock (reported)
			{
				reported.Append(chunk);
			}
		});

		GitProcessResult result = await runner.RunAsync(
			new GitProcessRequest { Arguments = ["--info"], Progress = progress },
			TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Volatile.Write(ref returned, 1);

		string reportedText = reported.ToString();

		Assert.IsFalse(string.IsNullOrEmpty(reportedText));

		// Every report landed before RunAsync returned, which is what "incremental" means here:
		// output is surfaced as git produces it, not replayed once the process has exited.
		Assert.IsFalse(reportedAfterReturn);

		// And nothing was reported that was not also accumulated, nor vice versa.
		Assert.AreEqual(result.StandardOutput.Length + result.StandardError.Length, reportedText.Length);
	}

	[TestMethod]
	public async Task NoProgressSinkIsNotAnErrorAsync()
	{
		RunCommandGitProcessRunner runner = new(new GitOptions { ExecutablePath = "dotnet" });

		GitProcessResult result = await runner.RunAsync(
			new GitProcessRequest { Arguments = ["--version"] },
			TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsTrue(result.Success);
	}

	[TestMethod]
	public async Task RejectsNullRequestAsync()
	{
		RunCommandGitProcessRunner runner = new(new GitOptions { ExecutablePath = "dotnet" });

		await Assert.ThrowsExactlyAsync<ArgumentNullException>(
			async () => await runner.RunAsync(null!, TestContext.CancellationTokenSource.Token).ConfigureAwait(false)).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task MutatingOptionsAfterConstructionDoesNotChangeBehaviourAsync()
	{
		GitOptions options = new() { ExecutablePath = "dotnet" };
		RunCommandGitProcessRunner runner = new(options);

		// GitOptions is a mutable singleton in the container, so a consumer resolving it and
		// setting a property must not be able to redirect an already-constructed runner.
		options.ExecutablePath = "definitely-not-a-real-executable-9f3a2b";
		options.Timeout = TimeSpan.FromMilliseconds(1);

		GitProcessResult result = await runner.RunAsync(
			new GitProcessRequest { Arguments = ["--version"] },
			TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsTrue(result.Success);
	}

	[TestMethod]
	public async Task ForcedEnvironmentReachesTheChildProcessAsync()
	{
		// Asserts the variables actually arrive in the spawned process, not merely that the
		// overlay dictionary contains them. Uses the platform shell to echo them back, since no
		// portable single command prints an environment variable.
		string executable;
		string[] arguments;

		if (OperatingSystem.IsWindows())
		{
			executable = "cmd";
			arguments = ["/c", "echo %GIT_TERMINAL_PROMPT%-%LC_ALL%"];
		}
		else
		{
			executable = "sh";
			arguments = ["-c", "echo $GIT_TERMINAL_PROMPT-$LC_ALL"];
		}

		RunCommandGitProcessRunner runner = new(new GitOptions { ExecutablePath = executable });

		GitProcessResult result = await runner.RunAsync(
			new GitProcessRequest { Arguments = arguments },
			TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(0, result.ExitCode);
		Assert.AreEqual("0-C", result.StandardOutput.Trim());
	}

	[TestMethod]
	public void EnvironmentOverlayForcesNonInteractiveEnglishGit()
	{
		Assert.AreEqual("0", RunCommandGitProcessRunner.EnvironmentOverlay["GIT_TERMINAL_PROMPT"]);
		Assert.AreEqual("C", RunCommandGitProcessRunner.EnvironmentOverlay["LC_ALL"]);
	}

	public TestContext TestContext { get; set; } = null!;

	/// <summary>An <see cref="IProgress{T}"/> that invokes its callback on the reporting thread.</summary>
	private sealed class SynchronousProgress(Action<string> onReport) : IProgress<string>
	{
		public void Report(string value) => onReport(value);
	}
}
