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
/// <remarks>
/// <para>
/// This is a sealed record class, not a struct, so that there is no <c>default</c> instance to
/// accidentally observe. A struct with independently-set members would let
/// <c>default(GitResult&lt;T&gt;)</c> — reachable via an uninitialized field, an array, or a failed
/// dictionary lookup — report success with a null error, or (if <c>Success</c> were instead derived
/// from <c>Error</c>) success with a null value. Neither reading is safe for a consumer that
/// reasonably dereferences the field matching the branch it is on. Restricting construction to
/// <see cref="FromValue(T)"/> and <see cref="FromError(GitCommandError)"/> makes every instance one
/// or the other, never both undefined.
/// </para>
/// <para>
/// <typeparamref name="T"/> is constrained to <c>notnull</c>, and <see cref="FromValue(T)"/>
/// rejects a null value the way <see cref="FromError(GitCommandError)"/> rejects a null error.
/// Without both, <c>FromValue(null)</c> would report <see cref="Success"/> with nothing for the
/// success branch to read — the one case the guarantee above does not survive. The constraint stops
/// short of <c>class</c> because two result types are value types: <c>rev-list</c> counts commits
/// into an <see cref="int"/>, and <c>Divergence()</c> answers with a
/// <see cref="GitDivergence"/> struct.
/// </para>
/// </remarks>
public sealed record GitResult<T>
	where T : notnull
{
	private GitResult() { }

	/// <summary>Gets a value indicating whether the command succeeded.</summary>
	public bool Success => Error is null;

	/// <summary>Gets the parsed result, or <see langword="null"/> when the command failed.</summary>
	public T? Value { get; private init; }

	/// <summary>Gets the failure detail, or <see langword="null"/> when the command succeeded.</summary>
	public GitCommandError? Error { get; private init; }

	/// <summary>Creates a successful result.</summary>
	/// <param name="value">The parsed result.</param>
	/// <returns>A successful result carrying <paramref name="value"/>.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="value"/> is <see langword="null"/>. <see cref="Success"/> is derived from
	/// <see cref="Error"/> alone, so a null value here would report success while leaving nothing for
	/// the success branch to read — the one reading this type's remarks promise is safe.
	/// </exception>
	public static GitResult<T> FromValue(T value)
	{
		// The notnull constraint is a compile-time check a caller can suppress, and the extension
		// point this guards — a derived GitCommandBuilder whose ParseResult returns null — is
		// reachable from outside this assembly, so the invariant is enforced at run time too.
		//
		// Ensure.NotNull constrains its own argument to a reference type, which T is not: the
		// constraint above stops at notnull for GitResult<int> and GitResult<GitDivergence>. Casting
		// to object satisfies that constraint for the reference-type instantiations, where null is
		// the only place it is reachable. typeof(T).IsValueType is a per-instantiation constant, so
		// the value-type instantiations fold this check away instead of boxing on every result.
		if (!typeof(T).IsValueType)
		{
			Ensure.NotNull((object?)value, nameof(value));
		}

		return new() { Value = value };
	}

	/// <summary>Creates a failed result.</summary>
	/// <param name="error">The failure detail.</param>
	/// <returns>A failed result carrying <paramref name="error"/>.</returns>
	public static GitResult<T> FromError(GitCommandError error)
	{
		Ensure.NotNull(error);
		return new() { Error = error };
	}
}
