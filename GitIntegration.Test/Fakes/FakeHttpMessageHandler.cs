// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Scripts HTTP responses and records complete requests, so hosting-provider tests never need a
/// real network call.
/// </summary>
/// <remarks>
/// Mirrors the split between <see cref="RecordingGitProcessRunner"/> and
/// <see cref="ScriptedGitProcessRunner"/>, but deliberately combines both roles in one type: this
/// handler both scripts responses and records the whole request it was given, headers and body
/// included. A fake that recorded only argument-vector-equivalents (method and URI, say) would let
/// a test claim to check an auth header while actually observing nothing about it.
/// </remarks>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
	private readonly Queue<QueuedResponse> _responses = new();

	private readonly List<RecordedRequest> _requests = [];

	private int _totalQueued;

	/// <summary>Gets every request this handler received, in order.</summary>
	public IReadOnlyList<RecordedRequest> Requests => _requests;

	/// <summary>Queues the next response this handler will return.</summary>
	/// <param name="status">The status code the response carries.</param>
	/// <param name="body">The response body, sent as UTF-8 text.</param>
	/// <param name="headers">
	/// Header name/value pairs to attach to the response. Each pair is routed to whichever header
	/// collection accepts it: content headers such as <c>Content-Type</c> go on
	/// <see cref="HttpContent.Headers"/>, everything else goes on
	/// <see cref="HttpResponseMessage.Headers"/>.
	/// </param>
	/// <returns>The same handler, to allow chaining.</returns>
	public FakeHttpMessageHandler Respond(HttpStatusCode status, string body, params (string Name, string Value)[] headers)
	{
		_responses.Enqueue(new QueuedResponse(status, body, headers));
		_totalQueued++;

		return this;
	}

	/// <inheritdoc/>
	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		// ArgumentNullException.ThrowIfNull rather than Ensure.NotNull: the library takes Polyfill
		// with PrivateAssets="all", so Ensure is not visible to the test project.
		ArgumentNullException.ThrowIfNull(request);

		// The request must be read and recorded before returning: once SendAsync returns, the
		// caller may dispose the request, and reading request.Content lazily afterwards can fail.
		string? body = request.Content is null
			? null
			: await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

		Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);

		foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
		{
			headers[header.Key] = string.Join(", ", header.Value);
		}

		if (request.Content is not null)
		{
			foreach (KeyValuePair<string, IEnumerable<string>> header in request.Content.Headers)
			{
				headers[header.Key] = string.Join(", ", header.Value);
			}
		}

		_requests.Add(new RecordedRequest(request.Method, request.RequestUri!, body, headers));

		// Running out of queued responses means the code under test issued a request the test did
		// not anticipate. Failing here names that request; returning a default would hide it.
		if (_responses.Count == 0)
		{
			throw new InvalidOperationException(
				$"No queued response for {request.Method} {request.RequestUri}: " +
				$"{_totalQueued} queued, {_requests.Count} arrived.");
		}

		QueuedResponse queued = _responses.Dequeue();

		HttpResponseMessage response = new(queued.Status)
		{
			Content = new StringContent(queued.Body, Encoding.UTF8),
		};

		foreach ((string name, string value) in queued.Headers)
		{
			// HttpResponseMessage.Headers throws InvalidOperationException for a content header such
			// as Content-Type or Content-Length — that validation is exactly how a content header is
			// told apart from a response header, so catching it is the routing decision rather than a
			// failure to handle. TryAddWithoutValidation would accept anything unconditionally and
			// silently defeat that routing, so the initial attempt must go through the validating Add.
			try
			{
				response.Headers.Add(name, value);
			}
			catch (InvalidOperationException)
			{
				// StringContent already set a default Content-Type; replace it rather than appending
				// a second value, which would make HttpContentHeaders.ContentType unparsable.
				response.Content.Headers.Remove(name);

				if (!response.Content.Headers.TryAddWithoutValidation(name, value))
				{
					throw new InvalidOperationException($"Header '{name}' fits neither response nor content headers.");
				}
			}
		}

		return response;
	}

	private readonly record struct QueuedResponse(HttpStatusCode Status, string Body, (string Name, string Value)[] Headers);

	/// <summary>A single request this handler received, captured in full.</summary>
	/// <param name="Method">The HTTP method used.</param>
	/// <param name="Uri">The request URI.</param>
	/// <param name="Body">The request body as text, or <see langword="null"/> when the request had no content.</param>
	/// <param name="Headers">Every header on the request, request headers and content headers combined.</param>
	public sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Body, IReadOnlyDictionary<string, string> Headers);
}
