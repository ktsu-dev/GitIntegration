// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;

/// <summary>
/// Configures how the git executable is located and invoked.
/// </summary>
public sealed class GitOptions
{
	/// <summary>
	/// Gets or sets the git executable to invoke. A bare name is resolved through <c>PATH</c>;
	/// an absolute path is used as given.
	/// </summary>
	public string ExecutablePath { get; set; } = "git";

	/// <summary>
	/// Gets or sets a wall-clock bound on a single git invocation, or <see langword="null"/> to
	/// leave invocations unbounded.
	/// </summary>
	/// <remarks>
	/// A bound matters because <c>ktsu.RunCommand</c> cannot set environment variables, so
	/// <c>GIT_TERMINAL_PROMPT=0</c> cannot be applied and a remote operation may otherwise block
	/// indefinitely waiting for credentials that will never be typed.
	/// </remarks>
	public TimeSpan? Timeout { get; set; } = TimeSpan.FromMinutes(5);
}
