// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// What GitHub issues when a device flow begins: the code a human types, and the code the polling
/// request carries.
/// </summary>
/// <remarks>
/// <see cref="UserCode"/> and <see cref="DeviceCode"/> are different values for different audiences.
/// The user code is short and is displayed, the device code is opaque and is what
/// <see cref="GitHubDeviceFlow.WaitForTokenAsync"/> sends. Showing the device code to a user, or
/// sending the user code in its place, both fail in ways that look like a broken sign-in.
/// </remarks>
public sealed record GitHubDeviceCode
{
	/// <summary>Gets the code the user enters at <see cref="VerificationUri"/>.</summary>
	public required string UserCode { get; init; }

	/// <summary>Gets the code the token request carries. Not shown to the user.</summary>
	public required string DeviceCode { get; init; }

	/// <summary>Gets the page the user opens to enter <see cref="UserCode"/>.</summary>
	public required Uri VerificationUri { get; init; }

	/// <summary>Gets how long this code remains usable.</summary>
	/// <remarks>Surfaced because a caller showing a countdown needs it and cannot derive it.</remarks>
	public required TimeSpan ExpiresIn { get; init; }

	/// <summary>Gets the minimum wait GitHub requires between token requests.</summary>
	public required TimeSpan Interval { get; init; }
}

/// <summary>
/// Obtains a GitHub credential through the OAuth device flow.
/// </summary>
/// <remarks>
/// <para>
/// Two calls rather than one. Device flow has an inherent pause in the middle: GitHub issues a
/// short code, the user types it into a browser, and only then does polling succeed, and that pause
/// is minutes long with the code on screen throughout. A single method taking a "here's the code"
/// callback would invoke it from whatever thread an HTTP continuation resumed on, leaving every
/// graphical caller to marshal the code back to a user interface thread from inside a callback it
/// does not control. Splitting the call puts the seam where the pause already is.
/// </para>
/// <para>
/// Separate from <see cref="GitHubProvider"/>, whose every other method is one request and one
/// response. Nothing here resolves or applies a credential, this type produces one and
/// <see cref="GitProvider"/> consumes one, through the credential cache or
/// <see cref="GitProvider.CredentialSource"/>.
/// </para>
/// <para>
/// Stores nothing. <see cref="WaitForTokenAsync"/> returns the credential and the caller decides
/// where it lives:
/// </para>
/// <code>
/// GitHubDeviceCode code = await flow.RequestDeviceCodeAsync(cancellationToken);
/// // show code.UserCode and open code.VerificationUri
/// HostingCredential credential = await flow.WaitForTokenAsync(code, cancellationToken);
/// CredentialCache.Instance.AddOrReplace(persona, new CredentialWithToken { Token = credential.Token!.As&lt;CredentialToken&gt;() });
/// </code>
/// <para>
/// Built on a raw <see cref="HttpClient"/> rather than on <c>Octokit.IOauthClient</c>, even though
/// Octokit exposes exactly this pair of calls. Two things about Octokit 14.0.0's own device-flow
/// implementation make it unusable here without changing what this type promises:
/// </para>
/// <list type="number">
/// <item>
/// <c>OauthClient.InitiateDeviceFlow</c> sends <c>scope</c> as a comma-joined,
/// form-url-encoded value, so a scope such as <c>read:org</c> travels as <c>read%3Aorg</c>. GitHub's
/// documented device-flow request accepts a JSON body, so this type sends one instead and the colon
/// survives unencoded.
/// </item>
/// <item>
/// <c>OauthClient.CreateAccessTokenForDeviceFlow</c> throws <c>Octokit.ApiException</c> for a body
/// carrying an OAuth <c>error</c> field (<c>access_denied</c>, <c>expired_token</c>, and so on)
/// rather than returning an <c>OauthToken</c> with <c>Error</c> populated, which collapses a refused
/// authorisation and an expired code into the same exception shape a transport failure gets. This
/// type reads the <c>error</c> field itself, so it can tell that apart as
/// <see cref="GitHostingAuthenticationException"/> rather than <see cref="GitHostingRequestException"/>.
/// </item>
/// </list>
/// <para>
/// Both were found empirically, against the actual package, while implementing this type. Reading
/// the response body by hand also means this type owns the poll loop: it waits
/// <see cref="GitHubDeviceCode.Interval"/> between attempts, widening it by five seconds whenever
/// GitHub answers <c>slow_down</c>, exactly as GitHub's documentation for the device flow describes.
/// </para>
/// <para>
/// Both endpoints are sent a JSON request body, with <c>Accept: application/json</c>, even though
/// GitHub's own published documentation shows the device flow using form-url-encoded requests.
/// GitHub's actual service accepts the JSON form in practice, which is what let
/// <see cref="RequestDeviceCodeAsync"/> avoid the scope-encoding problem above, but this is
/// undocumented behavior this library now depends on, and nothing in this library's test suite
/// verifies it against the real service, only against the fake transport.
/// </para>
/// </remarks>
/// <param name="clientId">The OAuth App's client identifier.</param>
/// <param name="scopes">The scopes to request, such as <c>repo</c> and <c>read:org</c>.</param>
public sealed class GitHubDeviceFlow(GitHubOAuthClientId clientId, IReadOnlyList<string> scopes)
{
	/// <summary>GitHub's endpoint for beginning a device flow.</summary>
	private static readonly Uri DeviceCodeEndpoint = new("https://github.com/login/device/code");

	/// <summary>GitHub's endpoint for exchanging a device code for a token.</summary>
	private static readonly Uri AccessTokenEndpoint = new("https://github.com/login/oauth/access_token");

	/// <summary>The grant type GitHub's device-flow token exchange expects.</summary>
	private const string DeviceCodeGrantType = "urn:ietf:params:oauth:grant-type:device_code";

	/// <summary>How much longer to wait after GitHub answers <c>slow_down</c>.</summary>
	private static readonly TimeSpan SlowDownIncrement = TimeSpan.FromSeconds(5);

	/// <summary>
	/// The shortest wait this flow will ever use between polls, regardless of what
	/// <see cref="GitHubDeviceCode.Interval"/> says.
	/// </summary>
	/// <remarks>
	/// GitHub's own device-flow documentation states five seconds as the minimum polling interval.
	/// <see cref="GitHubDeviceCode"/> is public with an unvalidated <see langword="required init"/>
	/// <see cref="GitHubDeviceCode.Interval"/>, so a value of zero, or one hand-built by a caller,
	/// must not be able to produce an unthrottled loop against github.com. Applied at the point each
	/// wait is issued, not when a wire response is parsed, so a hand-built
	/// <see cref="GitHubDeviceCode"/> is covered exactly as a parsed one is.
	/// </remarks>
	private static readonly TimeSpan MinimumPollInterval = TimeSpan.FromSeconds(5);

	/// <summary>
	/// The transport every flow shares when no <see cref="Handler"/> was injected.
	/// </summary>
	/// <remarks>
	/// Its own instance rather than <see cref="GitHubProvider"/>'s, matching that type's reasoning:
	/// one handler for the process rather than one per call, and
	/// <see cref="GitProvider.CreateDefaultHandler"/> is what keeps the settings from drifting apart.
	/// </remarks>
	private static readonly SocketsHttpHandler SharedHandler = GitProvider.CreateDefaultHandler();

	private readonly GitHubOAuthClientId _clientId = Ensure.NotNull(clientId);
	private readonly IReadOnlyList<string> _scopes = Ensure.NotNull(scopes);

	/// <summary>
	/// Gets or initializes the transport this flow issues requests through, or <see langword="null"/>
	/// to use the shared one.
	/// </summary>
	/// <remarks>
	/// Internal rather than public, so no transport type appears in this library's public API, and
	/// the test project injects a fake through <c>InternalsVisibleTo</c>, the same seam
	/// <see cref="GitProvider.Handler"/> provides.
	/// </remarks>
	internal HttpMessageHandler? Handler { get; init; }

	/// <summary>
	/// Gets or initializes the wait <see cref="WaitForTokenAsync"/> performs between polls.
	/// </summary>
	/// <remarks>
	/// Defaults to <see cref="Task.Delay(TimeSpan, CancellationToken)"/>. Internal for the same
	/// reason as <see cref="Handler"/>: this exists so a test can replace minutes of real waiting
	/// with an instantaneous one while still exercising the interval, the widening, and the deadline
	/// arithmetic that surround it, not so a caller can tune polling behavior.
	/// </remarks>
	internal Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

	/// <summary>
	/// Gets or initializes the monotonic clock, in milliseconds, <see cref="WaitForTokenAsync"/> reads
	/// to enforce <see cref="GitHubDeviceCode.ExpiresIn"/>.
	/// </summary>
	/// <remarks>
	/// Defaults to <see cref="Environment.TickCount64"/>, a monotonic source deliberately chosen over
	/// <see cref="DateTime.Now"/> or <see cref="DateTimeOffset.UtcNow"/>: neither is guaranteed
	/// monotonic, and a clock adjustment during a wait that can last minutes must not extend or
	/// collapse the window this flow enforces. Named for its unit rather than for the .NET member it
	/// defaults to: a future replacement built on, say, <see cref="DateTime.UtcNow"/>'s <c>Ticks</c>
	/// (100-nanosecond units) would silently widen the deadline computed from it roughly ten-thousand
	/// fold, and a name that already states "milliseconds" is what stops that substitution compiling
	/// clean while quietly breaking the deadline. Internal for the same reason <see cref="Delay"/> is.
	/// </remarks>
	internal Func<long> NowMilliseconds { get; init; } = () => Environment.TickCount64;

	/// <summary>
	/// Asks GitHub to begin a device flow.
	/// </summary>
	/// <param name="cancellationToken">Cancels the request.</param>
	/// <returns>The codes and timings the flow's second half needs.</returns>
	/// <exception cref="GitHostingRequestException">GitHub refused or could not be reached.</exception>
	public async Task<GitHubDeviceCode> RequestDeviceCodeAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		GitHubDeviceCodeRequestBody requestBody = new()
		{
			ClientId = _clientId.WeakString,
			Scope = string.Join(' ', _scopes),
		};

		using HttpClient client = CreateHttpClient();
		using HttpRequestMessage request = CreateJsonRequest(
			DeviceCodeEndpoint, JsonSerializer.Serialize(requestBody, GitHubDeviceFlowJsonContext.Default.GitHubDeviceCodeRequestBody));

		using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
		string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

		if (!response.IsSuccessStatusCode)
		{
			throw new GitHostingRequestException(
				$"GitHub refused to begin a device flow: {(int)response.StatusCode} {response.ReasonPhrase}. {body}".TrimEnd());
		}

		GitHubDeviceCodeResponseBody parsed = DeserializeOrThrow(
			body, GitHubDeviceFlowJsonContext.Default.GitHubDeviceCodeResponseBody, "device code");

		// TryCreate, not `new Uri(...)`: `verification_uri` being present (required by the response
		// shape) says nothing about its content. An empty string, a relative path, or a truncated
		// body all reach here as a syntactically valid JSON string, and `new Uri` would throw
		// UriFormatException straight past this method's documented failure surface, which is
		// GitHostingRequestException alone, exactly the leak class DeserializeOrThrow already guards
		// against for JsonException.
		if (!Uri.TryCreate(parsed.VerificationUri, UriKind.Absolute, out Uri? verificationUri))
		{
			throw new GitHostingRequestException(
				$"GitHub reported success but returned a verification URI that is not an absolute URI: \"{parsed.VerificationUri}\".");
		}

		return new GitHubDeviceCode
		{
			UserCode = parsed.UserCode,
			DeviceCode = parsed.DeviceCode,
			VerificationUri = verificationUri,
			// GitHub reports both as integer seconds, the conversion happens once, here.
			ExpiresIn = TimeSpan.FromSeconds(parsed.ExpiresIn),
			Interval = TimeSpan.FromSeconds(parsed.Interval),
		};
	}

	/// <summary>
	/// Waits for the user to authorise the flow, then returns the credential GitHub issues.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Polls until the user authorises, the code expires, or <paramref name="cancellationToken"/> is
	/// canceled, so this may block for as long as <see cref="GitHubDeviceCode.ExpiresIn"/>. A
	/// <c>authorization_pending</c> answer waits <see cref="GitHubDeviceCode.Interval"/> and tries
	/// again; a <c>slow_down</c> answer widens that wait by <see cref="SlowDownIncrement"/> first.
	/// The deadline is enforced by this flow, not left to GitHub: a server that keeps answering
	/// <c>authorization_pending</c> past <see cref="GitHubDeviceCode.ExpiresIn"/> would otherwise poll
	/// forever, since nothing about that answer's shape distinguishes a slow user from a server that
	/// never intends to resolve.
	/// </para>
	/// <para>
	/// Every other OAuth error code is split by what a caller can do about it, not merely by whether
	/// GitHub happened to send one. <c>access_denied</c> and <c>expired_token</c> are a fact about the
	/// person authorising and become <see cref="GitHostingAuthenticationException"/>, since offering
	/// the sign-in again is the caller's correct remedy for both. <c>incorrect_client_credentials</c>,
	/// <c>unsupported_grant_type</c>, and <c>device_flow_disabled</c> are a fact about the caller's own
	/// configuration and become <see cref="GitHostingRequestException"/> instead: none of the three can
	/// be fixed by the user trying again, so reporting them as an authentication failure would loop a
	/// person through a sign-in that can never succeed. A code this method does not recognize is
	/// deliberately treated as a <see cref="GitHostingRequestException"/> as well, on the same
	/// reasoning: an unrecognized code is either a GitHub error this library has not been taught yet or
	/// something upstream of GitHub answering instead, and in both cases a caller is better served
	/// being told something is wrong with the request than being invited to retry a sign-in for a
	/// reason nobody has verified sign-in can fix.
	/// </para>
	/// </remarks>
	/// <param name="code">What <see cref="RequestDeviceCodeAsync"/> returned.</param>
	/// <param name="cancellationToken">Abandons the wait.</param>
	/// <returns>A host-native token credential.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="code"/> is <see langword="null"/>.</exception>
	/// <exception cref="GitHostingAuthenticationException">The user refused, or the code expired.</exception>
	/// <exception cref="GitHostingRequestException">
	/// GitHub refused the request, could not be reached, reported a configuration fault
	/// (<c>incorrect_client_credentials</c>, <c>unsupported_grant_type</c>, <c>device_flow_disabled</c>),
	/// or reported an error code this method does not recognize.
	/// </exception>
	public async Task<HostingCredential> WaitForTokenAsync(GitHubDeviceCode code, CancellationToken cancellationToken = default)
	{
		Ensure.NotNull(code);
		cancellationToken.ThrowIfCancellationRequested();

		using HttpClient client = CreateHttpClient();

		GitHubAccessTokenRequestBody requestBody = new()
		{
			ClientId = _clientId.WeakString,
			DeviceCode = code.DeviceCode,
			GrantType = DeviceCodeGrantType,
		};

		string requestJson = JsonSerializer.Serialize(requestBody, GitHubDeviceFlowJsonContext.Default.GitHubAccessTokenRequestBody);

		TimeSpan interval = code.Interval;

		// A monotonic elapsed-time budget rather than a fixed end-of-wall-clock instant:
		// NowMilliseconds wraps Environment.TickCount64 by default, which is what makes this immune
		// to the machine's clock being changed mid-wait, forward or back.
		long startMilliseconds = NowMilliseconds();
		long expiresInMilliseconds = (long)code.ExpiresIn.TotalMilliseconds;

		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (NowMilliseconds() - startMilliseconds >= expiresInMilliseconds)
			{
				throw new GitHostingAuthenticationException("GitHub did not issue a token before the device code expired.");
			}

			using HttpRequestMessage request = CreateJsonRequest(AccessTokenEndpoint, requestJson);
			using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
			string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

			if (!response.IsSuccessStatusCode)
			{
				// Status and reason phrase only, not the body: this is the one place in this type
				// where a failure response comes from the endpoint that issues tokens, and a body
				// echoed back into an exception message is a body that can end up in a log.
				throw new GitHostingRequestException(
					$"GitHub refused the device flow token request: {(int)response.StatusCode} {response.ReasonPhrase}".TrimEnd());
			}

			GitHubAccessTokenResponseBody parsed = DeserializeOrThrow(
				body, GitHubDeviceFlowJsonContext.Default.GitHubAccessTokenResponseBody, "access token");

			switch (parsed.Error)
			{
				case "authorization_pending":
					await WaitAsync(interval, cancellationToken).ConfigureAwait(false);
					continue;

				case "slow_down":
					interval += SlowDownIncrement;
					await WaitAsync(interval, cancellationToken).ConfigureAwait(false);
					continue;

				case "access_denied" or "expired_token":
					// GitHub reports a refusal and an expiry as a 200 carrying an error field
					// rather than as a failure status, which is why this is read from the body
					// rather than caught as a failed HttpResponseMessage above. These two, and
					// only these two, are a fact about the person authorising: they said no, or
					// ran out of time, and the caller's own remedy is to offer the sign-in again.
					throw new GitHostingAuthenticationException(
						$"GitHub did not issue a token: {parsed.Error}. {parsed.ErrorDescription}".TrimEnd());

				case "incorrect_client_credentials" or "unsupported_grant_type" or "device_flow_disabled":
					// A fact about this library's caller, not about the person authorising: the
					// client identifier is wrong, revoked, or device flow was never enabled for it.
					// Retrying the sign-in cannot fix any of the three, so these are a request
					// fault rather than an authentication failure, matching the design's Failures
					// table. Offering the user another sign-in attempt here would loop them through
					// a flow that can never succeed.
					throw new GitHostingRequestException(
						$"GitHub refused the device flow token request: {parsed.Error}. {parsed.ErrorDescription}".TrimEnd());

				case string unrecognizedError:
					// A code this library does not recognize is treated as a request fault rather
					// than an authentication failure, deliberately: the two known authentication
					// codes above are enumerated explicitly, so anything else reaching here is
					// either a new GitHub error this library has not been taught yet, or a
					// misbehaving proxy or interstitial, and in both cases looping the user through
					// another sign-in attempt is more likely to be wrong than treating it as
					// something the caller or its configuration needs to look at.
					throw new GitHostingRequestException(
						$"GitHub reported an unrecognized device flow error: {unrecognizedError}. {parsed.ErrorDescription}".TrimEnd());

				default:
					// Every non-null error is handled above, so the error is null by the time control
					// reaches here and testing it again would be testing a constant. The only question
					// left is whether a token actually arrived alongside that absent error.
					if (string.IsNullOrEmpty(parsed.AccessToken))
					{
						throw new GitHostingRequestException("GitHub reported neither a token nor an error.");
					}

					// FromToken, not FromBearerToken: a GitHub OAuth token travels under Octokit's
					// Token scheme, which is what FromToken means. FromBearerToken is for an Entra
					// ID access token against Azure DevOps.
					return HostingCredential.FromToken(parsed.AccessToken);
			}
		}
	}

	/// <summary>Waits between polls, never for less than <see cref="MinimumPollInterval"/>.</summary>
	/// <param name="interval">The wait this flow would otherwise use.</param>
	/// <param name="cancellationToken">Cancels the wait.</param>
	/// <returns>A task that completes once the (possibly floored) wait has elapsed.</returns>
	private Task WaitAsync(TimeSpan interval, CancellationToken cancellationToken) =>
		Delay(interval < MinimumPollInterval ? MinimumPollInterval : interval, cancellationToken);

	/// <summary>Creates the transport this flow's calls share.</summary>
	/// <remarks>
	/// <see langword="disposeHandler: false"/> unconditionally: <see cref="SharedHandler"/> has to
	/// outlive every call, and an injected <see cref="Handler"/> belongs to whoever supplied it.
	/// </remarks>
	/// <returns>The client, to be disposed once the call using it is done.</returns>
	private HttpClient CreateHttpClient() => new(Handler ?? SharedHandler, disposeHandler: false);

	/// <summary>Builds a JSON <c>POST</c> request, asking GitHub to answer in JSON as well.</summary>
	/// <param name="endpoint">The endpoint to post to.</param>
	/// <param name="json">The already-serialized request body.</param>
	/// <returns>The request.</returns>
	private static HttpRequestMessage CreateJsonRequest(Uri endpoint, string json)
	{
		HttpRequestMessage request = new(HttpMethod.Post, endpoint)
		{
			Content = new StringContent(json, Encoding.UTF8, "application/json"),
		};

		request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

		return request;
	}

	/// <summary>Deserializes a response body, translating a malformed one into this library's own exception.</summary>
	/// <typeparam name="T">The shape the body is expected to have.</typeparam>
	/// <param name="body">The response body, already read.</param>
	/// <param name="typeInfo">The source-generated metadata to deserialize <typeparamref name="T"/> with.</param>
	/// <param name="what">What the body was expected to describe, folded into the failure message.</param>
	/// <returns>The deserialized body.</returns>
	/// <exception cref="GitHostingRequestException">The body is not valid JSON of the expected shape.</exception>
	private static T DeserializeOrThrow<T>(string body, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo, string what)
	{
		try
		{
			return JsonSerializer.Deserialize(body, typeInfo)
				?? throw new GitHostingRequestException($"GitHub reported success but returned an empty {what} body.");
		}
		catch (JsonException exception)
		{
			throw new GitHostingRequestException(
				$"GitHub reported success but returned a {what} body that is not the expected JSON: {exception.Message}",
				exception);
		}
	}
}

/// <summary>The body <see cref="GitHubDeviceFlow.RequestDeviceCodeAsync"/> sends.</summary>
internal sealed class GitHubDeviceCodeRequestBody
{
	/// <summary>Gets the OAuth App's client identifier.</summary>
	[JsonPropertyName("client_id")]
	public required string ClientId { get; init; }

	/// <summary>Gets the requested scopes, space-separated.</summary>
	[JsonPropertyName("scope")]
	public required string Scope { get; init; }
}

/// <summary>The body GitHub's device code endpoint returns.</summary>
internal sealed class GitHubDeviceCodeResponseBody
{
	/// <summary>Gets the code the token request carries.</summary>
	[JsonPropertyName("device_code")]
	public required string DeviceCode { get; init; }

	/// <summary>Gets the code the user enters.</summary>
	[JsonPropertyName("user_code")]
	public required string UserCode { get; init; }

	/// <summary>Gets the page the user opens.</summary>
	[JsonPropertyName("verification_uri")]
	public required string VerificationUri { get; init; }

	/// <summary>Gets how long the codes remain usable, in seconds.</summary>
	[JsonPropertyName("expires_in")]
	public required int ExpiresIn { get; init; }

	/// <summary>Gets the minimum wait between token requests, in seconds.</summary>
	[JsonPropertyName("interval")]
	public required int Interval { get; init; }
}

/// <summary>The body <see cref="GitHubDeviceFlow.WaitForTokenAsync"/> sends.</summary>
internal sealed class GitHubAccessTokenRequestBody
{
	/// <summary>Gets the OAuth App's client identifier.</summary>
	[JsonPropertyName("client_id")]
	public required string ClientId { get; init; }

	/// <summary>Gets the device code the earlier request obtained.</summary>
	[JsonPropertyName("device_code")]
	public required string DeviceCode { get; init; }

	/// <summary>Gets the grant type identifying this as a device-flow exchange.</summary>
	[JsonPropertyName("grant_type")]
	public required string GrantType { get; init; }
}

/// <summary>The body GitHub's token endpoint returns.</summary>
internal sealed class GitHubAccessTokenResponseBody
{
	/// <summary>Gets the issued token, or <see langword="null"/> when <see cref="Error"/> is set.</summary>
	[JsonPropertyName("access_token")]
	public string? AccessToken { get; init; }

	/// <summary>
	/// Gets the OAuth error code, such as <c>authorization_pending</c>, <c>slow_down</c>,
	/// <c>access_denied</c>, or <c>expired_token</c>, or <see langword="null"/> on success.
	/// </summary>
	[JsonPropertyName("error")]
	public string? Error { get; init; }

	/// <summary>Gets the human-readable detail accompanying <see cref="Error"/>, when GitHub sent one.</summary>
	[JsonPropertyName("error_description")]
	public string? ErrorDescription { get; init; }
}

/// <summary>Source-generated JSON metadata for the device flow's request and response bodies.</summary>
[JsonSerializable(typeof(GitHubDeviceCodeRequestBody))]
[JsonSerializable(typeof(GitHubDeviceCodeResponseBody))]
[JsonSerializable(typeof(GitHubAccessTokenRequestBody))]
[JsonSerializable(typeof(GitHubAccessTokenResponseBody))]
internal sealed partial class GitHubDeviceFlowJsonContext : JsonSerializerContext
{
}
