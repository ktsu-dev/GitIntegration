// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

/// <summary>
/// The raw outcome of one git invocation, before any parsing.
/// </summary>
public sealed record GitProcessResult
{
	/// <summary>Gets the process exit code.</summary>
	public required int ExitCode { get; init; }

	/// <summary>Gets everything the process wrote to standard output.</summary>
	public required string StandardOutput { get; init; }

	/// <summary>Gets everything the process wrote to standard error.</summary>
	public required string StandardError { get; init; }

	/// <summary>Gets the argument vector that was passed to git, for diagnostics.</summary>
	public required IReadOnlyList<string> Arguments { get; init; }

	/// <summary>Gets a value indicating whether git exited with code zero.</summary>
	public bool Success => ExitCode == 0;
}
