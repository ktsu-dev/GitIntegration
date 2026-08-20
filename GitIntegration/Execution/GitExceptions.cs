// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;

/// <summary>
/// The base type for every failure originating in this library.
/// </summary>
public class GitException : Exception
{
	/// <summary>Initializes a new instance of the <see cref="GitException"/> class.</summary>
	public GitException() { }

	/// <summary>Initializes a new instance of the <see cref="GitException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	public GitException(string message) : base(message) { }

	/// <summary>Initializes a new instance of the <see cref="GitException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="innerException">The underlying failure.</param>
	public GitException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// The git executable could not be started. Carries no exit code, because nothing ran.
/// </summary>
public sealed class GitExecutableNotFoundException : GitException
{
	/// <summary>Initializes a new instance of the <see cref="GitExecutableNotFoundException"/> class.</summary>
	public GitExecutableNotFoundException() { }

	/// <summary>Initializes a new instance of the <see cref="GitExecutableNotFoundException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	public GitExecutableNotFoundException(string message) : base(message) { }

	/// <summary>Initializes a new instance of the <see cref="GitExecutableNotFoundException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="innerException">The underlying failure.</param>
	public GitExecutableNotFoundException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Git ran and exited with a non-zero code.
/// </summary>
public class GitCommandException : GitException
{
	/// <summary>Gets the exit code git returned.</summary>
	/// <remarks>
	/// Defaults to <c>-1</c>, not <c>0</c>, when the parameterless, message-only, or
	/// message-and-inner-exception constructors are used. <c>0</c> is what this codebase treats as
	/// success (see <see cref="GitProcessResult.Success"/>), so defaulting to it here would make a
	/// catch block reading an unset exit code see something that looks like success instead of
	/// "no data".
	/// </remarks>
	public int ExitCode { get; } = -1;

	/// <summary>Gets the argument vector that produced the failure.</summary>
	public IReadOnlyList<string> Arguments { get; } = [];

	/// <summary>Gets everything git wrote to standard error.</summary>
	public string StandardError { get; } = string.Empty;

	/// <summary>Initializes a new instance of the <see cref="GitCommandException"/> class.</summary>
	public GitCommandException() { }

	/// <summary>Initializes a new instance of the <see cref="GitCommandException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	public GitCommandException(string message) : base(message) { }

	/// <summary>Initializes a new instance of the <see cref="GitCommandException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="innerException">The underlying failure.</param>
	public GitCommandException(string message, Exception innerException) : base(message, innerException) { }

	/// <summary>Initializes a new instance of the <see cref="GitCommandException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="exitCode">The exit code git returned.</param>
	/// <param name="arguments">The argument vector that produced the failure.</param>
	/// <param name="standardError">Everything git wrote to standard error.</param>
	public GitCommandException(string message, int exitCode, IReadOnlyList<string> arguments, string standardError)
		: base(message)
	{
		ExitCode = exitCode;
		Arguments = arguments;
		StandardError = standardError;
	}
}

/// <summary>
/// Git did not complete within <see cref="GitOptions.Timeout"/> and was terminated.
/// </summary>
/// <remarks>
/// Distinct from <see cref="OperationCanceledException"/>, which means the caller cancelled. The
/// distinction matters because a timeout is a candidate for retry while a caller's cancellation is
/// not, and because <c>ktsu.RunCommand</c> cannot set <c>GIT_TERMINAL_PROMPT=0</c>, so a remote
/// operation blocking on a credential prompt reaches the caller as a timeout.
/// </remarks>
public sealed class GitTimeoutException : GitException
{
	/// <summary>Gets the bound that was exceeded.</summary>
	public TimeSpan Timeout { get; }

	/// <summary>Initializes a new instance of the <see cref="GitTimeoutException"/> class.</summary>
	public GitTimeoutException() { }

	/// <summary>Initializes a new instance of the <see cref="GitTimeoutException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	public GitTimeoutException(string message) : base(message) { }

	/// <summary>Initializes a new instance of the <see cref="GitTimeoutException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="innerException">The underlying failure.</param>
	public GitTimeoutException(string message, Exception innerException) : base(message, innerException) { }

	/// <summary>Initializes a new instance of the <see cref="GitTimeoutException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="timeout">The bound that was exceeded.</param>
	/// <param name="innerException">The cancellation that terminated the process.</param>
	public GitTimeoutException(string message, TimeSpan timeout, Exception innerException)
		: base(message, innerException) => Timeout = timeout;
}

/// <summary>
/// The path given is not inside a git working tree.
/// </summary>
public sealed class GitRepositoryNotFoundException : GitCommandException
{
	/// <summary>Initializes a new instance of the <see cref="GitRepositoryNotFoundException"/> class.</summary>
	public GitRepositoryNotFoundException() { }

	/// <summary>Initializes a new instance of the <see cref="GitRepositoryNotFoundException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	public GitRepositoryNotFoundException(string message) : base(message) { }

	/// <summary>Initializes a new instance of the <see cref="GitRepositoryNotFoundException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="innerException">The underlying failure.</param>
	public GitRepositoryNotFoundException(string message, Exception innerException) : base(message, innerException) { }

	/// <summary>Initializes a new instance of the <see cref="GitRepositoryNotFoundException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="exitCode">The exit code git returned.</param>
	/// <param name="arguments">The argument vector that produced the failure.</param>
	/// <param name="standardError">Everything git wrote to standard error.</param>
	public GitRepositoryNotFoundException(string message, int exitCode, IReadOnlyList<string> arguments, string standardError)
		: base(message, exitCode, arguments, standardError) { }
}

/// <summary>
/// Git ran successfully but produced output this library could not interpret.
/// </summary>
/// <remarks>
/// Distinct from <see cref="GitCommandException"/>, which means git itself reported a failure.
/// This means the invocation succeeded and the output did not match the machine-readable format
/// the parser was written against — a git version emitting a shape we do not know, or a value git
/// permits that the corresponding <c>ktsu.Semantics</c> type refuses, such as a path containing a
/// newline on Windows.
/// </remarks>
public sealed class GitParseException : GitException
{
	/// <summary>Initializes a new instance of the <see cref="GitParseException"/> class.</summary>
	public GitParseException() { }

	/// <summary>Initializes a new instance of the <see cref="GitParseException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	public GitParseException(string message) : base(message) { }

	/// <summary>Initializes a new instance of the <see cref="GitParseException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="innerException">The underlying failure.</param>
	public GitParseException(string message, Exception innerException) : base(message, innerException) { }
}
