// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Builds and runs one git command, returning a parsed result.
/// </summary>
/// <remarks>
/// A builder is single-use and not thread-safe. It carries the operands accumulated for one
/// invocation, so it must not be shared between threads or reused for a second command; construct
/// a fresh builder per command. The underlying <see cref="IGitProcessRunner"/> is the opposite: it
/// is a shared singleton and safe to call concurrently.
/// </remarks>
/// <typeparam name="TResult">The parsed result type.</typeparam>
public interface IGitCommandBuilder<TResult>
{
	/// <summary>
	/// Gets the exact argument vector this builder will pass to git.
	/// </summary>
	/// <remarks>
	/// This is a pure computation with no I/O, which makes the produced command directly
	/// assertable in tests and inspectable when diagnosing an unexpected result.
	/// </remarks>
	/// <returns>The argument vector.</returns>
	public IReadOnlyList<string> BuildArguments();

	/// <summary>Runs the command, throwing when git exits non-zero.</summary>
	/// <param name="cancellationToken">Cancels the invocation.</param>
	/// <returns>The parsed result.</returns>
	/// <exception cref="GitCommandException">Git exited with a non-zero code.</exception>
	/// <exception cref="GitExecutableNotFoundException">The git executable could not be started.</exception>
	/// <exception cref="GitTimeoutException">Git did not complete within the configured bound.</exception>
	/// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
	public Task<TResult> ExecuteAsync(CancellationToken cancellationToken = default);

	/// <summary>Runs the command, reporting a non-zero exit as a result rather than an exception.</summary>
	/// <remarks>
	/// Only a non-zero exit code is suppressed. Everything that prevents git from producing an
	/// exit code at all still throws: the executable not being found, the configured timeout
	/// elapsing, and the caller's own cancellation. A <see cref="GitResult{T}"/> reporting failure
	/// therefore always means "git ran and said no", never "git never ran".
	/// </remarks>
	/// <param name="cancellationToken">Cancels the invocation.</param>
	/// <returns>The parsed result, or the failure detail.</returns>
	/// <exception cref="GitExecutableNotFoundException">The git executable could not be started.</exception>
	/// <exception cref="GitTimeoutException">Git did not complete within the configured bound.</exception>
	/// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
	public Task<GitResult<TResult>> TryExecuteAsync(CancellationToken cancellationToken = default);
}
