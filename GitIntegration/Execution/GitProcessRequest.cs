// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;

/// <summary>Describes one git invocation.</summary>
/// <remarks>
/// A record rather than a widening parameter list on <see cref="IGitProcessRunner.RunAsync"/>:
/// adding a parameter to a shipped public interface is a binary break for anyone who implemented
/// it. Working-directory and environment-variable support are tracked upstream
/// (ktsu-dev/RunCommand issues #39 and #40), and when they land this record absorbs them without
/// another break.
/// </remarks>
public sealed record GitProcessRequest
{
	/// <summary>Gets the argument vector, each element unquoted and unescaped.</summary>
	public required IReadOnlyList<string> Arguments { get; init; }

	/// <summary>
	/// Gets an optional sink for incremental output, reported as git produces it rather than
	/// after the process exits. Used by long-running remote operations.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The sink may be entered concurrently by the standard-output and standard-error readers.
	/// Implementations must be thread-safe.
	/// </para>
	/// <para>
	/// An exception thrown by the sink does not reach the caller. <see cref="RunCommandGitProcessRunner"/>
	/// stops reporting to that sink, lets git run to completion, and returns the full output, so
	/// the result is the same as if no sink had been attached. Another <see cref="IGitProcessRunner"/>
	/// implementation is expected to do the same.
	/// </para>
	/// </remarks>
	public IProgress<string>? Progress { get; init; }
}
