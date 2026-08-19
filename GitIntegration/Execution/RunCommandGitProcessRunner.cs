// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using ktsu.RunCommand;

/// <summary>
/// Runs git through <c>ktsu.RunCommand</c>, using its argument-vector overload so that no shell
/// is involved and no argument needs manual quoting.
/// </summary>
/// <param name="options">Configures the executable location and the per-invocation timeout.</param>
public sealed class RunCommandGitProcessRunner(GitOptions options) : IGitProcessRunner
{
	// Snapshotted at construction rather than read per invocation. GitOptions is registered as a
	// mutable singleton, so any consumer that resolves it and sets a property would otherwise
	// change the behaviour of every other consumer mid-flight. The properties cannot simply be
	// made init-only: the Action<GitOptions> configure delegate receives an already-constructed
	// instance, and init accessors are settable only during object initialization.
	private readonly string _executablePath = Ensure.NotNull(options).ExecutablePath;
	private readonly TimeSpan? _timeout = Ensure.NotNull(options).Timeout;

	/// <summary>
	/// The environment applied over the inherited one for every git invocation.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>GIT_TERMINAL_PROMPT=0</c> stops git blocking forever on a credential prompt. Output is
	/// redirected and no terminal is attached, so a prompt could never be answered; without this,
	/// a fetch or push against a repository needing credentials hangs until the timeout fires.
	/// With it, git fails immediately and says why.
	/// </para>
	/// <para>
	/// <c>LC_ALL=C</c> forces English, machine-stable output. Git translates its messages when the
	/// host has the catalogs installed, which would otherwise make every message-matching decision
	/// in this library — and every parser built on it — silently locale-dependent.
	/// </para>
	/// <para>
	/// Both were impossible before <c>ktsu.RunCommand</c> 1.5.0, which added
	/// <see cref="CommandOptions.EnvironmentVariables"/>. The entries are an overlay, so every
	/// other variable the calling process had is inherited unchanged.
	/// </para>
	/// </remarks>
	internal static IReadOnlyDictionary<string, string?> EnvironmentOverlay { get; } =
		new Dictionary<string, string?>(StringComparer.Ordinal)
		{
			["GIT_TERMINAL_PROMPT"] = "0",
			["LC_ALL"] = "C",
		};

	/// <inheritdoc />
	public async Task<GitProcessResult> RunAsync(GitProcessRequest request, CancellationToken cancellationToken = default)
	{
		Ensure.NotNull(request);

		IReadOnlyList<string> arguments = request.Arguments;
		IProgress<string>? progress = request.Progress;

		StringBuilder standardOutput = new();
		StringBuilder standardError = new();

		// Chunks are reported to the progress sink as ktsu.RunCommand delivers them, which is what
		// makes push and fetch observable while they are still running, rather than only after the
		// process exits.
		OutputHandler outputHandler = new(
			onStandardOutput: chunk =>
			{
				standardOutput.Append(chunk);
				progress?.Report(chunk);
			},
			onStandardError: chunk =>
			{
				standardError.Append(chunk);
				progress?.Report(chunk);
			});

		using CancellationTokenSource linked = CreateLinkedTokenSource(cancellationToken);

		int exitCode;
		try
		{
			// Fully qualified on purpose: `RunCommand` names both the namespace
			// `ktsu.RunCommand` and the static class `ktsu.RunCommand.RunCommand`, so the short
			// form is ambiguous to read even where it compiles.
			exitCode = await ktsu.RunCommand.RunCommand.ExecuteAsync(
				_executablePath,
				arguments,
				outputHandler,
				new CommandOptions
				{
					Elevation = Elevation.Default,
					EnvironmentVariables = EnvironmentOverlay,
				},
				linked.Token).ConfigureAwait(false);
		}
		catch (Win32Exception ex)
		{
			// Thrown when the executable cannot be found or started at all.
			throw new GitExecutableNotFoundException(
				$"Could not start the git executable '{_executablePath}'. Is git installed and on PATH?",
				ex);
		}
		catch (OperationCanceledException ex) when (_timeout is not null && !cancellationToken.IsCancellationRequested)
		{
			// The caller's own token was not signalled, so this cancellation can only have come from
			// the internal timer started in CreateLinkedTokenSource.
			throw new GitTimeoutException(
				$"git did not complete within {_timeout.Value}.",
				_timeout.Value,
				ex);
		}

		// RunCommand delivers cancellation two ways at once: a registration that kills the process, and
		// WaitForExitAsync observing the token. When the kill wins that race the process exits before the
		// await faults, so ExecuteAsync returns normally carrying a killed process's exit code (-1 on
		// Windows) and a cancelled run is indistinguishable from an ordinary git failure. Classify it the
		// same way the catch clause does, so both paths reach the caller with identical semantics.
		//
		// Gated on a non-zero exit code as well, because the token being signalled does not by
		// itself mean the process was killed: git can exit successfully microseconds before the
		// timeout timer fires, and discarding that valid result as a timeout would be wrong. A
		// killed process never exits 0 on any platform, so a zero exit code proves git finished
		// on its own.
		if (linked.IsCancellationRequested && exitCode != 0)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (_timeout is TimeSpan timeout)
			{
				throw new GitTimeoutException(
					$"git did not complete within {timeout}.",
					timeout,
					new OperationCanceledException(linked.Token));
			}

			throw new OperationCanceledException(linked.Token);
		}

		return new GitProcessResult
		{
			ExitCode = exitCode,
			StandardOutput = standardOutput.ToString(),
			StandardError = standardError.ToString(),

			// Copied, not aliased: the caller's list must not be observable through the result,
			// and a caller holding a mutable List<string> could otherwise change what the result
			// reports the command was.
			Arguments = [.. arguments],
		};
	}

	private CancellationTokenSource CreateLinkedTokenSource(CancellationToken cancellationToken)
	{
		CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

		if (_timeout is TimeSpan timeout)
		{
			linked.CancelAfter(timeout);
		}

		return linked;
	}
}
