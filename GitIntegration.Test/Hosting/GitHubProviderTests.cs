// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;

using ktsu.CredentialCache;
using ktsu.Semantics.Strings;

// System.Net (for HttpStatusCode) and ktsu.CredentialCache both declare a type named
// CredentialCache; this alias resolves the ambiguity in favour of the credential store.
using CredentialCache = ktsu.CredentialCache.CredentialCache;

[TestClass]
public sealed class GitHubProviderTests
{
	// CredentialCache.Instance is configured onto an in-memory store exactly once, assembly-wide, by
	// CredentialCacheAssemblySetup's [AssemblyInitialize] — see that type's remarks for why a
	// per-class static constructor doing this raced against GitProviderTests' own, under this
	// assembly's method-level test parallelism.

	/// <summary>Reads a captured fixture's raw JSON text from the test output's Fixtures directory.</summary>
	private static string Fixture(string name) =>
		File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

	/// <summary>
	/// Wraps the single captured pull request fixture in a one-element array — the shape
	/// <c>GetAllForRepository</c> actually returns — varying <c>state</c>, <c>merged</c>, and
	/// <c>merged_at</c> to exercise each of the three <see cref="GitPullRequestState"/> mappings from
	/// one captured payload. GitHub's <c>state</c> field is only ever <c>open</c> or <c>closed</c>;
	/// <c>merged</c> is what tells the two closed outcomes apart. <c>merged_at</c> is varied alongside
	/// it because Octokit's own <c>PullRequest.Merged</c> is computed from whether <c>MergedAt</c> is
	/// non-null rather than read from the <c>merged</c> field directly — a real merged pull request
	/// always carries both, so this keeps the fixture internally consistent the way a real captured
	/// merged response would be. The payload's shape is untouched — only these field values change.
	/// </summary>
	private static string SinglePullRequestArray(string state, bool merged)
	{
		string json = Fixture("github-pullrequest-created.json")
			.Replace("\"state\": \"open\"", $"\"state\": \"{state}\"", StringComparison.Ordinal)
			.Replace("\"merged\": false", $"\"merged\": {(merged ? "true" : "false")}", StringComparison.Ordinal);

		if (merged)
		{
			json = json.Replace("\"merged_at\": null", "\"merged_at\": \"2026-08-21T00:50:00Z\"", StringComparison.Ordinal);
		}

		return $"[{json}]";
	}

	[TestMethod]
	public async Task AppliesATokenCredentialToTheRequestAsync()
	{
		PersonaGUID persona = CredentialCache.CreatePersonaGUID();
		CredentialCache.Instance.AddOrReplace(persona, new CredentialWithToken { Token = "ghp_abc123".As<CredentialToken>() });

		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, Fixture("github-repositories.json"), ("Content-Type", "application/json"));
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), PersonaGUID = persona, Handler = handler };

		_ = await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		// Proves ResolveCredential's result actually reaches the Octokit client, not merely that
		// ResolveCredential itself resolves correctly (GitProviderTests already covers that).
		Assert.AreEqual("Token ghp_abc123", handler.Requests[0].Headers["Authorization"]);
	}

	[TestMethod]
	public async Task EnumeratesRepositoriesForTheOwnerAsync()
	{
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, Fixture("github-repositories.json"), ("Content-Type", "application/json"));
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		IReadOnlyList<GitRepository> repositories =
			await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		// Asserted on the parsed fields of the first two entries, against the fixture's real
		// values — a count-only assertion would pass even if every field were dropped.
		Assert.AreEqual(3, repositories.Count);

		Assert.AreEqual("example-repo-1".As<GitRepositoryName>(), repositories[0].Name);
		Assert.AreEqual("https://github.com/example-user/example-repo-1.git".As<GitRepositoryRemotePath>(), repositories[0].RemotePath);
		Assert.AreEqual("https://github.com/example-user/example-repo-1".As<GitRepositoryWebURI>(), repositories[0].WebURI);

		Assert.AreEqual("example-repo-2".As<GitRepositoryName>(), repositories[1].Name);
		Assert.AreEqual("https://github.com/example-user/example-repo-2.git".As<GitRepositoryRemotePath>(), repositories[1].RemotePath);
		Assert.AreEqual("https://github.com/example-user/example-repo-2".As<GitRepositoryWebURI>(), repositories[1].WebURI);
	}

	[TestMethod]
	public async Task MapsAnOpenPullRequestToOpenAsync()
	{
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, SinglePullRequestArray("open", merged: false), ("Content-Type", "application/json"));
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		IReadOnlyList<GitPullRequest> pullRequests = await provider
			.GetPullRequestsAsync("example-repo".As<GitRepositoryName>(), TestContext.CancellationTokenSource.Token)
			.ConfigureAwait(false);

		Assert.AreEqual("132594".As<GitPullRequestNumber>(), pullRequests[0].Number);
		Assert.AreEqual(GitPullRequestState.Open, pullRequests[0].State);
	}

	[TestMethod]
	public async Task MapsAClosedUnmergedPullRequestToClosedAsync()
	{
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, SinglePullRequestArray("closed", merged: false), ("Content-Type", "application/json"));
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		IReadOnlyList<GitPullRequest> pullRequests = await provider
			.GetPullRequestsAsync("example-repo".As<GitRepositoryName>(), TestContext.CancellationTokenSource.Token)
			.ConfigureAwait(false);

		Assert.AreEqual(GitPullRequestState.Closed, pullRequests[0].State);
	}

	[TestMethod]
	public async Task MapsAMergedPullRequestToMergedAsync()
	{
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, SinglePullRequestArray("closed", merged: true), ("Content-Type", "application/json"));
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		IReadOnlyList<GitPullRequest> pullRequests = await provider
			.GetPullRequestsAsync("example-repo".As<GitRepositoryName>(), TestContext.CancellationTokenSource.Token)
			.ConfigureAwait(false);

		Assert.AreEqual(GitPullRequestState.Merged, pullRequests[0].State);
	}

	[TestMethod]
	public async Task MapsEveryFieldOfAPullRequestAsync()
	{
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, SinglePullRequestArray("open", merged: false), ("Content-Type", "application/json"));
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		IReadOnlyList<GitPullRequest> pullRequests = await provider
			.GetPullRequestsAsync("example-repo".As<GitRepositoryName>(), TestContext.CancellationTokenSource.Token)
			.ConfigureAwait(false);

		// Every mapped field asserted against the fixture's real values, not just Number/State/WebURI:
		// SourceBranch/TargetBranch come from Head.Ref/Base.Ref and a swap between the two would ship
		// green if only one side were checked, and a dropped Title, Description, Author, IsDraft, or
		// CreatedAt would likewise pass a narrower assertion set.
		GitPullRequest pullRequest = pullRequests[0];
		Assert.AreEqual("132594".As<GitPullRequestNumber>(), pullRequest.Number);
		Assert.AreEqual("Don't build all JIT flavors for clr.aot".As<GitPullRequestTitle>(), pullRequest.Title);
		StringAssert.Contains(pullRequest.Description, "After #131666");
		Assert.AreEqual("example-branch-1".As<GitBranchName>(), pullRequest.SourceBranch);
		Assert.AreEqual("main".As<GitBranchName>(), pullRequest.TargetBranch);
		Assert.AreEqual("example-user-1".As<GitPullRequestAuthor>(), pullRequest.Author);
		Assert.AreEqual(GitPullRequestState.Open, pullRequest.State);
		Assert.IsFalse(pullRequest.IsDraft);
		Assert.AreEqual("https://github.com/contoso/example-repo/pull/132594".As<GitPullRequestWebURI>(), pullRequest.WebURI);
		Assert.AreEqual(new DateTimeOffset(2026, 8, 21, 0, 43, 5, TimeSpan.Zero), pullRequest.CreatedAt);
	}

	[TestMethod]
	public async Task RequestsOnlyOpenPullRequestsAsync()
	{
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, "[]", ("Content-Type", "application/json"));
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		_ = await provider
			.GetPullRequestsAsync("example-repo".As<GitRepositoryName>(), TestContext.CancellationTokenSource.Token)
			.ConfigureAwait(false);

		// The library's contract is "open pull requests only", requested explicitly rather than
		// inherited from whatever GitHub happens to default to today.
		StringAssert.Contains(handler.Requests[0].Uri.Query, "state=open");
	}

	[TestMethod]
	public async Task SendsTheConfiguredValuesInTheCreateBodyAsync()
	{
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.Created, Fixture("github-pullrequest-created.json"), ("Content-Type", "application/json"));
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		GitPullRequest created = await provider.CreatePullRequest("example-repo".As<GitRepositoryName>())
			.From("example-branch-1".As<GitBranchName>())
			.Into("main".As<GitBranchName>())
			.Titled("Don't build all JIT flavors for clr.aot".As<GitPullRequestTitle>())
			.Describing("Because reasons")
			.AsDraft()
			.ExecuteAsync(TestContext.CancellationTokenSource.Token)
			.ConfigureAwait(false);

		string body = handler.Requests[0].Body!;
		StringAssert.Contains(body, "\"title\":\"Don't build all JIT flavors for clr.aot\"");
		StringAssert.Contains(body, "\"head\":\"example-branch-1\"");
		StringAssert.Contains(body, "\"base\":\"main\"");
		StringAssert.Contains(body, "\"body\":\"Because reasons\"");
		StringAssert.Contains(body, "\"draft\":true");

		Assert.AreEqual("132594".As<GitPullRequestNumber>(), created.Number);
		Assert.AreEqual("https://github.com/contoso/example-repo/pull/132594".As<GitPullRequestWebURI>(), created.WebURI);
	}

	[TestMethod]
	public async Task TranslatesAnUnauthorizedResponseToGitHostingAuthenticationExceptionAsync()
	{
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.Unauthorized, "{\"message\":\"Bad credentials\"}", ("Content-Type", "application/json"));
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		GitHostingAuthenticationException exception = await Assert.ThrowsExactlyAsync<GitHostingAuthenticationException>(
			async () => await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.AreEqual(HttpStatusCode.Unauthorized, exception.StatusCode);
		StringAssert.Contains(exception.ResponseBody, "Bad credentials");
	}

	[TestMethod]
	public async Task TranslatesAForbiddenRateLimitResponseToGitHostingRateLimitExceptionAsync()
	{
		DateTimeOffset resetsAt = DateTimeOffset.FromUnixTimeSeconds(1798800000);
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(
				(HttpStatusCode)403,
				"{\"message\":\"API rate limit exceeded\"}",
				("Content-Type", "application/json"),
				("X-RateLimit-Limit", "60"),
				("X-RateLimit-Remaining", "0"),
				("X-RateLimit-Reset", "1798800000"));
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		GitHostingRateLimitException exception = await Assert.ThrowsExactlyAsync<GitHostingRateLimitException>(
			async () => await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.AreEqual((HttpStatusCode)403, exception.StatusCode);
		StringAssert.Contains(exception.ResponseBody, "API rate limit exceeded");
		Assert.AreEqual(resetsAt, exception.ResetsAt);
	}

	[TestMethod]
	public async Task TranslatesANotFoundResponseToGitHostingNotFoundExceptionAsync()
	{
		// Octokit's GET path (used by GetRepositoriesAsync/GetPullRequestsAsync) reports a 404
		// through a synthetic NotFoundException with no response attached, which would make
		// ResponseBody trivially empty here. The create (POST) path is exercised instead, where
		// Octokit does attach the response and ResponseBody is genuinely populated.
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.NotFound, "{\"message\":\"Not Found\"}", ("Content-Type", "application/json"));
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		GitHostingNotFoundException exception = await Assert.ThrowsExactlyAsync<GitHostingNotFoundException>(
			async () => await provider.CreatePullRequest("example-repo".As<GitRepositoryName>())
				.From("a".As<GitBranchName>()).Into("b".As<GitBranchName>()).Titled("t".As<GitPullRequestTitle>())
				.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.AreEqual(HttpStatusCode.NotFound, exception.StatusCode);
		StringAssert.Contains(exception.ResponseBody, "Not Found");
	}

	[TestMethod]
	public async Task TranslatesAValidationFailureToGitHostingRequestExceptionAsync()
	{
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.UnprocessableEntity, "{\"message\":\"Validation Failed\"}", ("Content-Type", "application/json"));
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		GitHostingRequestException exception = await Assert.ThrowsExactlyAsync<GitHostingRequestException>(
			async () => await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.AreEqual(HttpStatusCode.UnprocessableEntity, exception.StatusCode);
		StringAssert.Contains(exception.ResponseBody, "Validation Failed");
	}

	public TestContext TestContext { get; set; } = null!;
}
