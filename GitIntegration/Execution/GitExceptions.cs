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
	public int ExitCode { get; }

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
