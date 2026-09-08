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
	/// <remarks>
	/// <see langword="null"/> unless a constructor taking a <see cref="GitProviderName"/> was used.
	/// The parameterless and message-only constructors exist because the analyzers require the
	/// standard exception constructor set, not because this library ever calls them, and neither has
	/// a provider to record.
	/// </remarks>
	/// <value>The provider, or <see langword="null"/> when none was recorded.</value>
	public GitProviderName? ProviderName { get; }

	/// <summary>Gets the HTTP status code the provider returned.</summary>
	/// <remarks>
	/// Defaults to <c>0</c>, which is not a real status code, under the same constructors that leave
	/// <see cref="ProviderName"/> <see langword="null"/>. Test <see cref="ProviderName"/> rather than
	/// this property when distinguishing a failure carrying host context from one that does not:
	/// <see cref="HttpStatusCode"/> has no member meaning "unset", so <c>0</c> cannot be told apart
	/// from a status by its type alone.
	/// </remarks>
	/// <value>The status code, or <c>0</c> when none was recorded.</value>
	public HttpStatusCode StatusCode { get; }

	/// <summary>Gets the raw response body the provider returned.</summary>
	/// <remarks>Empty when none was recorded, and empty when the provider sent no body.</remarks>
	/// <value>The response body, never <see langword="null"/>.</value>
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
		: this(message, providerName, statusCode, responseBody, innerException: null)
	{
	}

	/// <summary>Initializes a new instance of the <see cref="GitHostingException"/> class.</summary>
	/// <remarks>
	/// The overload a provider translating a vendor SDK's own exception uses. Without it the
	/// translated exception is all a caller ever sees, and detail the host supplied but this
	/// hierarchy has no field for is lost — Octokit's <c>ApiError.Errors</c>, which is often the only
	/// place GitHub explains what was actually wrong with a request, and Octokit's synthetic 404,
	/// which carries no <c>HttpResponse</c> at all and so reaches a caller with an empty
	/// <see cref="ResponseBody"/> and nothing else.
	/// </remarks>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="providerName">The provider that reported the failure.</param>
	/// <param name="statusCode">The HTTP status code the provider returned.</param>
	/// <param name="responseBody">The raw response body the provider returned.</param>
	/// <param name="innerException">The underlying failure, or <see langword="null"/> when there is none.</param>
	public GitHostingException(string message, GitProviderName providerName, HttpStatusCode statusCode, string responseBody, Exception? innerException)
		: base(message, innerException)
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

	/// <summary>Initializes a new instance of the <see cref="GitHostingAuthenticationException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="providerName">The provider that reported the failure.</param>
	/// <param name="statusCode">The HTTP status code the provider returned.</param>
	/// <param name="responseBody">The raw response body the provider returned.</param>
	/// <param name="innerException">The underlying failure, or <see langword="null"/> when there is none.</param>
	public GitHostingAuthenticationException(string message, GitProviderName providerName, HttpStatusCode statusCode, string responseBody, Exception? innerException)
		: base(message, providerName, statusCode, responseBody, innerException) { }
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

	/// <summary>Initializes a new instance of the <see cref="GitHostingNotFoundException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="providerName">The provider that reported the failure.</param>
	/// <param name="statusCode">The HTTP status code the provider returned.</param>
	/// <param name="responseBody">The raw response body the provider returned.</param>
	/// <param name="innerException">The underlying failure, or <see langword="null"/> when there is none.</param>
	public GitHostingNotFoundException(string message, GitProviderName providerName, HttpStatusCode statusCode, string responseBody, Exception? innerException)
		: base(message, providerName, statusCode, responseBody, innerException) { }
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
		: this(message, providerName, statusCode, responseBody, resetsAt, innerException: null)
	{
	}

	/// <summary>Initializes a new instance of the <see cref="GitHostingRateLimitException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="providerName">The provider that reported the failure.</param>
	/// <param name="statusCode">The HTTP status code the provider returned.</param>
	/// <param name="responseBody">The raw response body the provider returned.</param>
	/// <param name="resetsAt">
	/// The time at which the provider expects the caller's quota to reset, or
	/// <see langword="null"/> when the provider did not report one.
	/// </param>
	/// <param name="innerException">The underlying failure, or <see langword="null"/> when there is none.</param>
	public GitHostingRateLimitException(
		string message,
		GitProviderName providerName,
		HttpStatusCode statusCode,
		string responseBody,
		DateTimeOffset? resetsAt,
		Exception? innerException)
		: base(message, providerName, statusCode, responseBody, innerException) => ResetsAt = resetsAt;
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

	/// <summary>Initializes a new instance of the <see cref="GitHostingRequestException"/> class.</summary>
	/// <param name="message">The message describing the failure.</param>
	/// <param name="providerName">The provider that reported the failure.</param>
	/// <param name="statusCode">The HTTP status code the provider returned.</param>
	/// <param name="responseBody">The raw response body the provider returned.</param>
	/// <param name="innerException">The underlying failure, or <see langword="null"/> when there is none.</param>
	public GitHostingRequestException(string message, GitProviderName providerName, HttpStatusCode statusCode, string responseBody, Exception? innerException)
		: base(message, providerName, statusCode, responseBody, innerException) { }
}
