// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
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
	/// <summary>A non-JSON body of the kind a proxy or sign-on page returns under a success status.</summary>
	private const string SignInPage = "<!DOCTYPE html><html><body>Sign in to continue</body></html>";

	// CredentialCache.Instance is configured onto an in-memory store exactly once, assembly-wide, by
	// CredentialCacheAssemblySetup's [AssemblyInitialize] — see that type's remarks for why a
	// per-class initializer doing this would race under this assembly's method-level test
	// parallelism. This class must not add a second call site.

	/// <summary>Reads a captured fixture's raw JSON text from the test output's Fixtures directory.</summary>
	private static string Fixture(string name) =>
		File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

	/// <summary>
	/// Wraps the single captured create-response fixture in the pull-request-list envelope
	/// (<c>{ "value": [...], "count": 1 }</c>, confirmed by findings section 3), varying only
	/// <c>status</c> — the payload's shape is otherwise untouched.
	/// </summary>
	/// <param name="status">The <c>status</c> value to substitute.</param>
	private static string SinglePullRequestListResponse(string status)
	{
		string json = Fixture("azure-devops-pullrequest-created.json")
			.Replace("\"status\": \"active\"", $"\"status\": \"{status}\"", StringComparison.Ordinal);
		return $"{{\"value\":[{json}],\"count\":1}}";
	}

	/// <summary>
	/// Adds a <c>web</c> entry to the captured create-response fixture's <c>_links</c> object.
	/// </summary>
	/// <remarks>
	/// Findings section "Contradictions and gaps" entry 1 confirms no published Azure DevOps example
	/// populates a <c>web</c> key inside a pull request's <c>_links</c> — this is the one place this
	/// test suite adds a key a captured fixture never carries, done deliberately and only here, to
	/// prove <see cref="AzureDevOpsProvider"/> reads <see cref="GitPullRequest.WebURI"/> from it when
	/// it is present. Every other key in <c>_links</c>, and the rest of the payload, stays exactly as
	/// captured.
	/// </remarks>
	private static string WithWebLink(string json, string href) =>
		json.Replace("\"_links\": {", $"\"_links\": {{\n    \"web\": {{ \"href\": \"{href}\" }},", StringComparison.Ordinal);

	/// <summary>
	/// Wraps one copy of the captured create-response fixture per supplied id in the pull-request-list
	/// envelope, substituting each copy's <c>pullRequestId</c>.
	/// </summary>
	/// <remarks>
	/// Paging cannot be exercised from a captured fixture alone: the provider asks for a page at a time
	/// and treats a page short of what it asked for as the last one, so proving it fetches a second page
	/// needs a first page that is genuinely full, and no captured response is that long. Only
	/// <c>pullRequestId</c> varies between the copies, which is what lets an assertion tell one page's
	/// entries from the other's.
	/// </remarks>
	/// <param name="pullRequestIds">The <c>pullRequestId</c> to give each entry, in order.</param>
	private static string PullRequestListPage(IEnumerable<int> pullRequestIds)
	{
		string template = Fixture("azure-devops-pullrequest-created.json");

		string[] entries = [.. pullRequestIds.Select(id =>
			template.Replace("\"pullRequestId\": 22", $"\"pullRequestId\": {id}", StringComparison.Ordinal))];

		return $"{{\"value\":[{string.Join(",", entries)}],\"count\":{entries.Length}}}";
	}

	/// <summary>
	/// Adds <c>"isDraft": true</c> to every pull request object in a captured payload.
	/// </summary>
	/// <remarks>
	/// The field is confirmed in Microsoft's schema but populated in none of the published example
	/// responses, so no captured fixture carries it, and its absence maps to
	/// <see cref="GitPullRequest.IsDraft"/>'s own <see langword="false"/> default. Asserting that
	/// default would pass with the mapping deleted, so the flag has to be varied to be worth asserting.
	/// The same additive treatment <see cref="WithWebLink"/> applies, for the same reason.
	/// </remarks>
	/// <param name="json">The captured payload to add the flag to.</param>
	private static string WithDraftFlag(string json) =>
		json.Replace("\"status\": \"active\",", "\"isDraft\": true, \"status\": \"active\",", StringComparison.Ordinal);

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
		StringAssert.Contains(exception.ResponseBody, "Request was blocked due to exceeding usage");
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

	[TestMethod]
	public async Task TranslatesAForbiddenResponseWithoutRateLimitHeadersToAnAuthenticationFailureAsync()
	{
		// The companion to TranslatesAForbiddenRateLimitResponseToGitHostingRateLimitExceptionAsync:
		// a single 403 test would pin only one branch of Translate's 403 routing and leave the other
		// free to regress. No X-RateLimit-* header is queued here at all.
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond((HttpStatusCode)403, "{\"message\":\"Access Denied\"}", ("Content-Type", "application/json"));
		AzureDevOpsProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		GitHostingAuthenticationException exception = await Assert.ThrowsExactlyAsync<GitHostingAuthenticationException>(
			async () => await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.AreEqual((HttpStatusCode)403, exception.StatusCode);
		StringAssert.Contains(exception.ResponseBody, "Access Denied");
	}

	[TestMethod]
	public async Task TranslatesATooManyRequestsResponseToGitHostingRateLimitExceptionAsync()
	{
		DateTimeOffset resetsAt = DateTimeOffset.FromUnixTimeSeconds(1798800000);
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(
				HttpStatusCode.TooManyRequests,
				"{\"message\":\"Request was blocked due to exceeding usage\"}",
				("Content-Type", "application/json"),
				("X-RateLimit-Reset", "1798800000"));
		AzureDevOpsProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		GitHostingRateLimitException exception = await Assert.ThrowsExactlyAsync<GitHostingRateLimitException>(
			async () => await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.AreEqual(HttpStatusCode.TooManyRequests, exception.StatusCode);
		StringAssert.Contains(exception.ResponseBody, "Request was blocked");
		Assert.AreEqual(resetsAt, exception.ResetsAt);
	}

	[TestMethod]
	public async Task ThrowsWhenPullRequestsAreRequestedWithoutAProjectAsync()
	{
		AzureDevOpsProvider provider = new() { Owner = "contoso".As<GitProviderOwner>() };

		InvalidOperationException exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
			async () => await provider.GetPullRequestsAsync("repo".As<GitRepositoryName>(), TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		StringAssert.Contains(exception.Message, nameof(AzureDevOpsProvider.Project));
	}

	[TestMethod]
	public async Task ThrowsWhenAPullRequestIsCreatedWithoutAProjectAsync()
	{
		AzureDevOpsProvider provider = new() { Owner = "contoso".As<GitProviderOwner>() };

		InvalidOperationException exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
			async () => await provider.CreatePullRequest("repo".As<GitRepositoryName>())
				.From("a".As<GitBranchName>()).Into("b".As<GitBranchName>()).Titled("t".As<GitPullRequestTitle>())
				.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		StringAssert.Contains(exception.Message, nameof(AzureDevOpsProvider.Project));
	}

	[TestMethod]
	public async Task RequestsThePullRequestsEndpointForTheConfiguredRepositoryAsync()
	{
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, Fixture("azure-devops-pullrequests.json"), ("Content-Type", "application/json"));
		AzureDevOpsProvider provider = new()
		{
			Owner = "contoso".As<GitProviderOwner>(),
			Project = "ExampleProject".As<AzureDevOpsProjectName>(),
			Handler = handler,
		};

		_ = await provider.GetPullRequestsAsync("example-repo".As<GitRepositoryName>(), TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(HttpMethod.Get, handler.Requests[0].Method);
		Assert.AreEqual(
			"https://dev.azure.com/contoso/ExampleProject/_apis/git/repositories/example-repo/pullrequests",
			handler.Requests[0].Uri.GetLeftPart(UriPartial.Path));

		// A first page short of the size asked for is the last page, so this costs exactly one
		// request. Without this, a paging bug that always asked for one page too many would show
		// up only as an unrelated "no queued response" failure.
		Assert.AreEqual(1, handler.Requests.Count);
	}

	[TestMethod]
	public async Task RequestsOnlyActivePullRequestsAsync()
	{
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, Fixture("azure-devops-pullrequests.json"), ("Content-Type", "application/json"));
		AzureDevOpsProvider provider = new()
		{
			Owner = "contoso".As<GitProviderOwner>(),
			Project = "ExampleProject".As<AzureDevOpsProjectName>(),
			Handler = handler,
		};

		_ = await provider.GetPullRequestsAsync("example-repo".As<GitRepositoryName>(), TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		// The library's contract is "open pull requests only", requested explicitly rather than
		// inherited from whatever Azure DevOps happens to default to today (findings section 3).
		StringAssert.Contains(handler.Requests[0].Uri.Query, "searchCriteria.status=active");
	}

	[TestMethod]
	public async Task StripsRefsHeadsFromTheBranchNamesAsync()
	{
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, Fixture("azure-devops-pullrequests.json"), ("Content-Type", "application/json"));
		AzureDevOpsProvider provider = new()
		{
			Owner = "contoso".As<GitProviderOwner>(),
			Project = "ExampleProject".As<AzureDevOpsProjectName>(),
			Handler = handler,
		};

		IReadOnlyList<GitPullRequest> pullRequests = await provider
			.GetPullRequestsAsync("example-repo".As<GitRepositoryName>(), TestContext.CancellationTokenSource.Token)
			.ConfigureAwait(false);

		// The fixture's first entry carries "refs/heads/npaulk/my_work" and
		// "refs/heads/new_feature" — asserted bare, not fully-qualified, so a caller never has to
		// branch on host to get a plain branch name.
		Assert.AreEqual("npaulk/my_work".As<GitBranchName>(), pullRequests[0].SourceBranch);
		Assert.AreEqual("new_feature".As<GitBranchName>(), pullRequests[0].TargetBranch);
	}

	[TestMethod]
	public async Task ParsesEveryPullRequestFieldAsync()
	{
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, WithDraftFlag(Fixture("azure-devops-pullrequests.json")), ("Content-Type", "application/json"));
		AzureDevOpsProvider provider = new()
		{
			Owner = "contoso".As<GitProviderOwner>(),
			Project = "ExampleProject".As<AzureDevOpsProjectName>(),
			Handler = handler,
		};

		IReadOnlyList<GitPullRequest> pullRequests = await provider
			.GetPullRequestsAsync("example-repo".As<GitRepositoryName>(), TestContext.CancellationTokenSource.Token)
			.ConfigureAwait(false);

		Assert.AreEqual(3, pullRequests.Count);

		GitPullRequest pullRequest = pullRequests[0];
		Assert.AreEqual("22".As<GitPullRequestNumber>(), pullRequest.Number);
		Assert.AreEqual("A new feature".As<GitPullRequestTitle>(), pullRequest.Title);
		Assert.AreEqual("Adding a new feature", pullRequest.Description);
		Assert.AreEqual("npaulk/my_work".As<GitBranchName>(), pullRequest.SourceBranch);
		Assert.AreEqual("new_feature".As<GitBranchName>(), pullRequest.TargetBranch);
		Assert.AreEqual("example-user@contoso.example".As<GitPullRequestAuthor>(), pullRequest.Author);
		Assert.AreEqual(GitPullRequestState.Open, pullRequest.State);
		// Asserted true, against a fixture varied to carry "isDraft": true. False is
		// GitPullRequest.IsDraft's own default, so asserting it would still pass with the
		// mapping deleted. See WithDraftFlag.
		Assert.IsTrue(pullRequest.IsDraft);
		// None of the list fixture's entries populate _links at all (findings section 3), so WebURI
		// stays null rather than being guessed from "url", the API address.
		Assert.IsNull(pullRequest.WebURI);
		Assert.AreEqual(
			DateTimeOffset.Parse("2016-11-01T16:30:31.6655471Z", CultureInfo.InvariantCulture, DateTimeStyles.None),
			pullRequest.CreatedAt);
	}

	[TestMethod]
	public async Task LeavesTheWebUriNullWhenTheWebLinkIsAbsentAsync()
	{
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, Fixture("azure-devops-pullrequests.json"), ("Content-Type", "application/json"));
		AzureDevOpsProvider provider = new()
		{
			Owner = "contoso".As<GitProviderOwner>(),
			Project = "ExampleProject".As<AzureDevOpsProjectName>(),
			Handler = handler,
		};

		IReadOnlyList<GitPullRequest> pullRequests = await provider
			.GetPullRequestsAsync("example-repo".As<GitRepositoryName>(), TestContext.CancellationTokenSource.Token)
			.ConfigureAwait(false);

		Assert.IsNull(pullRequests[0].WebURI);
	}

	[TestMethod]
	public async Task TakesTheWebUriFromTheWebLinkNotTheApiUrlAsync()
	{
		// The create-response fixture is the only one with a populated _links object at all, and it
		// carries no "web" key by default (findings section 4) — WithWebLink adds exactly that one
		// key, the additive variant ruling 2 in the task allows.
		string json = WithWebLink(
			Fixture("azure-devops-pullrequest-created.json"),
			"https://dev.azure.com/contoso/ExampleProject/_git/ExampleProject/pullrequest/22");

		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.Created, json, ("Content-Type", "application/json"));
		AzureDevOpsProvider provider = new()
		{
			Owner = "contoso".As<GitProviderOwner>(),
			Project = "ExampleProject".As<AzureDevOpsProjectName>(),
			Handler = handler,
		};

		GitPullRequest created = await provider.CreatePullRequest("example-repo".As<GitRepositoryName>())
			.From("npaulk/my_work".As<GitBranchName>())
			.Into("new_feature".As<GitBranchName>())
			.Titled("A new feature".As<GitPullRequestTitle>())
			.ExecuteAsync(TestContext.CancellationTokenSource.Token)
			.ConfigureAwait(false);

		// Asserted against the "web" value and proven distinct from "url" — a regression to reading
		// "url" (the API address) instead of "_links.web.href" would make this fail.
		Assert.AreEqual(
			"https://dev.azure.com/contoso/ExampleProject/_git/ExampleProject/pullrequest/22".As<GitPullRequestWebURI>(),
			created.WebURI);
		Assert.AreNotEqual(
			"https://dev.azure.com/contoso/_apis/git/repositories/3411ebc1-d5aa-464f-9615-0b527bc66719/pullRequests/22",
			created.WebURI!.WeakString);
	}

	[TestMethod]
	public async Task SendsTheConfiguredValuesInTheCreateBodyAsync()
	{
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.Created, Fixture("azure-devops-pullrequest-created.json"), ("Content-Type", "application/json"));
		AzureDevOpsProvider provider = new()
		{
			Owner = "contoso".As<GitProviderOwner>(),
			Project = "ExampleProject".As<AzureDevOpsProjectName>(),
			Handler = handler,
		};

		GitPullRequest created = await provider.CreatePullRequest("example-repo".As<GitRepositoryName>())
			.From("npaulk/my_work".As<GitBranchName>())
			.Into("new_feature".As<GitBranchName>())
			.Titled("A new feature".As<GitPullRequestTitle>())
			.Describing("Adding a new feature")
			.AsDraft()
			.ExecuteAsync(TestContext.CancellationTokenSource.Token)
			.ConfigureAwait(false);

		Assert.AreEqual(HttpMethod.Post, handler.Requests[0].Method);
		string body = handler.Requests[0].Body!;
		// Sent fully-qualified: GitPullRequestSpecification carries bare branch names, and Azure
		// DevOps's request body needs refs/heads/... (findings section 4) — the reverse of the
		// stripping GetPullRequestsAsync applies on the way back in.
		StringAssert.Contains(body, "\"sourceRefName\":\"refs/heads/npaulk/my_work\"");
		StringAssert.Contains(body, "\"targetRefName\":\"refs/heads/new_feature\"");
		StringAssert.Contains(body, "\"title\":\"A new feature\"");
		StringAssert.Contains(body, "\"description\":\"Adding a new feature\"");
		StringAssert.Contains(body, "\"isDraft\":true");

		Assert.AreEqual("22".As<GitPullRequestNumber>(), created.Number);
	}

	[TestMethod]
	public async Task MapsActiveCompletedAndAbandonedAsync()
	{
		static AzureDevOpsProvider MakeProvider(FakeHttpMessageHandler handler) => new()
		{
			Owner = "contoso".As<GitProviderOwner>(),
			Project = "ExampleProject".As<AzureDevOpsProjectName>(),
			Handler = handler,
		};

		using (FakeHttpMessageHandler activeHandler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, SinglePullRequestListResponse("active"), ("Content-Type", "application/json")))
		{
			IReadOnlyList<GitPullRequest> pullRequests = await MakeProvider(activeHandler)
				.GetPullRequestsAsync("example-repo".As<GitRepositoryName>(), TestContext.CancellationTokenSource.Token)
				.ConfigureAwait(false);
			Assert.AreEqual(GitPullRequestState.Open, pullRequests[0].State);
		}

		using (FakeHttpMessageHandler completedHandler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, SinglePullRequestListResponse("completed"), ("Content-Type", "application/json")))
		{
			IReadOnlyList<GitPullRequest> pullRequests = await MakeProvider(completedHandler)
				.GetPullRequestsAsync("example-repo".As<GitRepositoryName>(), TestContext.CancellationTokenSource.Token)
				.ConfigureAwait(false);
			Assert.AreEqual(GitPullRequestState.Merged, pullRequests[0].State);
		}

		using (FakeHttpMessageHandler abandonedHandler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, SinglePullRequestListResponse("abandoned"), ("Content-Type", "application/json")))
		{
			IReadOnlyList<GitPullRequest> pullRequests = await MakeProvider(abandonedHandler)
				.GetPullRequestsAsync("example-repo".As<GitRepositoryName>(), TestContext.CancellationTokenSource.Token)
				.ConfigureAwait(false);
			Assert.AreEqual(GitPullRequestState.Closed, pullRequests[0].State);
		}
	}

	[TestMethod]
	public async Task FetchesEveryPageOfPullRequestsAsync()
	{
		// A first page returned full is the only thing that makes the provider ask for a second, and
		// Azure DevOps documents no continuation token on this endpoint, so a short page is the only
		// end-of-results signal available. 100 is the $top the provider sends.
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, PullRequestListPage(Enumerable.Range(1, 100)), ("Content-Type", "application/json"))
			.Respond(HttpStatusCode.OK, PullRequestListPage([101, 102]), ("Content-Type", "application/json"));
		AzureDevOpsProvider provider = new()
		{
			Owner = "contoso".As<GitProviderOwner>(),
			Project = "ExampleProject".As<AzureDevOpsProjectName>(),
			Handler = handler,
		};

		IReadOnlyList<GitPullRequest> pullRequests = await provider
			.GetPullRequestsAsync("example-repo".As<GitRepositoryName>(), TestContext.CancellationTokenSource.Token)
			.ConfigureAwait(false);

		// $top and $skip are the documented pagination for this endpoint, and $top is sent on the
		// first page too: without it a short page could equally mean the service applied its own
		// default, and the provider could not tell the last page from a truncated one.
		Assert.AreEqual(2, handler.Requests.Count);
		StringAssert.Contains(handler.Requests[0].Uri.Query, "$top=100");
		StringAssert.Contains(handler.Requests[0].Uri.Query, "$skip=0");
		StringAssert.Contains(handler.Requests[1].Uri.Query, "$top=100");
		StringAssert.Contains(handler.Requests[1].Uri.Query, "$skip=100");

		// The second page's entries are in the result, not merely its request issued: returning only
		// the first page's 100 is exactly the silent truncation paging exists to avoid.
		Assert.AreEqual(102, pullRequests.Count);
		Assert.AreEqual("1".As<GitPullRequestNumber>(), pullRequests[0].Number);
		Assert.AreEqual("100".As<GitPullRequestNumber>(), pullRequests[99].Number);
		Assert.AreEqual("101".As<GitPullRequestNumber>(), pullRequests[100].Number);
		Assert.AreEqual("102".As<GitPullRequestNumber>(), pullRequests[101].Number);
	}

	[TestMethod]
	public async Task TranslatesAnUnparsableRepositoryListBodyToGitHostingRequestExceptionAsync()
	{
		// A 200 carrying HTML is what a proxy interstitial, a captive portal, or a single sign-on
		// redirect page looks like. Unwrapped, System.Text.Json's JsonException would escape a public
		// method whose documented failure surface is the GitHostingException hierarchy.
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, SignInPage, ("Content-Type", "text/html"));
		AzureDevOpsProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		GitHostingRequestException exception = await Assert.ThrowsExactlyAsync<GitHostingRequestException>(
			async () => await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		// The status the host actually reported, not a synthesised failure code.
		Assert.AreEqual(HttpStatusCode.OK, exception.StatusCode);
		// The body travels on the exception, which is what makes such a page diagnosable rather than
		// a parse error with no evidence attached.
		StringAssert.Contains(exception.ResponseBody, "Sign in to continue");
	}

	[TestMethod]
	public async Task TranslatesAnUnparsablePullRequestListBodyToGitHostingRequestExceptionAsync()
	{
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, SignInPage, ("Content-Type", "text/html"));
		AzureDevOpsProvider provider = new()
		{
			Owner = "contoso".As<GitProviderOwner>(),
			Project = "ExampleProject".As<AzureDevOpsProjectName>(),
			Handler = handler,
		};

		GitHostingRequestException exception = await Assert.ThrowsExactlyAsync<GitHostingRequestException>(
			async () => await provider.GetPullRequestsAsync("example-repo".As<GitRepositoryName>(), TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.AreEqual(HttpStatusCode.OK, exception.StatusCode);
		StringAssert.Contains(exception.ResponseBody, "Sign in to continue");
	}

	[TestMethod]
	public async Task TranslatesAnUnparsableCreateResponseBodyToGitHostingRequestExceptionAsync()
	{
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.Created, SignInPage, ("Content-Type", "text/html"));
		AzureDevOpsProvider provider = new()
		{
			Owner = "contoso".As<GitProviderOwner>(),
			Project = "ExampleProject".As<AzureDevOpsProjectName>(),
			Handler = handler,
		};

		GitHostingRequestException exception = await Assert.ThrowsExactlyAsync<GitHostingRequestException>(
			async () => await provider.CreatePullRequest("example-repo".As<GitRepositoryName>())
				.From("a".As<GitBranchName>()).Into("b".As<GitBranchName>()).Titled("t".As<GitPullRequestTitle>())
				.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.AreEqual(HttpStatusCode.Created, exception.StatusCode);
		StringAssert.Contains(exception.ResponseBody, "Sign in to continue");
	}

	[TestMethod]
	public async Task LeavesAnInjectedHandlerUndisposedAndUsableForASecondCallAsync()
	{
		// The GitHubProviderTests counterpart, through the other transport: this provider builds a
		// per-call HttpClient and disposes it after every request, which must not take the injected
		// handler with it. It equally protects the shared default handler, which a test cannot
		// observe directly.
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, Fixture("azure-devops-repositories.json"), ("Content-Type", "application/json"))
			.Respond(HttpStatusCode.OK, Fixture("azure-devops-repositories.json"), ("Content-Type", "application/json"));
		AzureDevOpsProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		IReadOnlyList<GitRepository> first =
			await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);
		Assert.IsFalse(handler.WasDisposed);

		IReadOnlyList<GitRepository> second =
			await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);
		Assert.IsFalse(handler.WasDisposed);

		Assert.AreEqual(2, handler.Requests.Count);
		Assert.AreEqual("example-repo-1".As<GitRepositoryName>(), first[0].Name);
		Assert.AreEqual("example-repo-1".As<GitRepositoryName>(), second[0].Name);
	}

	public TestContext TestContext { get; set; } = null!;
}
