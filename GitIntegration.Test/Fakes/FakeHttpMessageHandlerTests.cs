// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

[TestClass]
public class FakeHttpMessageHandlerTests
{
	[TestMethod]
	public async Task RecordsTheMethodUriHeadersAndBodyOfEveryRequestAsync()
	{
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, "{\"first\":true}")
			.Respond(HttpStatusCode.Created, "{\"second\":true}");
		using HttpClient client = new(handler);

		using HttpRequestMessage first = new(HttpMethod.Get, "https://example.invalid/one");
		first.Headers.Add("Authorization", "Basic dXNlcjpwYXNz");
		_ = await client.SendAsync(first, TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		using StringContent payload = new("{\"title\":\"t\"}", Encoding.UTF8, "application/json");
		_ = await client.PostAsync(new Uri("https://example.invalid/two"), payload, TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(2, handler.Requests.Count);

		Assert.AreEqual(HttpMethod.Get, handler.Requests[0].Method);
		Assert.AreEqual(new Uri("https://example.invalid/one"), handler.Requests[0].Uri);
		Assert.AreEqual("Basic dXNlcjpwYXNz", handler.Requests[0].Headers["Authorization"]);
		Assert.IsNull(handler.Requests[0].Body);

		Assert.AreEqual(HttpMethod.Post, handler.Requests[1].Method);
		Assert.AreEqual(new Uri("https://example.invalid/two"), handler.Requests[1].Uri);
		Assert.AreEqual("{\"title\":\"t\"}", handler.Requests[1].Body);
		StringAssert.Contains(handler.Requests[1].Headers["Content-Type"], "application/json");
	}

	[TestMethod]
	public async Task ReturnsQueuedResponsesInOrderAsync()
	{
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, "first")
			.Respond(HttpStatusCode.NotFound, "second");
		using HttpClient client = new(handler);

		using HttpResponseMessage one = await client.GetAsync(new Uri("https://example.invalid/a"), TestContext.CancellationTokenSource.Token).ConfigureAwait(false);
		using HttpResponseMessage two = await client.GetAsync(new Uri("https://example.invalid/b"), TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(HttpStatusCode.OK, one.StatusCode);
		Assert.AreEqual("first", await one.Content.ReadAsStringAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false));
		Assert.AreEqual(HttpStatusCode.NotFound, two.StatusCode);
		Assert.AreEqual("second", await two.Content.ReadAsStringAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task ThrowsWhenARequestArrivesWithNoQueuedResponseAsync()
	{
		using FakeHttpMessageHandler handler = new();
		using HttpClient client = new(handler);

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(
			async () => await client.GetAsync(new Uri("https://example.invalid/a"), TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task NamesHowManyResponsesWereQueuedAndHowManyRequestsArrivedAsync()
	{
		// The failure message is the only diagnostic a test author sees when a builder issues an
		// unanticipated request, so it must name both counts rather than just "something went wrong".
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, "first");
		using HttpClient client = new(handler);

		_ = await client.GetAsync(new Uri("https://example.invalid/a"), TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		InvalidOperationException exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
			async () => await client.GetAsync(new Uri("https://example.invalid/b"), TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		StringAssert.Contains(exception.Message, "1 queued");
		StringAssert.Contains(exception.Message, "2 arrived");
	}

	[TestMethod]
	public async Task AttachesResponseHeadersWhenGivenAsync()
	{
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.TooManyRequests, "{}", ("Retry-After", "60"));
		using HttpClient client = new(handler);

		using HttpResponseMessage response = await client.GetAsync(new Uri("https://example.invalid/a"), TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual("60", response.Headers.GetValues("Retry-After").Single());
	}

	[TestMethod]
	public async Task RoutesAContentHeaderToTheContentHeaderCollectionAsync()
	{
		// HttpResponseMessage.Headers throws at runtime if a content header such as Content-Type is
		// added to it directly; this fake must route it to response.Content.Headers instead of
		// letting that exception escape.
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, "{}", ("Content-Type", "application/vnd.github+json"));
		using HttpClient client = new(handler);

		using HttpResponseMessage response = await client.GetAsync(new Uri("https://example.invalid/a"), TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		// response.Headers.Contains("Content-Type") is not a usable counter-check here: HttpHeaders
		// itself throws InvalidOperationException for a mismatched header name, the same guard this
		// fake relies on to route the header in the first place. A single, correctly parsed
		// ContentType on the content headers is what proves the routing worked.
		Assert.AreEqual("application/vnd.github+json", response.Content.Headers.ContentType?.MediaType);
	}

	public TestContext TestContext { get; set; } = null!;
}
