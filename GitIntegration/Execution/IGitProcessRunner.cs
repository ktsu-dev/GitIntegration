// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Runs the git executable with a given argument vector.
/// </summary>
/// <remarks>
/// <para>
/// The contract takes an argument vector rather than a command string on purpose. Flattening
/// arguments into a single string would hand them to a shell for re-splitting, which corrupts any
/// argument containing a quote, backtick, dollar sign, or ampersand — a commit message, for
/// instance — and turns caller-supplied text into a shell injection vector.
/// </para>
/// <para>
/// Implementations must be safe to call concurrently from multiple threads, because a single
/// runner is registered as a singleton and shared by every builder. Command builders, by contrast,
/// are single-use and not thread-safe: see <see cref="IGitCommandBuilder{TResult}"/>.
/// </para>
/// </remarks>
public interface IGitProcessRunner
{
	/// <summary>
	/// Runs git as described by the request and captures its output.
	/// </summary>
	/// <param name="request">The argument vector and any optional progress sink.</param>
	/// <param name="cancellationToken">Cancels the invocation, terminating the process tree.</param>
	/// <returns>The exit code and captured output.</returns>
	/// <exception cref="GitExecutableNotFoundException">The git executable could not be started.</exception>
	/// <exception cref="GitTimeoutException">
	/// Git did not complete within the configured <see cref="GitOptions.Timeout"/> and was
	/// terminated. Distinct from <see cref="OperationCanceledException"/>, because a timeout is a
	/// candidate for retry while the caller's own cancellation is not.
	/// </exception>
	/// <exception cref="OperationCanceledException">
	/// <paramref name="cancellationToken"/> was signalled and git did not exit successfully.
	/// If git completes with exit code 0 before the token is observed, the result is returned normally.
	/// </exception>
	public Task<GitProcessResult> RunAsync(GitProcessRequest request, CancellationToken cancellationToken = default);
}
