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
		// The command sleeps far longer than the bound, so the timeout is certain to fire whatever
		// the host's speed. See LongRunningCommand for why a short-lived process is unsafe here.
		(string executable, string[] arguments) = LongRunningCommand();

		TimeSpan timeout = TimeSpan.FromMilliseconds(50);
		RunCommandGitProcessRunner runner = new(new GitOptions
		{
			ExecutablePath = executable,
			Timeout = timeout,
		});

		GitTimeoutException exception = await Assert.ThrowsExactlyAsync<GitTimeoutException>(
			async () => await runner.RunAsync(new GitProcessRequest { Arguments = arguments }, TestContext.CancellationTokenSource.Token).ConfigureAwait(false)).ConfigureAwait(false);

		Assert.AreEqual(timeout, exception.Timeout);
	}

	[TestMethod]
	public async Task TimeoutNeverReturnsSilentlyWhenCancellationRacesProcessExitAsync()
	{
		// A 1ms bound against a long-running process means cancellation always arrives while the
		// process is still alive, which is when the kill-the-process registration can win the race
		// against the wait observing the token. ktsu.RunCommand 1.5.0 closed the silent-return path
		// upstream by re-checking the token after the await, so both deliveries now throw; this
		// keeps the runner's own post-return guard honest as defence in depth, and pins that a
		// timeout surfaces as GitTimeoutException no matter which path delivers it.
		//
		// Repeated because the delivery path is decided by OS scheduling, so a single pass would
		// exercise only whichever happened to win that time.
		(string executable, string[] arguments) = LongRunningCommand();

		RunCommandGitProcessRunner runner = new(new GitOptions
		{
			ExecutablePath = executable,
			Timeout = TimeSpan.FromMilliseconds(1),
		});

		for (int iteration = 0; iteration < 20; iteration++)
		{
			await Assert.ThrowsExactlyAsync<GitTimeoutException>(
				async () => await runner.RunAsync(new GitProcessRequest { Arguments = arguments }, TestContext.CancellationTokenSource.Token).ConfigureAwait(false)).ConfigureAwait(false);
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
		// Cancelling 1ms in, against a process that sleeps far longer, guarantees the token is
		// signalled while the process is still alive — which is the only window in which the
		// kill-the-process registration can win and drive execution into the guard. Repeated 50
		// times because which delivery path wins is decided by OS scheduling, so a single pass
		// would exercise only whichever won that time.
		//
		// Do not raise the delay: a bound long enough for the process to finish first would stop
		// this test exercising the branch it exists for. Earlier revisions ran a short-lived
		// `dotnet --info` here and depended on it outlasting the cancellation, which was a bet on
		// host speed — CI ran the equivalent invocation in roughly 19ms and broke two sibling
		// tests that made the same bet.
		(string executable, string[] arguments) = LongRunningCommand();

		RunCommandGitProcessRunner runner = new(new GitOptions
		{
			ExecutablePath = executable,
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
					new GitProcessRequest { Arguments = arguments },
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
	/// <summary>
	/// A command that runs far longer than any timeout under test, so that a timeout is guaranteed
	/// to fire on any host.
	/// </summary>
	/// <remarks>
	/// These tests previously ran <c>dotnet --version</c> and assumed it outlived a 50ms bound.
	/// That held on a developer machine but not on CI, which completed the same invocation in
	/// roughly 19ms — so the timeout never fired and the tests failed with "no exception was
	/// thrown". The bug was the assumption, not the bound: no fixed number is safe when the thing
	/// being outrun is an arbitrarily fast process. A process that sleeps far longer than any bound
	/// removes the race in the only direction that matters. Cancellation kills it immediately, so
	/// these tests still finish in milliseconds rather than waiting out the sleep.
	/// </remarks>
	private static (string Executable, string[] Arguments) LongRunningCommand()
	{
		if (OperatingSystem.IsWindows())
		{
			return ("powershell", ["-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30"]);
		}

		return ("sh", ["-c", "sleep 30"]);
	}

	private sealed class SynchronousProgress(Action<string> onReport) : IProgress<string>
	{
		public void Report(string value) => onReport(value);
	}
}
