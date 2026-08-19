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
	private GitOptions Options { get; } = Ensure.NotNull(options);

	/// <inheritdoc />
	public async Task<GitProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
	{
		Ensure.NotNull(arguments);

		StringBuilder standardOutput = new();
		StringBuilder standardError = new();

		OutputHandler outputHandler = new(
			onStandardOutput: chunk => standardOutput.Append(chunk),
			onStandardError: chunk => standardError.Append(chunk));

		using CancellationTokenSource linked = CreateLinkedTokenSource(cancellationToken);

		int exitCode;
		try
		{
			// Fully qualified on purpose: `RunCommand` names both the namespace
			// `ktsu.RunCommand` and the static class `ktsu.RunCommand.RunCommand`, so the short
			// form is ambiguous to read even where it compiles.
			exitCode = await ktsu.RunCommand.RunCommand.ExecuteAsync(
				Options.ExecutablePath,
				arguments,
				outputHandler,
				Elevation.Default,
				linked.Token).ConfigureAwait(false);
		}
		catch (Win32Exception ex)
		{
			// Thrown when the executable cannot be found or started at all.
			throw new GitExecutableNotFoundException(
				$"Could not start the git executable '{Options.ExecutablePath}'. Is git installed and on PATH?",
				ex);
		}
		catch (OperationCanceledException ex) when (Options.Timeout is not null && !cancellationToken.IsCancellationRequested)
		{
			// The caller's own token was not signalled, so this cancellation can only have come from
			// the internal timer started in CreateLinkedTokenSource.
			throw new GitTimeoutException(
				$"git did not complete within {Options.Timeout.Value}.",
				Options.Timeout.Value,
				ex);
		}

		return new GitProcessResult
		{
			ExitCode = exitCode,
			StandardOutput = standardOutput.ToString(),
			StandardError = standardError.ToString(),
			Arguments = arguments,
		};
	}

	private CancellationTokenSource CreateLinkedTokenSource(CancellationToken cancellationToken)
	{
		CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

		if (Options.Timeout is TimeSpan timeout)
		{
			linked.CancelAfter(timeout);
		}

		return linked;
	}
}
