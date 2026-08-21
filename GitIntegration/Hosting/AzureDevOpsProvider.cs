// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

/// <summary>
/// Provides integration with Azure DevOps repositories over a raw <see cref="HttpClient"/>.
/// </summary>
/// <remarks>
/// Unlike <see cref="GitHubProvider"/>, this provider has no client library to lean on — Azure
/// DevOps hosting support was deliberately kept off the two official TFS/Azure DevOps client
/// packages, because they pull a package with a known high-severity advisory into this library's
/// dependency graph (see <see cref="GitProvider"/>'s remarks on the hosting layer). Every request
/// here is therefore built and parsed by hand, against the exact URL templates, <c>api-version</c>,
/// and field names recorded in
/// <c>docs/superpowers/research/2026-08-21-azure-devops-rest-findings.md</c>.
/// </remarks>
public sealed class AzureDevOpsProvider : GitProvider
{
	/// <summary>
	/// The <c>api-version</c> query parameter every request in this provider carries.
	/// </summary>
	/// <remarks>
	/// <c>7.1</c>, not the <c>7.2-preview.2</c> the documentation's default view reports — findings
	/// section 1 confirms 7.1 is stable, documents every field and query parameter this provider
	/// needs, and rules out the preview surface deliberately: a <c>-preview</c> pin is a durable
	/// liability for a library shipped to nuget.org, since Microsoft can change or withdraw it under
	/// a consumer who already upgraded.
	/// </remarks>
	private const string ApiVersion = "7.1";

	/// <summary>
	/// Gets the name of this Git provider.
	/// </summary>
	public override GitProviderName Name => "AzureDevOps".As<GitProviderName>();

	/// <summary>
	/// Gets or initializes the Azure DevOps project to scope repository enumeration to, or
	/// <see langword="null"/> to enumerate every repository in <see cref="GitProvider.Owner"/>'s
	/// organisation.
	/// </summary>
	/// <remarks>
	/// Azure DevOps nests repositories under a project, which GitHub has no equivalent of — see
	/// <see cref="AzureDevOpsProjectName"/>'s remarks. Findings section 2 confirms the org-wide and
	/// project-scoped forms are the same endpoint with the project path segment present or absent,
	/// not two different routes, which is exactly what <see cref="Project"/> being optional models.
	/// </remarks>
	public AzureDevOpsProjectName? Project { get; init; }

	/// <inheritdoc/>
	/// <remarks>
	/// Calls <c>GET https://dev.azure.com/{organization}/[{project}/]_apis/git/repositories</c> —
	/// the project path segment appears only when <see cref="Project"/> is set. Findings section 5
	/// confirms this endpoint has no pagination parameters at all: the response is the complete
	/// repository list for the organisation or project on every call, so this method issues exactly
	/// one request.
	/// </remarks>
	public override async Task<IReadOnlyList<GitRepository>> GetRepositoriesAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		// Both of these can throw — BuildRepositoriesUri is defensive rather than a real risk, but
		// ResolveCredential genuinely can (InvalidOperationException for a credential subtype this
		// library does not recognise). Both run before CreateHttpClient so a throw here never leaves
		// a constructed HttpClient stranded with nothing left to dispose it.
		Uri requestUri = BuildRepositoriesUri();
		HostingCredential credential = ResolveCredential();

		HttpClient client = CreateHttpClient();

		try
		{
			using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
			ApplyAuthentication(request, credential);

			using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
			string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

			if (!response.IsSuccessStatusCode)
			{
				throw Translate(response, body);
			}

			AzureDevOpsRepositoryListResponse? parsed = JsonSerializer.Deserialize(
				body, AzureDevOpsJsonContext.Default.AzureDevOpsRepositoryListResponse);

			return parsed is null ? [] : [.. parsed.Value.Select(ToGitRepository)];
		}
		finally
		{
			// This client owns its handler exactly when it constructed one — CreateHttpClient's
			// disposeHandler flag is false whenever a test injected Handler, so disposing here never
			// tears down a handler this provider does not own and a later call would reuse.
			client.Dispose();
		}
	}

	/// <inheritdoc/>
	// The real implementation lists the repository's open pull requests through the same transport
	// GetRepositoriesAsync uses, with $skip/$top pagination; it is not written yet.
	public override Task<IReadOnlyList<GitPullRequest>> GetPullRequestsAsync(GitRepositoryName repositoryName, CancellationToken cancellationToken = default) =>
		throw new NotImplementedException("Azure DevOps pull request listing is not yet implemented.");

	/// <inheritdoc/>
	// The real implementation submits the specification over the same transport and maps the result
	// back to GitPullRequest; it is not written yet.
	internal override Task<GitPullRequest> CreatePullRequestCoreAsync(GitRepositoryName repositoryName, GitPullRequestSpecification specification, CancellationToken cancellationToken) =>
		throw new NotImplementedException("Azure DevOps pull request creation is not yet implemented.");

	/// <summary>
	/// Builds the repository-list request URI for this provider's <see cref="GitProvider.Owner"/>
	/// and <see cref="Project"/>.
	/// </summary>
	/// <returns>The request URI, carrying <see cref="ApiVersion"/>.</returns>
	private Uri BuildRepositoriesUri()
	{
		string organization = Uri.EscapeDataString(Owner.WeakString);

		string path = Project is null
			? $"https://dev.azure.com/{organization}/_apis/git/repositories"
			: $"https://dev.azure.com/{organization}/{Uri.EscapeDataString(Project.WeakString)}/_apis/git/repositories";

		return new Uri($"{path}?api-version={ApiVersion}");
	}

	/// <summary>
	/// Applies this provider's resolved credential to a request, Azure DevOps's way.
	/// </summary>
	/// <remarks>
	/// Findings section 2's contradictions-and-gaps entry 2 confirms Azure DevOps's getting-started
	/// page: Basic authentication with an <b>empty username</b> and the token as the password. A
	/// resolved username/password credential is sent the conventional way instead — username and
	/// password both populated — since nothing in the findings suggests Azure DevOps rejects that
	/// shape, and <see cref="HostingCredential"/> already distinguishes it from a bearer token.
	/// </remarks>
	/// <param name="request">The request to apply the credential to.</param>
	/// <param name="credential">This provider's resolved credential.</param>
	private static void ApplyAuthentication(HttpRequestMessage request, HostingCredential credential)
	{
		switch (credential.Kind)
		{
			case HostingCredentialKind.Token:
				request.Headers.Authorization = BasicAuthenticationHeader(string.Empty, credential.Token ?? string.Empty);
				break;
			case HostingCredentialKind.UsernamePassword:
				request.Headers.Authorization = BasicAuthenticationHeader(credential.Username ?? string.Empty, credential.Password ?? string.Empty);
				break;
			case HostingCredentialKind.None:
			default:
				// No Authorization header at all — distinct from an empty one, and what an
				// unauthenticated caller enumerating public repositories needs.
				break;
		}
	}

	/// <summary>Builds a Basic authentication header from a username and password.</summary>
	/// <param name="username">The username half of the credential.</param>
	/// <param name="password">The password half of the credential.</param>
	/// <returns>The header value, ready to assign to <see cref="HttpRequestHeaders.Authorization"/>.</returns>
	private static AuthenticationHeaderValue BasicAuthenticationHeader(string username, string password)
	{
		string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
		return new AuthenticationHeaderValue("Basic", encoded);
	}

	/// <summary>
	/// Maps an Azure DevOps repository onto this library's model.
	/// </summary>
	/// <remarks>
	/// <see cref="GitRepository.LocalPath"/> is required, but a repository this provider enumerates
	/// from the host has never been cloned, so there is no real path to report — mirrors
	/// <see cref="GitHubProvider"/>'s own mapping, using the same destination a bare
	/// <c>git clone &lt;url&gt;</c> would pick.
	/// </remarks>
	/// <param name="repository">The repository Azure DevOps returned.</param>
	/// <returns>The equivalent <see cref="GitRepository"/>.</returns>
	private static GitRepository ToGitRepository(AzureDevOpsRepository repository)
	{
		string name = repository.Name ?? string.Empty;

		return new GitRepository
		{
			LocalPath = Path.Combine(Environment.CurrentDirectory, name).As<AbsoluteDirectoryPath>(),
			Name = repository.Name is string repositoryName ? repositoryName.As<GitRepositoryName>() : null,
			WebURI = repository.WebUrl is string webUrl ? webUrl.As<GitRepositoryWebURI>() : null,
			RemotePath = repository.RemoteUrl is string remoteUrl ? remoteUrl.As<GitRepositoryRemotePath>() : null,
		};
	}

	/// <summary>
	/// Maps a failed Azure DevOps response onto this library's hosting exception hierarchy.
	/// </summary>
	/// <remarks>
	/// Status-code routing matches
	/// <c>docs/superpowers/specs/2026-08-21-gitintegration-hosting-layer-design.md</c>'s Errors
	/// section: 401, and 403 without rate-limit headers, become
	/// <see cref="GitHostingAuthenticationException"/>; 429, and 403 with rate-limit headers, become
	/// <see cref="GitHostingRateLimitException"/>; 404 becomes
	/// <see cref="GitHostingNotFoundException"/>; anything else becomes
	/// <see cref="GitHostingRequestException"/>. The rate-limit header names, and
	/// <c>X-RateLimit-Reset</c> as the reset-time header, come from findings section 7.
	/// </remarks>
	/// <param name="response">The failed response.</param>
	/// <param name="responseBody">The response body, already read.</param>
	/// <returns>The equivalent <see cref="GitHostingException"/>, ready to throw.</returns>
	private GitHostingException Translate(HttpResponseMessage response, string responseBody)
	{
		HttpStatusCode statusCode = response.StatusCode;
		bool hasRateLimitHeaders = response.Headers.Contains("X-RateLimit-Limit")
			|| response.Headers.Contains("X-RateLimit-Remaining")
			|| response.Headers.Contains("X-RateLimit-Reset");

		string message = ExtractMessage(responseBody);

		if (statusCode == HttpStatusCode.TooManyRequests || (statusCode == HttpStatusCode.Forbidden && hasRateLimitHeaders))
		{
			return new GitHostingRateLimitException(message, Name, statusCode, responseBody, TryGetRateLimitReset(response.Headers));
		}

		if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
		{
			return new GitHostingAuthenticationException(message, Name, statusCode, responseBody);
		}

		if (statusCode == HttpStatusCode.NotFound)
		{
			return new GitHostingNotFoundException(message, Name, statusCode, responseBody);
		}

		return new GitHostingRequestException(message, Name, statusCode, responseBody);
	}

	/// <summary>
	/// Reads the human-readable message from an Azure DevOps error body, falling back to the raw
	/// body when it does not parse as the documented shape.
	/// </summary>
	/// <param name="responseBody">The raw response body.</param>
	/// <returns>The extracted message, or <paramref name="responseBody"/> itself as a fallback.</returns>
	private static string ExtractMessage(string responseBody)
	{
		try
		{
			AzureDevOpsErrorResponse? error = JsonSerializer.Deserialize(
				responseBody, AzureDevOpsJsonContext.Default.AzureDevOpsErrorResponse);
			return error?.Message ?? responseBody;
		}
		catch (JsonException)
		{
			// A rate-limit block, in particular, is documented as plain text rather than JSON (see
			// findings section 7) — falling back to the raw body is the correct outcome here, not a
			// swallowed failure, since the raw body IS the message in that case.
			return responseBody;
		}
	}

	/// <summary>Reads the rate-limit reset time from a response's headers, if present.</summary>
	/// <param name="headers">The response headers.</param>
	/// <returns>The reset time, or <see langword="null"/> when the header is absent or unparsable.</returns>
	private static DateTimeOffset? TryGetRateLimitReset(HttpResponseHeaders headers)
	{
		if (headers.TryGetValues("X-RateLimit-Reset", out IEnumerable<string>? values)
			&& long.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long epochSeconds))
		{
			return DateTimeOffset.FromUnixTimeSeconds(epochSeconds);
		}

		return null;
	}
}
