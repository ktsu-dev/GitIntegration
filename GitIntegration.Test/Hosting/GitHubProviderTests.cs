// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;

using ktsu.CredentialCache;
using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

using Octokit;

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
	/// <c>GetAllForRepository</c> actually returns — varying <c>state</c>, <c>merged</c>,
	/// <c>merged_at</c>, and <c>draft</c> from one captured payload. GitHub's <c>state</c> field is
	/// only ever <c>open</c> or <c>closed</c>; <c>merged</c> is what tells the two closed outcomes
	/// apart. <c>merged_at</c> is varied alongside it because Octokit's own <c>PullRequest.Merged</c>
	/// is computed from whether <c>MergedAt</c> is non-null rather than read from the <c>merged</c>
	/// field directly — a real merged pull request always carries both, so this keeps the fixture
	/// internally consistent the way a real captured merged response would be. <c>draft</c> is
	/// varied for a different reason: the captured value is <c>false</c>, which is also what
	/// <see cref="GitPullRequest.IsDraft"/> holds when nothing assigns it, so asserting the captured
	/// value would pass even with the mapping deleted. The payload's shape is untouched — only these
	/// field values change.
	/// </summary>
	private static string SinglePullRequestArray(string state, bool merged, bool draft = false)
	{
		string json = Fixture("github-pullrequest-created.json")
			.Replace("\"state\": \"open\"", $"\"state\": \"{state}\"", StringComparison.Ordinal)
			.Replace("\"merged\": false", $"\"merged\": {(merged ? "true" : "false")}", StringComparison.Ordinal)
			.Replace("\"draft\": false", $"\"draft\": {(draft ? "true" : "false")}", StringComparison.Ordinal);

		if (merged)
		{
			json = json.Replace("\"merged_at\": null", "\"merged_at\": \"2026-08-21T00:50:00Z\"", StringComparison.Ordinal);
		}

		return $"[{json}]";
	}

	/// <summary>
	/// Builds a single-repository response carrying only the fields <see cref="GitHubProvider"/>'s
	/// mapping reads, with a caller-supplied <c>name</c> — used to drive
	/// <see cref="GitRepository.LocalPath"/>'s containment behaviour without depending on the full
	/// captured fixture's real repository names.
	/// </summary>
	/// <param name="name">The value to send as the repository's <c>name</c> field.</param>
	private static string SingleRepositoryArray(string name) =>
		$$"""
		[{"name":"{{name}}","html_url":"https://github.com/example-user/example-repo","clone_url":"https://github.com/example-user/example-repo.git"}]
		""";

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

		// The route this provider calls is a documented part of its contract, not an Octokit detail:
		// GET /users/{login}/repos is exactly why GetRepositoriesAsync returns public repositories
		// only, and GET /user/repos would silently return the token's own repositories instead of the
		// configured owner's. Nothing else in the suite pins it.
		Assert.AreEqual("/users/contoso/repos", handler.Requests[0].Uri.AbsolutePath);

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
	public async Task MapsAnOrdinaryRepositoryNameToALocalPathUnderTheCurrentDirectoryAsync()
	{
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, SingleRepositoryArray("my-repo"), ("Content-Type", "application/json"));
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		IReadOnlyList<GitRepository> repositories =
			await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(
			Path.Combine(Environment.CurrentDirectory, "my-repo").As<AbsoluteDirectoryPath>(),
			repositories[0].LocalPath);
	}

	[TestMethod]
	public async Task ThrowsWhenARootedRepositoryNameHasNoLeafSegmentAsync()
	{
		// "/" is rooted on every platform .NET runs this suite on (both Windows and POSIX treat "/"
		// as a separator), and is exactly the shape that let the old
		// Path.Combine(Environment.CurrentDirectory, repository.Name) call silently discard
		// Environment.CurrentDirectory and report LocalPath as the bare root. Path.GetFileName("/")
		// strips the root and leaves nothing behind, so this is treated as the host having sent a
		// name this library cannot represent as a directory, rather than as a value that happens to
		// look safe once stripped.
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, SingleRepositoryArray("/"), ("Content-Type", "application/json"));
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		GitHostingRequestException exception = await Assert.ThrowsExactlyAsync<GitHostingRequestException>(
			async () => await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		StringAssert.Contains(exception.Message, "cannot be represented as a local directory");
	}

	[TestMethod]
	public async Task ThrowsWhenARepositoryNameIsADirectoryTraversalTokenAsync()
	{
		// Path.GetFileName("..") returns ".." unchanged — stripping a rooted prefix does not resolve
		// this case, which is exactly why the mapping checks the derived leaf itself rather than
		// trusting Path.GetFileName alone.
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, SingleRepositoryArray(".."), ("Content-Type", "application/json"));
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		GitHostingRequestException exception = await Assert.ThrowsExactlyAsync<GitHostingRequestException>(
			async () => await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		StringAssert.Contains(exception.Message, "..");
	}

	[TestMethod]
	public async Task ThrowsWhenARepositoryNameIsWhitespaceOnlyAsync()
	{
		// Path.GetFileName("   ") returns "   " unchanged — there is no separator to strip, so the
		// leaf is exactly the whitespace-only input. GitRepositoryName itself rejects a
		// whitespace-only value ([HasNonWhitespaceContent]), so LocalPath is held to the same rule:
		// a name the semantic type would refuse is not a name this mapping can turn into a directory
		// either.
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, SingleRepositoryArray("   "), ("Content-Type", "application/json"));
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		GitHostingRequestException exception = await Assert.ThrowsExactlyAsync<GitHostingRequestException>(
			async () => await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		StringAssert.Contains(exception.Message, "cannot be represented as a local directory");
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
			.Respond(HttpStatusCode.OK, SinglePullRequestArray("open", merged: false, draft: true), ("Content-Type", "application/json"));
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
		// Asserted true, against a fixture varied to "draft": true: false is GitPullRequest.IsDraft's
		// own default, so asserting the captured false would still pass with the mapping deleted.
		Assert.IsTrue(pullRequest.IsDraft);
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
	public async Task TranslatesAPlainForbiddenResponseToGitHostingAuthenticationExceptionAsync()
	{
		// GitHub's most common auth failure: a token that authenticated fine but is not scoped for
		// this resource. Octokit reports it as ForbiddenException, which does NOT derive from
		// AuthorizationException, so without its own arm in Translate it would land on
		// GitHostingRequestException and a host-agnostic catch (GitHostingAuthenticationException)
		// would miss on GitHub what it catches on Azure DevOps.
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(
				(HttpStatusCode)403,
				"{\"message\":\"Resource not accessible by personal access token\"}",
				("Content-Type", "application/json"));
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		GitHostingAuthenticationException exception = await Assert.ThrowsExactlyAsync<GitHostingAuthenticationException>(
			async () => await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.AreEqual((HttpStatusCode)403, exception.StatusCode);
		StringAssert.Contains(exception.ResponseBody, "Resource not accessible by personal access token");
	}

	[TestMethod]
	public async Task TranslatesASecondaryRateLimitResponseToGitHostingRateLimitExceptionAsync()
	{
		// Octokit routes a 403 whose body names a secondary rate limit to
		// SecondaryRateLimitExceededException, which derives from ForbiddenException. It must
		// therefore be matched before the ForbiddenException arm, or it would be reported as an
		// authentication failure. GitHub sends no reset time with it, so ResetsAt is null.
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(
				(HttpStatusCode)403,
				"{\"message\":\"You have exceeded a secondary rate limit\"}",
				("Content-Type", "application/json"));
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		GitHostingRateLimitException exception = await Assert.ThrowsExactlyAsync<GitHostingRateLimitException>(
			async () => await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.AreEqual((HttpStatusCode)403, exception.StatusCode);
		StringAssert.Contains(exception.ResponseBody, "secondary rate limit");
		Assert.IsNull(exception.ResetsAt);
	}

	[TestMethod]
	public async Task TranslatesAnAbuseDetectionResponseToGitHostingRateLimitExceptionAsync()
	{
		// The third ForbiddenException subtype, and the only one carrying a relative delay rather
		// than an absolute instant. Bracketed against readings taken either side of the call because
		// the mapping anchors Retry-After to UtcNow, so no single exact value exists to assert.
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(
				(HttpStatusCode)403,
				"{\"message\":\"You have triggered an abuse detection mechanism\"}",
				("Content-Type", "application/json"),
				("Retry-After", "60"));
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		DateTimeOffset before = DateTimeOffset.UtcNow;

		GitHostingRateLimitException exception = await Assert.ThrowsExactlyAsync<GitHostingRateLimitException>(
			async () => await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		DateTimeOffset after = DateTimeOffset.UtcNow;

		Assert.AreEqual((HttpStatusCode)403, exception.StatusCode);
		StringAssert.Contains(exception.ResponseBody, "abuse detection mechanism");
		Assert.IsNotNull(exception.ResetsAt);
		Assert.IsInRange(before.AddSeconds(60), after.AddSeconds(60), exception.ResetsAt.Value);
	}

	[TestMethod]
	public async Task TranslatesATooManyRequestsResponseToGitHostingRateLimitExceptionAsync()
	{
		// Octokit has no dedicated type for a bare 429: it surfaces as a plain ApiException, so
		// without an arm matching on status this would reach GitHostingRequestException while
		// AzureDevOpsProvider maps the same status to GitHostingRateLimitException. Same cross-host
		// divergence as the plain 403, at the other rate-limit status.
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(
				HttpStatusCode.TooManyRequests,
				"{\"message\":\"You have exceeded a rate limit\"}",
				("Content-Type", "application/json"),
				("Retry-After", "90"));
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		DateTimeOffset before = DateTimeOffset.UtcNow;

		GitHostingRateLimitException exception = await Assert.ThrowsExactlyAsync<GitHostingRateLimitException>(
			async () => await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		DateTimeOffset after = DateTimeOffset.UtcNow;

		Assert.AreEqual(HttpStatusCode.TooManyRequests, exception.StatusCode);
		StringAssert.Contains(exception.ResponseBody, "exceeded a rate limit");

		// Retry-After is read from the response and converted to an absolute instant, the same way
		// AbuseException's own relative delay is. Bracketed rather than exact, because the mapping
		// anchors to UtcNow.
		Assert.IsNotNull(exception.ResetsAt);
		Assert.IsInRange(before.AddSeconds(90), after.AddSeconds(90), exception.ResetsAt.Value);
	}

	[TestMethod]
	public async Task LeavesResetsAtNullWhenATooManyRequestsResponseCarriesNoRetryAfterAsync()
	{
		// The companion to the test above. A 429 without a Retry-After leaves ResetsAt unset rather
		// than carrying an instant this library invented, since null already means "the host did not
		// report one" and a guessed reset time is worse than no reset time.
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(
				HttpStatusCode.TooManyRequests,
				"{\"message\":\"You have exceeded a rate limit\"}",
				("Content-Type", "application/json"));
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		GitHostingRateLimitException exception = await Assert.ThrowsExactlyAsync<GitHostingRateLimitException>(
			async () => await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.AreEqual(HttpStatusCode.TooManyRequests, exception.StatusCode);
		Assert.IsNull(exception.ResetsAt);
	}

	[TestMethod]
	public async Task LeavesResetsAtNullWhenRetryAfterCarriesAnHttpDateAsync()
	{
		// Retry-After may carry an HTTP date rather than a delay in seconds. GitHub sends seconds, so
		// TryGetRetryAfterSeconds parses only that form and a date yields null — correct by
		// inspection, and now pinned. An invented instant derived from a form this library does not
		// actually parse would be worse than no instant at all.
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(
				HttpStatusCode.TooManyRequests,
				"{\"message\":\"You have exceeded a rate limit\"}",
				("Content-Type", "application/json"),
				("Retry-After", "Wed, 21 Oct 2026 07:28:00 GMT"));
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		GitHostingRateLimitException exception = await Assert.ThrowsExactlyAsync<GitHostingRateLimitException>(
			async () => await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.AreEqual(HttpStatusCode.TooManyRequests, exception.StatusCode);
		Assert.IsNull(exception.ResetsAt);
	}

	[TestMethod]
	public async Task KeepsTheOctokitFailureAsTheInnerExceptionAsync()
	{
		// The hierarchy carries the provider, status, and body, which is enough to reproduce a
		// failure by hand — but not everything the host supplied. ApiError.Errors is often the only
		// place GitHub says what was actually wrong with a request, and it has no field here, so
		// discarding the Octokit exception discards it too.
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(
				HttpStatusCode.Forbidden,
				"{\"message\":\"Resource not accessible by personal access token\"}",
				("Content-Type", "application/json"));
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		GitHostingAuthenticationException exception = await Assert.ThrowsExactlyAsync<GitHostingAuthenticationException>(
			async () => await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.IsInstanceOfType<ApiException>(exception.InnerException);
	}

	[TestMethod]
	public async Task KeepsTheInnerExceptionEvenWhenOctokitAttachedNoResponseAsync()
	{
		// The worst case the inner exception exists for. Octokit reports a 404 on a GET through a
		// synthetic exception with no HttpResponse, so ResponseBody is empty and there is no other
		// context to fall back on — without the inner exception this failure reaches a caller
		// carrying only a message.
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.NotFound, "{\"message\":\"Not Found\"}", ("Content-Type", "application/json"));
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		GitHostingNotFoundException exception = await Assert.ThrowsExactlyAsync<GitHostingNotFoundException>(
			async () => await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.AreEqual(string.Empty, exception.ResponseBody);
		Assert.IsInstanceOfType<ApiException>(exception.InnerException);
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

	[TestMethod]
	public async Task LeavesAnInjectedHandlerUndisposedAndUsableForASecondCallAsync()
	{
		// The transport seam's load-bearing property. Each call builds its own HttpClientAdapter and
		// disposes it, and HttpClientAdapter tears down whatever handler its factory produced, so
		// without NonOwningHandler standing in between, the first call would dispose the injected
		// handler and the second would fail. It equally protects the shared default handler, which
		// a test cannot observe directly.
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, Fixture("github-repositories.json"), ("Content-Type", "application/json"))
			.Respond(HttpStatusCode.OK, Fixture("github-repositories.json"), ("Content-Type", "application/json"));
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		IReadOnlyList<GitRepository> first =
			await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);
		Assert.IsFalse(handler.WasDisposed);

		IReadOnlyList<GitRepository> second =
			await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);
		Assert.IsFalse(handler.WasDisposed);

		// Both calls really reached the handler and really parsed, so this cannot pass on a second
		// call that silently returned nothing.
		Assert.AreEqual(2, handler.Requests.Count);
		Assert.AreEqual("example-repo-1".As<GitRepositoryName>(), first[0].Name);
		Assert.AreEqual("example-repo-1".As<GitRepositoryName>(), second[0].Name);
	}

	public TestContext TestContext { get; set; } = null!;
}
