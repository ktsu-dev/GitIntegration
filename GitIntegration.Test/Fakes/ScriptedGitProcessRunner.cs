// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Replays a queued sequence of results and records every argument vector it was given.
/// </summary>
/// <remarks>
/// <see cref="RecordingGitProcessRunner"/> replays one canned result, which is enough for a single
/// builder but not for <see cref="GitClient"/>: discovery runs <c>rev-parse --show-toplevel</c> and
/// then <c>remote get-url origin</c>, and the two need different answers.
/// </remarks>
internal sealed class ScriptedGitProcessRunner : IGitProcessRunner
{
	private readonly Queue<GitProcessResult> _responses = new();

	/// <summary>Gets every argument vector this runner was asked to run, in order.</summary>
	public List<IReadOnlyList<string>> Invocations { get; } = [];

	/// <summary>Gets every request this runner was given, in the same order as <see cref="Invocations"/>.</summary>
	/// <remarks>
	/// <see cref="RecordingGitProcessRunner"/> keeps only the last request, which is enough where a
	/// builder runs git once. A multi-invocation verb needs one request per invocation, because
	/// what distinguishes them is not the arguments alone — the fetch carries the caller's progress
	/// sink while the version probe preceding it deliberately does not.
	/// </remarks>
	public List<GitProcessRequest> Requests { get; } = [];

	/// <summary>Queues the next result this runner will return.</summary>
	/// <param name="standardOutput">What git writes to standard output.</param>
	/// <param name="standardError">What git writes to standard error.</param>
	/// <param name="exitCode">The code git exits with.</param>
	/// <returns>The same runner, to allow chaining.</returns>
	public ScriptedGitProcessRunner Then(string standardOutput = "", string standardError = "", int exitCode = 0)
	{
		_responses.Enqueue(new GitProcessResult
		{
			ExitCode = exitCode,
			StandardOutput = standardOutput,
			StandardError = standardError,
			Arguments = [],
		});

		return this;
	}

	public Task<GitProcessResult> RunAsync(GitProcessRequest request, CancellationToken cancellationToken = default)
	{
		// ArgumentNullException.ThrowIfNull rather than Ensure.NotNull: the library takes Polyfill
		// with PrivateAssets="all", so Ensure is not visible to the test project.
		ArgumentNullException.ThrowIfNull(request);

		Invocations.Add([.. request.Arguments]);
		Requests.Add(request);

		// Running out of queued responses means the code under test issued a command the test did
		// not anticipate. Failing here names that command; returning a default would hide it.
		if (_responses.Count == 0)
		{
			throw new InvalidOperationException(
				$"No queued result for: git {string.Join(' ', request.Arguments)}");
		}

		GitProcessResult queued = _responses.Dequeue();

		return Task.FromResult(queued with { Arguments = [.. request.Arguments] });
	}
}
