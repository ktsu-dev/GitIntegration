// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

/// <summary>
/// Describes a git invocation that exited non-zero.
/// </summary>
public sealed record GitCommandError
{
	/// <summary>Gets the exit code git returned.</summary>
	public required int ExitCode { get; init; }

	/// <summary>Gets the argument vector that produced the failure.</summary>
	public required IReadOnlyList<string> Arguments { get; init; }

	/// <summary>Gets everything git wrote to standard error.</summary>
	public required string StandardError { get; init; }
}

/// <summary>
/// The outcome of a git command that was allowed to fail without throwing.
/// </summary>
/// <typeparam name="T">The parsed result type on success.</typeparam>
public readonly record struct GitResult<T>
{
	/// <summary>Gets a value indicating whether the command succeeded.</summary>
	public bool Success { get; private init; }

	/// <summary>Gets the parsed result, or <see langword="null"/> when the command failed.</summary>
	public T? Value { get; private init; }

	/// <summary>Gets the failure detail, or <see langword="null"/> when the command succeeded.</summary>
	public GitCommandError? Error { get; private init; }

	/// <summary>Creates a successful result.</summary>
	/// <param name="value">The parsed result.</param>
	/// <returns>A successful result carrying <paramref name="value"/>.</returns>
	public static GitResult<T> FromValue(T value) => new() { Success = true, Value = value };

	/// <summary>Creates a failed result.</summary>
	/// <param name="error">The failure detail.</param>
	/// <returns>A failed result carrying <paramref name="error"/>.</returns>
	public static GitResult<T> FromError(GitCommandError error) => new() { Success = false, Error = error };
}
