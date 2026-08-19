// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Builds and runs one git command, returning a parsed result.
/// </summary>
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
	public Task<TResult> ExecuteAsync(CancellationToken cancellationToken = default);

	/// <summary>Runs the command, reporting a non-zero exit as a result rather than an exception.</summary>
	/// <param name="cancellationToken">Cancels the invocation.</param>
	/// <returns>The parsed result, or the failure detail.</returns>
	public Task<GitResult<TResult>> TryExecuteAsync(CancellationToken cancellationToken = default);
}
