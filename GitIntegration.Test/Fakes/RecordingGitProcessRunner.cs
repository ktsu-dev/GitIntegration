// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Captures the argument vector a builder produces and replays canned output, so builder tests
/// never need a git binary.
/// </summary>
internal sealed class RecordingGitProcessRunner : IGitProcessRunner
{
	public IReadOnlyList<string>? LastArguments { get; private set; }

	public string StandardOutput { get; set; } = string.Empty;

	public string StandardError { get; set; } = string.Empty;

	public int ExitCode { get; set; }

	public Task<GitProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
	{
		LastArguments = arguments;

		return Task.FromResult(new GitProcessResult
		{
			ExitCode = ExitCode,
			StandardOutput = StandardOutput,
			StandardError = StandardError,
			Arguments = arguments,
		});
	}
}
