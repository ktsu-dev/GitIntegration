// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Captures the request a builder produces and replays canned output, so builder tests never need
/// a git binary.
/// </summary>
internal sealed class RecordingGitProcessRunner : IGitProcessRunner
{
	public GitProcessRequest? LastRequest { get; private set; }

	public IReadOnlyList<string>? LastArguments => LastRequest?.Arguments;

	public string StandardOutput { get; set; } = string.Empty;

	public string StandardError { get; set; } = string.Empty;

	public int ExitCode { get; set; }

	public Task<GitProcessResult> RunAsync(GitProcessRequest request, CancellationToken cancellationToken = default)
	{
		// ArgumentNullException.ThrowIfNull rather than Ensure.NotNull: the library takes Polyfill
		// with PrivateAssets="all", so Ensure is not visible to the test project.
		ArgumentNullException.ThrowIfNull(request);

		LastRequest = request;

		// Replayed through the progress sink as well, so a builder that supplies one is exercised
		// the same way the real runner would exercise it.
		if (!string.IsNullOrEmpty(StandardOutput))
		{
			request.Progress?.Report(StandardOutput);
		}

		if (!string.IsNullOrEmpty(StandardError))
		{
			request.Progress?.Report(StandardError);
		}

		return Task.FromResult(new GitProcessResult
		{
			ExitCode = ExitCode,
			StandardOutput = StandardOutput,
			StandardError = StandardError,
			Arguments = [.. request.Arguments],
		});
	}
}
