// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Runs the git executable with a given argument vector.
/// </summary>
/// <remarks>
/// The contract takes an argument vector rather than a command string on purpose. Flattening
/// arguments into a single string would hand them to a shell for re-splitting, which corrupts any
/// argument containing a quote, backtick, dollar sign, or ampersand — a commit message, for
/// instance — and turns caller-supplied text into a shell injection vector.
/// </remarks>
public interface IGitProcessRunner
{
	/// <summary>
	/// Runs git with the supplied arguments and captures its output.
	/// </summary>
	/// <param name="arguments">The argument vector, each element unquoted and unescaped.</param>
	/// <param name="cancellationToken">Cancels the invocation, terminating the process tree.</param>
	/// <returns>The exit code and captured output.</returns>
	/// <exception cref="GitExecutableNotFoundException">The git executable could not be started.</exception>
	public Task<GitProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default);
}
