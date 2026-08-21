// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Net;

/// <summary>
/// The base type for every failure a hosting provider reports back from an HTTP request.
/// </summary>
/// <remarks>
/// Derives from <see cref="Exception"/>, not from <see cref="GitException"/>. Every subtype of
/// <see cref="GitException"/> carries process concepts — an exit code, an argument vector — that an
/// HTTP failure simply does not have; forcing the inheritance would put meaningless members on both
/// sides of the tree. A hosting failure and a process failure are different kinds of thing, and
/// keeping their hierarchies separate is what lets each carry exactly the context it needs.
/// </remarks>
public class GitHostingException : Exception
{
	/// <summary>Gets the provider that reported the failure.</summary>
	public GitProviderName? ProviderName { get; }

	/// <summary>Gets the HTTP status code the provider returned.</summary>
	public HttpStatusCode StatusCode { get; }

	/// <summary>Gets the raw response body the provider returned.</summary>
	public string ResponseBody { get; } = string.Empty;

	/// <summary>Initializes a new instance of the <see cref="GitHostingException"/> class.</summary>
	public GitHostingException() { }

	/// <summary>Initializes a new instance of the <see cref="GitHostingException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	public GitHostingException(string message) : base(message) { }

	/// <summary>Initializes a new instance of the <see cref="GitHostingException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="innerException">The underlying failure.</param>
	public GitHostingException(string message, Exception innerException) : base(message, innerException) { }

	/// <summary>Initializes a new instance of the <see cref="GitHostingException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="providerName">The provider that reported the failure.</param>
	/// <param name="statusCode">The HTTP status code the provider returned.</param>
	/// <param name="responseBody">The raw response body the provider returned.</param>
	public GitHostingException(string message, GitProviderName providerName, HttpStatusCode statusCode, string responseBody)
		: base(message)
	{
		ProviderName = providerName;
		StatusCode = statusCode;
		ResponseBody = responseBody;
	}
}

/// <summary>
/// A hosting provider rejected the request because the caller was not authenticated, or the
/// credentials it presented no longer grant access.
/// </summary>
public sealed class GitHostingAuthenticationException : GitHostingException
{
	/// <summary>Initializes a new instance of the <see cref="GitHostingAuthenticationException"/> class.</summary>
	public GitHostingAuthenticationException() { }

	/// <summary>Initializes a new instance of the <see cref="GitHostingAuthenticationException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	public GitHostingAuthenticationException(string message) : base(message) { }

	/// <summary>Initializes a new instance of the <see cref="GitHostingAuthenticationException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="innerException">The underlying failure.</param>
	public GitHostingAuthenticationException(string message, Exception innerException) : base(message, innerException) { }

	/// <summary>Initializes a new instance of the <see cref="GitHostingAuthenticationException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="providerName">The provider that reported the failure.</param>
	/// <param name="statusCode">The HTTP status code the provider returned.</param>
	/// <param name="responseBody">The raw response body the provider returned.</param>
	public GitHostingAuthenticationException(string message, GitProviderName providerName, HttpStatusCode statusCode, string responseBody)
		: base(message, providerName, statusCode, responseBody) { }
}

/// <summary>
/// A hosting provider reported that the requested resource does not exist, or is not visible to
/// the caller's credentials.
/// </summary>
public sealed class GitHostingNotFoundException : GitHostingException
{
	/// <summary>Initializes a new instance of the <see cref="GitHostingNotFoundException"/> class.</summary>
	public GitHostingNotFoundException() { }

	/// <summary>Initializes a new instance of the <see cref="GitHostingNotFoundException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	public GitHostingNotFoundException(string message) : base(message) { }

	/// <summary>Initializes a new instance of the <see cref="GitHostingNotFoundException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="innerException">The underlying failure.</param>
	public GitHostingNotFoundException(string message, Exception innerException) : base(message, innerException) { }

	/// <summary>Initializes a new instance of the <see cref="GitHostingNotFoundException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="providerName">The provider that reported the failure.</param>
	/// <param name="statusCode">The HTTP status code the provider returned.</param>
	/// <param name="responseBody">The raw response body the provider returned.</param>
	public GitHostingNotFoundException(string message, GitProviderName providerName, HttpStatusCode statusCode, string responseBody)
		: base(message, providerName, statusCode, responseBody) { }
}

/// <summary>
/// A hosting provider reported that the caller has exhausted its request quota.
/// </summary>
public sealed class GitHostingRateLimitException : GitHostingException
{
	/// <summary>
	/// Gets the time at which the provider expects the caller's quota to reset, or
	/// <see langword="null"/> when the provider did not report one.
	/// </summary>
	public DateTimeOffset? ResetsAt { get; }

	/// <summary>Initializes a new instance of the <see cref="GitHostingRateLimitException"/> class.</summary>
	public GitHostingRateLimitException() { }

	/// <summary>Initializes a new instance of the <see cref="GitHostingRateLimitException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	public GitHostingRateLimitException(string message) : base(message) { }

	/// <summary>Initializes a new instance of the <see cref="GitHostingRateLimitException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="innerException">The underlying failure.</param>
	public GitHostingRateLimitException(string message, Exception innerException) : base(message, innerException) { }

	/// <summary>Initializes a new instance of the <see cref="GitHostingRateLimitException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="providerName">The provider that reported the failure.</param>
	/// <param name="statusCode">The HTTP status code the provider returned.</param>
	/// <param name="responseBody">The raw response body the provider returned.</param>
	/// <param name="resetsAt">
	/// The time at which the provider expects the caller's quota to reset, or
	/// <see langword="null"/> when the provider did not report one.
	/// </param>
	public GitHostingRateLimitException(
		string message,
		GitProviderName providerName,
		HttpStatusCode statusCode,
		string responseBody,
		DateTimeOffset? resetsAt)
		: base(message, providerName, statusCode, responseBody) => ResetsAt = resetsAt;
}

/// <summary>
/// A hosting provider rejected a request for a reason not covered by a more specific type in this
/// hierarchy — a malformed request, a validation failure, or a server-side error.
/// </summary>
public sealed class GitHostingRequestException : GitHostingException
{
	/// <summary>Initializes a new instance of the <see cref="GitHostingRequestException"/> class.</summary>
	public GitHostingRequestException() { }

	/// <summary>Initializes a new instance of the <see cref="GitHostingRequestException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	public GitHostingRequestException(string message) : base(message) { }

	/// <summary>Initializes a new instance of the <see cref="GitHostingRequestException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="innerException">The underlying failure.</param>
	public GitHostingRequestException(string message, Exception innerException) : base(message, innerException) { }

	/// <summary>Initializes a new instance of the <see cref="GitHostingRequestException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="providerName">The provider that reported the failure.</param>
	/// <param name="statusCode">The HTTP status code the provider returned.</param>
	/// <param name="responseBody">The raw response body the provider returned.</param>
	public GitHostingRequestException(string message, GitProviderName providerName, HttpStatusCode statusCode, string responseBody)
		: base(message, providerName, statusCode, responseBody) { }
}
