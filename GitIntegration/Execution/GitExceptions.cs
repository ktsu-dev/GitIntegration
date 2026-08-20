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
/// not. A credential prompt is not among the causes: every invocation runs with
/// <c>GIT_TERMINAL_PROMPT=0</c>, so a remote operation needing credentials it does not have fails
/// immediately and says so, rather than waiting out the bound with nothing to report.
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
/// <c>git commit</c> was run with nothing staged.
/// </summary>
/// <remarks>
/// Given its own type because it is the one <c>commit</c> failure that is an ordinary program state
/// rather than a fault — a caller that commits on a timer, or after a no-op edit, hits it routinely
/// and wants to carry on. The alternative is matching on exit code 1, which <c>commit</c> shares
/// with other failures, or on git's English prose, which every caller would then have to duplicate.
/// </remarks>
public sealed class GitNothingToCommitException : GitCommandException
{
	/// <summary>Initializes a new instance of the <see cref="GitNothingToCommitException"/> class.</summary>
	public GitNothingToCommitException() { }

	/// <summary>Initializes a new instance of the <see cref="GitNothingToCommitException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	public GitNothingToCommitException(string message) : base(message) { }

	/// <summary>Initializes a new instance of the <see cref="GitNothingToCommitException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="innerException">The underlying failure.</param>
	public GitNothingToCommitException(string message, Exception innerException) : base(message, innerException) { }

	/// <summary>Initializes a new instance of the <see cref="GitNothingToCommitException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="exitCode">The exit code git returned.</param>
	/// <param name="arguments">The argument vector that produced the failure.</param>
	/// <param name="standardError">Everything git wrote to standard error.</param>
	public GitNothingToCommitException(string message, int exitCode, IReadOnlyList<string> arguments, string standardError)
		: base(message, exitCode, arguments, standardError) { }
}

/// <summary>
/// Git refused at least one of the references a push tried to update.
/// </summary>
/// <remarks>
/// Carries the parsed <see cref="Result"/> because this is the one failure in the library whose
/// output is exactly what the caller wanted: a rejected push exits non-zero and still prints a
/// complete porcelain record naming every reference and why each was refused. Throwing that detail
/// away would leave a caller with an exit code and some prose.
/// </remarks>
public sealed class GitPushRejectedException : GitCommandException
{
	/// <summary>Gets the parsed push result, or <see langword="null"/> when none was available.</summary>
	public GitPushResult? Result { get; }

	/// <summary>Initializes a new instance of the <see cref="GitPushRejectedException"/> class.</summary>
	public GitPushRejectedException() { }

	/// <summary>Initializes a new instance of the <see cref="GitPushRejectedException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	public GitPushRejectedException(string message) : base(message) { }

	/// <summary>Initializes a new instance of the <see cref="GitPushRejectedException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="innerException">The underlying failure.</param>
	public GitPushRejectedException(string message, Exception innerException) : base(message, innerException) { }

	/// <summary>Initializes a new instance of the <see cref="GitPushRejectedException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="exitCode">The exit code git returned.</param>
	/// <param name="arguments">The argument vector that produced the failure.</param>
	/// <param name="standardError">Everything git wrote to standard error.</param>
	public GitPushRejectedException(string message, int exitCode, IReadOnlyList<string> arguments, string standardError)
		: base(message, exitCode, arguments, standardError) { }

	/// <summary>Initializes a new instance of the <see cref="GitPushRejectedException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="exitCode">The exit code git returned.</param>
	/// <param name="arguments">The argument vector that produced the failure.</param>
	/// <param name="standardError">Everything git wrote to standard error.</param>
	/// <param name="result">The parsed account of what happened to each reference.</param>
	public GitPushRejectedException(
		string message,
		int exitCode,
		IReadOnlyList<string> arguments,
		string standardError,
		GitPushResult result)
		: base(message, exitCode, arguments, standardError) => Result = result;
}

/// <summary>
/// A pull merged cleanly enough to start but left conflicts in the working tree.
/// </summary>
/// <remarks>
/// <para>
/// The repository is now mid-merge, which no other verb in this library produces. Resolving it is
/// a non-goal of this design, but the state is inspectable: <c>status</c> reports each conflicted
/// path as an unmerged entry, so a caller can find out exactly what needs attention through
/// <c>GitRepository.Status()</c>.
/// </para>
/// <para>
/// Given its own type because git reports the conflict on standard <em>output</em> while exiting
/// 128, so without a dedicated classification it would be indistinguishable from any other pull
/// failure.
/// </para>
/// </remarks>
public sealed class GitPullConflictException : GitCommandException
{
	/// <summary>Initializes a new instance of the <see cref="GitPullConflictException"/> class.</summary>
	public GitPullConflictException() { }

	/// <summary>Initializes a new instance of the <see cref="GitPullConflictException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	public GitPullConflictException(string message) : base(message) { }

	/// <summary>Initializes a new instance of the <see cref="GitPullConflictException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="innerException">The underlying failure.</param>
	public GitPullConflictException(string message, Exception innerException) : base(message, innerException) { }

	/// <summary>Initializes a new instance of the <see cref="GitPullConflictException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="exitCode">The exit code git returned.</param>
	/// <param name="arguments">The argument vector that produced the failure.</param>
	/// <param name="standardError">Everything git wrote to standard error.</param>
	public GitPullConflictException(string message, int exitCode, IReadOnlyList<string> arguments, string standardError)
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
