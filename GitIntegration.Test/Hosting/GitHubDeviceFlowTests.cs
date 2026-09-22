// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Net;
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

	private static GitHubDeviceFlow CreateFlow(FakeHttpMessageHandler handler) =>
		new("Iv1.0123456789abcdef".As<GitHubOAuthClientId>(), ["repo", "read:org"]) { Handler = handler };

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

		StringAssert.Contains(handler.Requests[0].Body, "repo");
		StringAssert.Contains(handler.Requests[0].Body, "read:org");
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
	public void RejectsANullClientIdentifier() =>
		_ = Assert.ThrowsExactly<ArgumentNullException>(() => new GitHubDeviceFlow(null!, ["repo"]));

	[TestMethod]
	public void RejectsNullScopes() =>
		_ = Assert.ThrowsExactly<ArgumentNullException>(
			() => new GitHubDeviceFlow("Iv1.0123456789abcdef".As<GitHubOAuthClientId>(), null!));
}
