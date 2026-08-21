// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

using ktsu.CredentialCache;
using ktsu.Semantics.Strings;

// System.Net (for HttpStatusCode) and ktsu.CredentialCache both declare a type named
// CredentialCache; this alias resolves the ambiguity in favour of the credential store.
using CredentialCache = ktsu.CredentialCache.CredentialCache;

[TestClass]
public sealed class AzureDevOpsProviderTests
{
	// CredentialCache.Instance is configured onto an in-memory store exactly once, assembly-wide, by
	// CredentialCacheAssemblySetup's [AssemblyInitialize] — see that type's remarks for why a
	// per-class initializer doing this would race under this assembly's method-level test
	// parallelism. This class must not add a second call site.

	/// <summary>Reads a captured fixture's raw JSON text from the test output's Fixtures directory.</summary>
	private static string Fixture(string name) =>
		File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

	[TestMethod]
	public async Task RequestsTheOrganizationWideRepositoryEndpointWhenNoProjectIsSetAsync()
	{
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, Fixture("azure-devops-repositories.json"), ("Content-Type", "application/json"));
		AzureDevOpsProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		_ = await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		// Org-wide template and api-version both from findings section 2: the project path segment
		// is absent entirely, not merely empty, when no project is configured.
		Assert.AreEqual(
			new Uri("https://dev.azure.com/contoso/_apis/git/repositories?api-version=7.1"),
			handler.Requests[0].Uri);
	}

	[TestMethod]
	public async Task RequestsTheProjectScopedEndpointWhenAProjectIsSetAsync()
	{
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, Fixture("azure-devops-repositories.json"), ("Content-Type", "application/json"));
		AzureDevOpsProvider provider = new()
		{
			Owner = "contoso".As<GitProviderOwner>(),
			Project = "ExampleProject".As<AzureDevOpsProjectName>(),
			Handler = handler,
		};

		_ = await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		// Project-scoped template and api-version, both from findings section 2 — the same endpoint
		// as the org-wide form, with the project path segment present.
		Assert.AreEqual(
			new Uri("https://dev.azure.com/contoso/ExampleProject/_apis/git/repositories?api-version=7.1"),
			handler.Requests[0].Uri);
	}

	[TestMethod]
	public async Task SendsBasicAuthWithAnEmptyUsernameForATokenCredentialAsync()
	{
		PersonaGUID persona = CredentialCache.CreatePersonaGUID();
		CredentialCache.Instance.AddOrReplace(persona, new CredentialWithToken { Token = "pat-abc123".As<CredentialToken>() });

		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, Fixture("azure-devops-repositories.json"), ("Content-Type", "application/json"));
		AzureDevOpsProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), PersonaGUID = persona, Handler = handler };

		_ = await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		// Decoded rather than compared to a hardcoded base64 string: a hardcoded string would still
		// match if the username half were wrong, and Azure DevOps expects an empty username with the
		// token as password (findings section 2, contradictions entry 2).
		string header = handler.Requests[0].Headers["Authorization"];
		StringAssert.StartsWith(header, "Basic ");

		string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..]));
		Assert.AreEqual(":pat-abc123", decoded);
	}

	[TestMethod]
	public async Task SendsNoAuthorizationHeaderWhenUnauthenticatedAsync()
	{
		// A fresh persona that nothing ever seeded — proceeding unauthenticated is legitimate for
		// enumerating public repositories.
		PersonaGUID persona = CredentialCache.CreatePersonaGUID();

		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, Fixture("azure-devops-repositories.json"), ("Content-Type", "application/json"));
		AzureDevOpsProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), PersonaGUID = persona, Handler = handler };

		_ = await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		// Absent, not empty — an empty header would still (wrongly) pass a check for "no credential
		// leaked", but a real HTTP client never sends an empty Authorization header in the first
		// place, so absence is the only outcome that actually reflects "no credential applied".
		Assert.IsFalse(handler.Requests[0].Headers.ContainsKey("Authorization"));
	}

	[TestMethod]
	public async Task ParsesEveryRepositoryFieldAsync()
	{
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, Fixture("azure-devops-repositories.json"), ("Content-Type", "application/json"));
		AzureDevOpsProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		IReadOnlyList<GitRepository> repositories =
			await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		// Asserted on the parsed fields of the first two entries, against the fixture's real
		// values — a count-only assertion would pass even if every field were dropped.
		Assert.AreEqual(3, repositories.Count);

		Assert.AreEqual("example-repo-1".As<GitRepositoryName>(), repositories[0].Name);
		Assert.AreEqual(
			"https://dev.azure.com/contoso/ExampleProject/_git/example-repo-1".As<GitRepositoryRemotePath>(),
			repositories[0].RemotePath);
		// The fixture's first entry never populates webUrl — findings section 2 confirms no example
		// response the findings could check populates it, so null is the correct, faithful mapping
		// rather than a defect to work around.
		Assert.IsNull(repositories[0].WebURI);

		Assert.AreEqual("ExampleProject".As<GitRepositoryName>(), repositories[1].Name);
		Assert.AreEqual(
			"https://dev.azure.com/contoso/_git/ExampleProject".As<GitRepositoryRemotePath>(),
			repositories[1].RemotePath);
		Assert.IsNull(repositories[1].WebURI);
	}

	[TestMethod]
	public async Task TranslatesAnUnauthorizedResponseToGitHostingAuthenticationExceptionAsync()
	{
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.Unauthorized, "{\"message\":\"Access Denied\"}", ("Content-Type", "application/json"));
		AzureDevOpsProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		GitHostingAuthenticationException exception = await Assert.ThrowsExactlyAsync<GitHostingAuthenticationException>(
			async () => await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.AreEqual(HttpStatusCode.Unauthorized, exception.StatusCode);
		StringAssert.Contains(exception.ResponseBody, "Access Denied");
	}

	[TestMethod]
	public async Task TranslatesAForbiddenRateLimitResponseToGitHostingRateLimitExceptionAsync()
	{
		DateTimeOffset resetsAt = DateTimeOffset.FromUnixTimeSeconds(1798800000);
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(
				(HttpStatusCode)403,
				"{\"message\":\"Request was blocked due to exceeding usage\"}",
				("Content-Type", "application/json"),
				("X-RateLimit-Reset", "1798800000"));
		AzureDevOpsProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		GitHostingRateLimitException exception = await Assert.ThrowsExactlyAsync<GitHostingRateLimitException>(
			async () => await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.AreEqual((HttpStatusCode)403, exception.StatusCode);
		Assert.AreEqual(resetsAt, exception.ResetsAt);
	}

	[TestMethod]
	public async Task TranslatesANotFoundResponseToGitHostingNotFoundExceptionAsync()
	{
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.NotFound, "{\"message\":\"The requested resource was not found\"}", ("Content-Type", "application/json"));
		AzureDevOpsProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		GitHostingNotFoundException exception = await Assert.ThrowsExactlyAsync<GitHostingNotFoundException>(
			async () => await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.AreEqual(HttpStatusCode.NotFound, exception.StatusCode);
		StringAssert.Contains(exception.ResponseBody, "not found");
	}

	[TestMethod]
	public async Task TranslatesAnyOtherFailureToGitHostingRequestExceptionAsync()
	{
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.InternalServerError, "{\"message\":\"Something went wrong\"}", ("Content-Type", "application/json"));
		AzureDevOpsProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		GitHostingRequestException exception = await Assert.ThrowsExactlyAsync<GitHostingRequestException>(
			async () => await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.AreEqual(HttpStatusCode.InternalServerError, exception.StatusCode);
		StringAssert.Contains(exception.ResponseBody, "Something went wrong");
	}

	public TestContext TestContext { get; set; } = null!;
}
