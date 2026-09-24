// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Strings;

[TestClass]
public sealed class GitHubDeviceFlowTests
{
	public TestContext TestContext { get; set; } = null!;

	private const string DeviceCodeBody =
		"{\"device_code\":\"dev-abc\",\"user_code\":\"WXYZ-1234\"," +
		"\"verification_uri\":\"https://github.com/login/device\"," +
		"\"expires_in\":900,\"interval\":5}";

	private const string DeviceCodeBodyWithZeroInterval =
		"{\"device_code\":\"dev-abc\",\"user_code\":\"WXYZ-1234\"," +
		"\"verification_uri\":\"https://github.com/login/device\"," +
		"\"expires_in\":900,\"interval\":0}";

	private const string SuccessTokenBody =
		"{\"access_token\":\"gho_realtoken\",\"token_type\":\"bearer\",\"scope\":\"repo,read:org\"}";

	private const string AuthorizationPendingBody = "{\"error\":\"authorization_pending\"}";

	private const string SlowDownBody = "{\"error\":\"slow_down\"}";

	/// <summary>
	/// Creates a flow against <paramref name="handler"/>, optionally replacing the wait and clock a
	/// test needs to observe or fast-forward without a real wait. Omitting both keeps this identical
	/// to the flow's own defaults (<see cref="Task.Delay(TimeSpan, CancellationToken)"/> and
	/// <see cref="Environment.TickCount64"/>), which is what the tests already written against this
	/// helper rely on.
	/// </summary>
	private static GitHubDeviceFlow CreateFlow(
		FakeHttpMessageHandler handler,
		Func<TimeSpan, CancellationToken, Task>? delay = null,
		Func<long>? nowMilliseconds = null) =>
		new("Iv1.0123456789abcdef".As<GitHubOAuthClientId>(), ["repo", "read:org"])
		{
			Handler = handler,
			Delay = delay ?? Task.Delay,
			NowMilliseconds = nowMilliseconds ?? (() => Environment.TickCount64),
		};

	[TestMethod]
	public async Task RequestsADeviceCodeAndReportsWhatTheUserNeeds()
	{
		using FakeHttpMessageHandler handler = new();
		_ = handler.Respond(HttpStatusCode.OK, DeviceCodeBody, ("Content-Type", "application/json"));

		GitHubDeviceCode code = await CreateFlow(handler)
			.RequestDeviceCodeAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual("WXYZ-1234", code.UserCode);
		Assert.AreEqual("dev-abc", code.DeviceCode);
		Assert.AreEqual(new Uri("https://github.com/login/device"), code.VerificationUri);
		Assert.AreEqual(TimeSpan.FromSeconds(900), code.ExpiresIn);
		Assert.AreEqual(TimeSpan.FromSeconds(5), code.Interval);
	}

	[TestMethod]
	public async Task SendsTheRequestedScopes()
	{
		using FakeHttpMessageHandler handler = new();
		_ = handler.Respond(HttpStatusCode.OK, DeviceCodeBody, ("Content-Type", "application/json"));

		_ = await CreateFlow(handler)
			.RequestDeviceCodeAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		// Space-separated, not comma-joined: GitHub's device-flow request wants scopes
		// space-separated, and this exact substring is what rules out a comma (or any other
		// separator) sneaking back in.
		StringAssert.Contains(handler.Requests[0].Body, "\"scope\":\"repo read:org\"");
	}

	[TestMethod]
	public async Task ReturnsAHostNativeTokenOnSuccess()
	{
		// FromToken, not FromBearerToken: a GitHub OAuth token travels under Octokit's Token scheme.
		using FakeHttpMessageHandler handler = new();
		_ = handler.Respond(HttpStatusCode.OK, DeviceCodeBody, ("Content-Type", "application/json"));
		_ = handler.Respond(
			HttpStatusCode.OK,
			"{\"access_token\":\"gho_realtoken\",\"token_type\":\"bearer\",\"scope\":\"repo,read:org\"}",
			("Content-Type", "application/json"));

		GitHubDeviceFlow flow = CreateFlow(handler);
		GitHubDeviceCode code = await flow.RequestDeviceCodeAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);
		HostingCredential credential = await flow.WaitForTokenAsync(code, TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(HostingCredentialKind.Token, credential.Kind);
		Assert.AreEqual("gho_realtoken", credential.Token);
	}

	[TestMethod]
	public async Task ReportsARefusedAuthorisationAsAnAuthenticationFailure()
	{
		using FakeHttpMessageHandler handler = new();
		_ = handler.Respond(HttpStatusCode.OK, DeviceCodeBody, ("Content-Type", "application/json"));
		_ = handler.Respond(
			HttpStatusCode.OK,
			"{\"error\":\"access_denied\",\"error_description\":\"The user denied the request.\"}",
			("Content-Type", "application/json"));

		GitHubDeviceFlow flow = CreateFlow(handler);
		GitHubDeviceCode code = await flow.RequestDeviceCodeAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		GitHostingAuthenticationException exception =
			await Assert.ThrowsExactlyAsync<GitHostingAuthenticationException>(
				async () => await flow.WaitForTokenAsync(code, TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
				.ConfigureAwait(false);

		StringAssert.Contains(exception.Message, "access_denied");
	}

	[TestMethod]
	public async Task ReportsAnExpiredCodeAsAnAuthenticationFailure()
	{
		using FakeHttpMessageHandler handler = new();
		_ = handler.Respond(HttpStatusCode.OK, DeviceCodeBody, ("Content-Type", "application/json"));
		_ = handler.Respond(
			HttpStatusCode.OK,
			"{\"error\":\"expired_token\",\"error_description\":\"The device code has expired.\"}",
			("Content-Type", "application/json"));

		GitHubDeviceFlow flow = CreateFlow(handler);
		GitHubDeviceCode code = await flow.RequestDeviceCodeAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		_ = await Assert.ThrowsExactlyAsync<GitHostingAuthenticationException>(
			async () => await flow.WaitForTokenAsync(code, TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	[TestMethod]
	[DataRow("incorrect_client_credentials")]
	[DataRow("unsupported_grant_type")]
	[DataRow("device_flow_disabled")]
	public async Task ReportsAConfigurationFaultAsARequestFailureAsync(string errorCode)
	{
		using FakeHttpMessageHandler handler = new();
		_ = handler.Respond(HttpStatusCode.OK, DeviceCodeBody, ("Content-Type", "application/json"));
		_ = handler.Respond(
			HttpStatusCode.OK,
			$"{{\"error\":\"{errorCode}\",\"error_description\":\"unusable\"}}",
			("Content-Type", "application/json"));

		GitHubDeviceFlow flow = CreateFlow(handler);
		GitHubDeviceCode code = await flow.RequestDeviceCodeAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		// Not GitHostingAuthenticationException: none of these three codes can be fixed by the user
		// trying the sign-in again, so reporting them as an authentication failure would loop them
		// through a flow that can never succeed.
		GitHostingRequestException exception =
			await Assert.ThrowsExactlyAsync<GitHostingRequestException>(
				async () => await flow.WaitForTokenAsync(code, TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
				.ConfigureAwait(false);

		StringAssert.Contains(exception.Message, errorCode);
	}

	[TestMethod]
	public async Task ReportsAnUnrecognizedErrorCodeAsARequestFailureAsync()
	{
		using FakeHttpMessageHandler handler = new();
		_ = handler.Respond(HttpStatusCode.OK, DeviceCodeBody, ("Content-Type", "application/json"));
		_ = handler.Respond(
			HttpStatusCode.OK,
			"{\"error\":\"some_future_github_error\",\"error_description\":\"not yet catalogued\"}",
			("Content-Type", "application/json"));

		GitHubDeviceFlow flow = CreateFlow(handler);
		GitHubDeviceCode code = await flow.RequestDeviceCodeAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		// The deliberate default for a code this library was not taught: a request fault, not an
		// authentication failure, so an unrecognized error never invites retrying a sign-in.
		GitHostingRequestException exception =
			await Assert.ThrowsExactlyAsync<GitHostingRequestException>(
				async () => await flow.WaitForTokenAsync(code, TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
				.ConfigureAwait(false);

		StringAssert.Contains(exception.Message, "some_future_github_error");
	}

	[TestMethod]
	public async Task DoesNotEchoTheTokenEndpointResponseBodyOnFailureAsync()
	{
		using FakeHttpMessageHandler handler = new();
		_ = handler.Respond(HttpStatusCode.OK, DeviceCodeBody, ("Content-Type", "application/json"));
		_ = handler.Respond(
			HttpStatusCode.InternalServerError,
			"{\"secret\":\"do-not-leak-me\"}",
			("Content-Type", "application/json"));

		GitHubDeviceFlow flow = CreateFlow(handler);
		GitHubDeviceCode code = await flow.RequestDeviceCodeAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		// A deliberate omission, not an oversight: this asserts a future edit cannot add the body
		// back "for symmetry" with RequestDeviceCodeAsync's own failure message without CI catching it.
		GitHostingRequestException exception =
			await Assert.ThrowsExactlyAsync<GitHostingRequestException>(
				async () => await flow.WaitForTokenAsync(code, TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
				.ConfigureAwait(false);

		Assert.IsFalse(exception.Message.Contains("do-not-leak-me", StringComparison.Ordinal));
	}

	[TestMethod]
	public async Task ReportsAnEmptyVerificationUriAsARequestFailureAsync()
	{
		using FakeHttpMessageHandler handler = new();
		_ = handler.Respond(
			HttpStatusCode.OK,
			"{\"device_code\":\"dev-abc\",\"user_code\":\"WXYZ-1234\"," +
			"\"verification_uri\":\"\",\"expires_in\":900,\"interval\":5}",
			("Content-Type", "application/json"));

		_ = await Assert.ThrowsExactlyAsync<GitHostingRequestException>(
			async () => await CreateFlow(handler)
				.RequestDeviceCodeAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task ReportsANonAbsoluteVerificationUriAsARequestFailureAsync()
	{
		using FakeHttpMessageHandler handler = new();
		_ = handler.Respond(
			HttpStatusCode.OK,
			// Not a leading-slash path: Uri.TryCreate(..., UriKind.Absolute, ...) accepts one of
			// those as an absolute file:// URI, which would defeat this test. A scheme-less
			// authority-and-path string is what genuinely fails UriKind.Absolute parsing.
			"{\"device_code\":\"dev-abc\",\"user_code\":\"WXYZ-1234\"," +
			"\"verification_uri\":\"github.com/login/device\",\"expires_in\":900,\"interval\":5}",
			("Content-Type", "application/json"));

		_ = await Assert.ThrowsExactlyAsync<GitHostingRequestException>(
			async () => await CreateFlow(handler)
				.RequestDeviceCodeAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task ReportsAnUnusableClientIdentifierAsARequestFailure()
	{
		using FakeHttpMessageHandler handler = new();
		_ = handler.Respond(
			HttpStatusCode.NotFound,
			"{\"error\":\"Not Found\"}",
			("Content-Type", "application/json"));

		_ = await Assert.ThrowsExactlyAsync<GitHostingRequestException>(
			async () => await CreateFlow(handler)
				.RequestDeviceCodeAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task PollRequestCarriesTheDeviceCodeAndNotTheUserCodeAsync()
	{
		using FakeHttpMessageHandler handler = new();
		_ = handler.Respond(HttpStatusCode.OK, DeviceCodeBody, ("Content-Type", "application/json"));
		_ = handler.Respond(HttpStatusCode.OK, SuccessTokenBody, ("Content-Type", "application/json"));

		GitHubDeviceFlow flow = CreateFlow(handler);
		GitHubDeviceCode code = await flow.RequestDeviceCodeAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);
		_ = await flow.WaitForTokenAsync(code, TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(new Uri("https://github.com/login/device/code"), handler.Requests[0].Uri);
		Assert.AreEqual(new Uri("https://github.com/login/oauth/access_token"), handler.Requests[1].Uri);
		StringAssert.Contains(handler.Requests[1].Body, "\"device_code\":\"dev-abc\"");
		StringAssert.Contains(handler.Requests[1].Body, "\"grant_type\":\"urn:ietf:params:oauth:grant-type:device_code\"");

		// The exact confusion GitHubDeviceCode's own XML doc warns about: the poll request must
		// never carry the code a human types, only the opaque one it was issued alongside.
		Assert.IsFalse(handler.Requests[1].Body!.Contains("user_code", StringComparison.Ordinal));
	}

	[TestMethod]
	public async Task ReportsAResponseWithNeitherTokenNorErrorAsARequestFailureAsync()
	{
		using FakeHttpMessageHandler handler = new();
		_ = handler.Respond(HttpStatusCode.OK, DeviceCodeBody, ("Content-Type", "application/json"));
		_ = handler.Respond(HttpStatusCode.OK, "{\"token_type\":\"bearer\"}", ("Content-Type", "application/json"));

		GitHubDeviceFlow flow = CreateFlow(handler);
		GitHubDeviceCode code = await flow.RequestDeviceCodeAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		_ = await Assert.ThrowsExactlyAsync<GitHostingRequestException>(
			async () => await flow.WaitForTokenAsync(code, TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task PollsThroughAuthorizationPendingToSuccessAsync()
	{
		using FakeHttpMessageHandler handler = new();
		_ = handler.Respond(HttpStatusCode.OK, DeviceCodeBody, ("Content-Type", "application/json"));
		_ = handler.Respond(HttpStatusCode.OK, AuthorizationPendingBody, ("Content-Type", "application/json"));
		_ = handler.Respond(HttpStatusCode.OK, SuccessTokenBody, ("Content-Type", "application/json"));

		// A no-op wait: this test is about the pending-then-success transition, not about timing, so
		// the interval is never actually slept.
		GitHubDeviceFlow flow = CreateFlow(handler, delay: (_, _) => Task.CompletedTask);

		GitHubDeviceCode code = await flow.RequestDeviceCodeAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);
		HostingCredential credential = await flow.WaitForTokenAsync(code, TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual("gho_realtoken", credential.Token);
		Assert.AreEqual(3, handler.Requests.Count);
	}

	[TestMethod]
	public async Task WidensTheIntervalCumulativelyOnRepeatedSlowDownAsync()
	{
		using FakeHttpMessageHandler handler = new();
		_ = handler.Respond(HttpStatusCode.OK, DeviceCodeBody, ("Content-Type", "application/json"));
		_ = handler.Respond(HttpStatusCode.OK, SlowDownBody, ("Content-Type", "application/json"));
		_ = handler.Respond(HttpStatusCode.OK, SlowDownBody, ("Content-Type", "application/json"));
		_ = handler.Respond(HttpStatusCode.OK, SuccessTokenBody, ("Content-Type", "application/json"));

		List<TimeSpan> delays = [];

		GitHubDeviceFlow flow = CreateFlow(handler, delay: (interval, _) =>
		{
			delays.Add(interval);
			return Task.CompletedTask;
		});

		GitHubDeviceCode code = await flow.RequestDeviceCodeAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);
		_ = await flow.WaitForTokenAsync(code, TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		// DeviceCodeBody's interval is 5 seconds. Two slow_down answers must widen it twice, to 10
		// then 15, not reset it back to 10 each time.
		Assert.AreEqual(2, delays.Count);
		Assert.AreEqual(TimeSpan.FromSeconds(10), delays[0]);
		Assert.AreEqual(TimeSpan.FromSeconds(15), delays[1]);
	}

	[TestMethod]
	public async Task FloorsAZeroIntervalFromTheWireAsync()
	{
		using FakeHttpMessageHandler handler = new();
		_ = handler.Respond(HttpStatusCode.OK, DeviceCodeBodyWithZeroInterval, ("Content-Type", "application/json"));
		_ = handler.Respond(HttpStatusCode.OK, AuthorizationPendingBody, ("Content-Type", "application/json"));
		_ = handler.Respond(HttpStatusCode.OK, SuccessTokenBody, ("Content-Type", "application/json"));

		List<TimeSpan> delays = [];

		GitHubDeviceFlow flow = CreateFlow(handler, delay: (interval, _) =>
		{
			delays.Add(interval);
			return Task.CompletedTask;
		});

		GitHubDeviceCode code = await flow.RequestDeviceCodeAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);
		Assert.AreEqual(TimeSpan.Zero, code.Interval);

		_ = await flow.WaitForTokenAsync(code, TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(1, delays.Count);
		Assert.AreEqual(TimeSpan.FromSeconds(5), delays[0]);
	}

	[TestMethod]
	public async Task ThrowsOnceTheDeviceCodeExpiresAsync()
	{
		using FakeHttpMessageHandler handler = new();
		_ = handler.Respond(HttpStatusCode.OK, DeviceCodeBody, ("Content-Type", "application/json"));
		_ = handler.Respond(HttpStatusCode.OK, AuthorizationPendingBody, ("Content-Type", "application/json"));

		// The fake clock jumps past DeviceCodeBody's 900-second expiry the moment the first wait is
		// asked for, simulating a server that answers authorization_pending forever without this
		// test actually waiting 900 seconds for it.
		long now = 0;
		GitHubDeviceFlow flow = CreateFlow(
			handler,
			delay: (_, _) =>
			{
				now += (long)TimeSpan.FromSeconds(900).TotalMilliseconds + 1_000;
				return Task.CompletedTask;
			},
			nowMilliseconds: () => now);

		GitHubDeviceCode code = await flow.RequestDeviceCodeAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		GitHostingAuthenticationException exception =
			await Assert.ThrowsExactlyAsync<GitHostingAuthenticationException>(
				async () => await flow.WaitForTokenAsync(code, TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
				.ConfigureAwait(false);

		StringAssert.Contains(exception.Message, "expired");

		// Exactly the one poll that answered authorization_pending: the deadline is caught before a
		// second poll is ever sent, not by a poll response saying so.
		Assert.AreEqual(2, handler.Requests.Count);
	}

	[TestMethod]
	public async Task CancelsDuringTheWaitBetweenPollsAsync()
	{
		using FakeHttpMessageHandler handler = new();
		_ = handler.Respond(HttpStatusCode.OK, DeviceCodeBody, ("Content-Type", "application/json"));
		_ = handler.Respond(HttpStatusCode.OK, AuthorizationPendingBody, ("Content-Type", "application/json"));

		using CancellationTokenSource cts = new();
		TaskCompletionSource waitStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

		// Never completes on its own: it only resolves when the token passed to it is canceled,
		// which is exactly the wait this test needs to cancel into rather than before.
		GitHubDeviceFlow flow = CreateFlow(handler, delay: (_, cancellationToken) =>
		{
			waitStarted.TrySetResult();
			return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
		});

		GitHubDeviceCode code = await flow.RequestDeviceCodeAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Task<HostingCredential> waitTask = flow.WaitForTokenAsync(code, cts.Token);

		await waitStarted.Task.ConfigureAwait(false);
		await cts.CancelAsync().ConfigureAwait(false);

		_ = await Assert.ThrowsExactlyAsync<TaskCanceledException>(
			async () => await waitTask.ConfigureAwait(false)).ConfigureAwait(false);
	}

	[TestMethod]
	public void RejectsANullClientIdentifier() =>
		_ = Assert.ThrowsExactly<ArgumentNullException>(() => new GitHubDeviceFlow(null!, ["repo"]));

	[TestMethod]
	public void RejectsNullScopes() =>
		_ = Assert.ThrowsExactly<ArgumentNullException>(
			() => new GitHubDeviceFlow("Iv1.0123456789abcdef".As<GitHubOAuthClientId>(), null!));
}
