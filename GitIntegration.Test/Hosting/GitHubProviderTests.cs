// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;

using ktsu.CredentialCache;
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
	/// Wraps the captured pull request fixture in a one-element array after substituting one captured
	/// field, for the cases that need a field to arrive omitted or null.
	/// </summary>
	/// <remarks>
	/// Kept separate from <see cref="SinglePullRequestArray"/>, whose substitutions are about state
	/// and draft mapping rather than about a field being absent. Substituting rather than writing a
	/// second fixture keeps every other key exactly as captured, so a test that removes <c>title</c>
	/// is varying one thing.
	/// </remarks>
	/// <param name="original">The captured text to replace, including its trailing comma when removing a whole key.</param>
	/// <param name="replacement">The text to put in its place, or an empty string to omit the key entirely.</param>
	private static string SinglePullRequestArrayReplacing(string original, string replacement) =>
		$"[{Fixture("github-pullrequest-created.json").Replace(original, replacement, StringComparison.Ordinal)}]";

	/// <summary>
	/// Builds a single-repository response carrying only the fields <see cref="GitHubProvider"/>'s
	/// mapping reads, with a caller-supplied <c>name</c> — used to drive
	/// <see cref="GitRepository.LocalPath"/>'s containment behaviour without depending on the full
	/// captured fixture's real repository names.
	/// </summary>
	/// <param name="name">The value to send as the repository's <c>name</c> field.</param>
	private static string SingleRepositoryArray(string name) =>
		$$"""
		[{"id":90000001,"name":"{{name}}","html_url":"https://github.com/example-user/example-repo","clone_url":"https://github.com/example-user/example-repo.git"}]
		""";

	/// <summary>
	/// Builds the response <c>GET /users/{login}</c> and <c>GET /user</c> return, carrying only the
	/// two fields <see cref="GitHubProvider"/>'s route selection reads.
	/// </summary>
	/// <remarks>
	/// Written inline for the reason <see cref="SingleRepositoryArray"/> is: the routing decision
	/// reads <c>type</c> and <c>login</c> and nothing else, and a captured account payload would
	/// carry thirty fields whose presence no assertion depends on, inviting a later reader to wonder
	/// which of them mattered.
	/// </remarks>
	/// <param name="login">The value to send as the account's <c>login</c> field.</param>
	/// <param name="type">The value to send as the account's <c>type</c> field — GitHub sends <c>User</c> or <c>Organization</c>.</param>
	private static string AccountPayload(string login, string type) =>
		$$"""
		{"id":90000002,"login":"{{login}}","type":"{{type}}"}
		""";

	[TestMethod]
	public async Task AppliesATokenCredentialToTheRequestAsync()
	{
		PersonaGUID persona = CredentialCache.CreatePersonaGUID();
		CredentialCache.Instance.AddOrReplace(persona, new CredentialWithToken { Token = "ghp_abc123".As<CredentialToken>() });

		// Two responses because a credential is supplied: an authenticated enumeration establishes the
		// owner's type before choosing a route. The assertion is on the first request either way — the
		// credential has to reach every request this provider issues, probe included.
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, AccountPayload("contoso", "Organization"), ("Content-Type", "application/json"))
			.Respond(HttpStatusCode.OK, Fixture("github-repositories.json"), ("Content-Type", "application/json"));
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), PersonaGUID = persona, Handler = handler };

		_ = await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		// Proves ResolveCredential's result actually reaches the Octokit client, not merely that
		// ResolveCredential itself resolves correctly (GitProviderTests already covers that).
		Assert.AreEqual("Token ghp_abc123", handler.Requests[0].Headers["Authorization"]);
	}

	[TestMethod]
	public async Task SendsBearerAuthForABearerTokenCredentialAsync()
	{
		// GitHub distinguishes the two as Octokit AuthenticationType values, and sends a different
		// scheme for each: "Token" for a PAT, "Bearer" for a JWT such as a GitHub App token. Proves
		// the BearerToken kind reaches the Octokit client as Bearer rather than collapsing to Token.
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, AccountPayload("contoso", "Organization"), ("Content-Type", "application/json"))
			.Respond(HttpStatusCode.OK, Fixture("github-repositories.json"), ("Content-Type", "application/json"));
		GitHubProvider provider = new()
		{
			Owner = "contoso".As<GitProviderOwner>(),
			Handler = handler,
			CredentialSource = () => HostingCredential.FromBearerToken("eyJ0eXAiOiJKV1Qi"),
		};

		_ = await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual("Bearer eyJ0eXAiOiJKV1Qi", handler.Requests[0].Headers["Authorization"]);
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
		// GET /users/{login}/repos is the owner-honouring route that needs no credential, and
		// GET /user/repos would silently return the token's own repositories instead of the configured
		// owner's. Nothing else in the suite pins it for the unauthenticated case.
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
	public async Task IssuesNoOwnerTypeProbeWithoutACredentialAsync()
	{
		// The unauthenticated enumeration is one request, not three. Every route this provider can
		// choose between collapses to the same public answer without a credential, so probing which
		// one to ask for would spend two requests distinguishing identical answers. Asserted as an
		// exact count rather than on the one path, because a probe that ran and was ignored would
		// leave that path assertion passing.
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.RespondToPath("/users/contoso/repos", HttpStatusCode.OK, SingleRepositoryArray("public-repo"), ("Content-Type", "application/json"));
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		IReadOnlyList<GitRepository> repositories =
			await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(1, handler.Requests.Count);
		Assert.AreEqual("public-repo".As<GitRepositoryName>(), repositories[0].Name);
	}

	[TestMethod]
	public async Task EnumeratesAnOrganisationOnTheOrgRouteWhenAuthenticatedAsync()
	{
		// The case this whole route selection exists for. GET /users/{login}/repos is public-only even
		// with a token, so an organisation's private repositories were invisible to a credential that
		// could plainly see them — the divergence from AzureDevOpsProvider, which reports everything
		// its token reaches, under one interface that promises the same coverage of both.
		//
		// RespondToPath rather than an assertion on the recorded URI: a scripted success returned
		// regardless of route would let the wrong route parse and map a body GitHub would never have
		// sent it, and the test would pass on a provider that still asks the public endpoint.
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.RespondToPath("/users/contoso", HttpStatusCode.OK, AccountPayload("contoso", "Organization"), ("Content-Type", "application/json"))
			.RespondToPath("/orgs/contoso/repos", HttpStatusCode.OK, SingleRepositoryArray("private-repo"), ("Content-Type", "application/json"));
		GitHubProvider provider = new()
		{
			Owner = "contoso".As<GitProviderOwner>(),
			Handler = handler,
			CredentialSource = () => HostingCredential.FromToken("ghp_abc123"),
		};

		IReadOnlyList<GitRepository> repositories =
			await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(2, handler.Requests.Count);
		Assert.AreEqual("/users/contoso", handler.Requests[0].Uri.AbsolutePath);
		Assert.AreEqual("/orgs/contoso/repos", handler.Requests[1].Uri.AbsolutePath);

		// The repository is named for the route that produced it: the public route is scripted to
		// answer nothing here, so this name can only have arrived through /orgs/contoso/repos.
		Assert.AreEqual("private-repo".As<GitRepositoryName>(), repositories[0].Name);
	}

	[TestMethod]
	public async Task EnumeratesTheCredentialsOwnAccountOnTheCurrentUserRouteAsync()
	{
		// A user owner that is the credential's own account. GET /user/repos is the only route that
		// reveals that account's private repositories, and it is reachable only once GET /user has
		// confirmed the configured owner IS that account — the endpoint describes the token's own
		// repositories regardless of which owner was configured, so reaching it any earlier would
		// stop honouring Owner.
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			// Scripted at the owner's own casing, because that is the spelling this provider sends:
			// GitHub resolves a login case-insensitively, the recorded path is compared exactly, and
			// nothing here canonicalises Owner before putting it in a URI.
			.RespondToPath("/users/Octocat", HttpStatusCode.OK, AccountPayload("octocat", "User"), ("Content-Type", "application/json"))
			.RespondToPath("/user", HttpStatusCode.OK, AccountPayload("octocat", "User"), ("Content-Type", "application/json"))
			.RespondToPath("/user/repos", HttpStatusCode.OK, SingleRepositoryArray("private-repo"), ("Content-Type", "application/json"));
		GitHubProvider provider = new()
		{
			// Deliberately cased differently from the login GET /user reports. GitHub logins are
			// unique case-insensitively, so "Octocat" and "octocat" name one account, and an ordinal
			// comparison here would demote the owner's own repositories to the public-only route.
			Owner = "Octocat".As<GitProviderOwner>(),
			Handler = handler,
			CredentialSource = () => HostingCredential.FromToken("ghp_abc123"),
		};

		IReadOnlyList<GitRepository> repositories =
			await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(3, handler.Requests.Count);
		Assert.AreEqual("/user/repos", handler.Requests[2].Uri.AbsolutePath);

		// Owner affiliation, not the unfiltered default: GET /user/repos with no affiliation also
		// returns repositories the account merely collaborates on or reaches through an organisation,
		// which would report another owner's work under this owner's name.
		Assert.AreEqual("?affiliation=owner", handler.Requests[2].Uri.Query);

		Assert.AreEqual("private-repo".As<GitRepositoryName>(), repositories[0].Name);
	}

	[TestMethod]
	public async Task FallsBackToThePublicRouteForAUserOtherThanTheCredentialsOwnAsync()
	{
		// The one case where the public-only limit is real rather than a defect: GitHub publishes no
		// authenticated route that honours an arbitrary user owner. Worth pinning, because the
		// tempting shortcut — sending GET /user/repos anyway — would answer with the token holder's
		// own repositories under someone else's name.
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.RespondToPath("/users/someone-else", HttpStatusCode.OK, AccountPayload("someone-else", "User"), ("Content-Type", "application/json"))
			.RespondToPath("/user", HttpStatusCode.OK, AccountPayload("octocat", "User"), ("Content-Type", "application/json"))
			.RespondToPath("/users/someone-else/repos", HttpStatusCode.OK, SingleRepositoryArray("public-repo"), ("Content-Type", "application/json"));
		GitHubProvider provider = new()
		{
			Owner = "someone-else".As<GitProviderOwner>(),
			Handler = handler,
			CredentialSource = () => HostingCredential.FromToken("ghp_abc123"),
		};

		IReadOnlyList<GitRepository> repositories =
			await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(3, handler.Requests.Count);
		Assert.AreEqual("/users/someone-else/repos", handler.Requests[2].Uri.AbsolutePath);
		Assert.AreEqual("public-repo".As<GitRepositoryName>(), repositories[0].Name);
	}

	[TestMethod]
	public async Task FallsBackToThePublicRouteWhenTheCredentialHasNoUserIdentityAsync()
	{
		// A GitHub App installation token authenticates but belongs to no user, and GitHub answers its
		// GET /user with 403. That must not become a thrown GitHostingAuthenticationException: such a
		// credential enumerated a user owner's public repositories perfectly well before any of this
		// routing existed, and turning a working call into a failure is a worse regression than the
		// under-reporting the routing was added to fix. A 401 is deliberately not covered here —
		// a credential GitHub rejects outright still propagates, and
		// TranslatesAnUnauthorizedResponseToGitHostingAuthenticationExceptionAsync pins that.
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.RespondToPath("/users/octocat", HttpStatusCode.OK, AccountPayload("octocat", "User"), ("Content-Type", "application/json"))
			.RespondToPath("/user", HttpStatusCode.Forbidden, "{\"message\":\"Resource not accessible by integration\"}", ("Content-Type", "application/json"))
			.RespondToPath("/users/octocat/repos", HttpStatusCode.OK, SingleRepositoryArray("public-repo"), ("Content-Type", "application/json"));
		GitHubProvider provider = new()
		{
			Owner = "octocat".As<GitProviderOwner>(),
			Handler = handler,
			CredentialSource = () => HostingCredential.FromBearerToken("eyJ0eXAiOiJKV1Qi"),
		};

		IReadOnlyList<GitRepository> repositories =
			await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual("/users/octocat/repos", handler.Requests[2].Uri.AbsolutePath);
		Assert.AreEqual("public-repo".As<GitRepositoryName>(), repositories[0].Name);
	}

	[TestMethod]
	public async Task ReportsNoLocalPathForAnEnumeratedRepositoryAsync()
	{
		// An enumerated repository has never been cloned, so there is no local path to report. This
		// used to be filled with Path.Combine(Environment.CurrentDirectory, name), which made the same
		// remote repository yield a different record depending on when it was enumerated — and which
		// needed a containment guard purely to keep a name from a remote response from escaping that
		// directory. Saying "not known" needs neither.
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, SingleRepositoryArray("my-repo"), ("Content-Type", "application/json"));
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		IReadOnlyList<GitRepository> repositories =
			await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsNull(repositories[0].LocalPath);
		Assert.AreEqual("my-repo".As<GitRepositoryName>(), repositories[0].Name);
	}

	[TestMethod]
	public async Task CarriesGitHubsOwnRepositoryIdAsync()
	{
		// GitHub's id is a decimal number where Azure DevOps's is a uuid, which is why
		// GitHostRepositoryId is a string: the value is handed back to the host it came from and
		// never parsed, so a type insisting on either host's shape would exclude the other.
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.OK, SingleRepositoryArray("my-repo"), ("Content-Type", "application/json"));
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		IReadOnlyList<GitRepository> repositories =
			await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual("90000001".As<GitHostRepositoryId>(), repositories[0].HostRepositoryId);
	}

	[TestMethod]
	public async Task ThrowsAHostingFailureWhenAReportedNameIsNotOneThisLibraryCanRepresentAsync()
	{
		// GitRepositoryName is validated as a single path segment, so a host reporting "/" or ".."
		// produces a value the semantic type refuses. That has to surface as a GitHostingException,
		// not as the ArgumentException the semantic type itself raises: a public hosting method's
		// documented failure surface is this hierarchy, the same rule that makes an unparsable success
		// body a GitHostingRequestException rather than a JsonException.
		foreach (string reported in new[] { "/", "..", "   ", "owner/repo" })
		{
			using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
				.Respond(HttpStatusCode.OK, SingleRepositoryArray(reported), ("Content-Type", "application/json"));
			GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

			GitHostingRequestException exception = await Assert.ThrowsExactlyAsync<GitHostingRequestException>(
				async () => await provider.GetRepositoriesAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
				.ConfigureAwait(false);

			StringAssert.Contains(exception.Message, "cannot represent");
		}
	}

	[TestMethod]
	public async Task AddressesARepositoryByNameWhenListingPullRequestsAsync()
	{
		// GET /repos/{owner}/{repo}/pulls takes a repository NAME in its {repo} slot — the id-addressed
		// form is the separate /repositories/{id}/pulls. This provider used to send GitHub's numeric
		// HostRepositoryId here, which addresses no repository at all and answers 404 for one that was
		// just enumerated from the same provider. The repository below carries both forms, so this is
		// the case where the preference itself decides, and the name has to win.
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.RespondToPath("/repos/contoso/my-repo/pulls", HttpStatusCode.OK, "[]", ("Content-Type", "application/json"));
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		GitRepository repository = new()
		{
			Name = "my-repo".As<GitRepositoryName>(),
			HostRepositoryId = "90000001".As<GitHostRepositoryId>(),
		};

		_ = await provider.GetPullRequestsAsync(repository, TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual("/repos/contoso/my-repo/pulls", handler.Requests[0].Uri.AbsolutePath);
	}

	[TestMethod]
	public async Task AddressesARepositoryByItsHostIdOnTheIdRouteWhenNoNameIsKnownAsync()
	{
		// Preferring the name does not discard the id. A repository carrying only a HostRepositoryId —
		// one a caller built by hand, since GetRepositoriesAsync always reports a name — is addressed
		// on GitHub's id-addressed route rather than by dropping its id into the name slot.
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.RespondToPath("/repositories/90000001/pulls", HttpStatusCode.OK, "[]", ("Content-Type", "application/json"));
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		GitRepository repository = new() { HostRepositoryId = "90000001".As<GitHostRepositoryId>() };

		_ = await provider.GetPullRequestsAsync(repository, TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual("/repositories/90000001/pulls", handler.Requests[0].Uri.AbsolutePath);
	}

	[TestMethod]
	public async Task ReportsAHostRepositoryIdGitHubCannotBeAddressedByAsync()
	{
		// GitHostRepositoryId is unvalidated, because Azure DevOps's is a uuid and GitHub's a whole
		// number, so the semantic type can enforce neither. A hand-built repository carrying a
		// GitHub-impossible id is a caller's argument rather than anything a host reported — nothing is
		// sent, so there is no response for a GitHostingException to describe.
		using FakeHttpMessageHandler handler = new();
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		GitRepository repository = new() { HostRepositoryId = "not-a-number".As<GitHostRepositoryId>() };

		ArgumentException exception = await Assert.ThrowsExactlyAsync<ArgumentException>(
			async () => await provider.GetPullRequestsAsync(repository, TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		StringAssert.Contains(exception.Message, "whole numbers");
		Assert.AreEqual(0, handler.Requests.Count);
	}

	[TestMethod]
	public async Task AddressesARepositoryByNameWhenCreatingAPullRequestAsync()
	{
		// CreatePullRequest(GitRepository) reaches GitHub's routes the same way GetPullRequestsAsync
		// does, and carried the identical bug: Octokit's Create(owner, name, ...) builds
		// POST /repos/{owner}/{repo}/pulls, so a numeric id in that slot 404s just as it does on the
		// listing route.
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.RespondToPath("/repos/contoso/my-repo/pulls", HttpStatusCode.Created, Fixture("github-pullrequest-created.json"), ("Content-Type", "application/json"));
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		GitRepository repository = new()
		{
			Name = "my-repo".As<GitRepositoryName>(),
			HostRepositoryId = "90000001".As<GitHostRepositoryId>(),
		};

		_ = await provider.CreatePullRequest(repository)
			.From("example-branch-1".As<GitBranchName>())
			.Into("main".As<GitBranchName>())
			.Titled("A title".As<GitPullRequestTitle>())
			.ExecuteAsync(TestContext.CancellationTokenSource.Token)
			.ConfigureAwait(false);

		Assert.AreEqual("/repos/contoso/my-repo/pulls", handler.Requests[0].Uri.AbsolutePath);
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
	public async Task TranslatesAnOmittedRequiredPullRequestFieldToGitHostingRequestExceptionAsync()
	{
		// Octokit's model types are mutable classes with unannotated string members deserialized
		// straight from the response, so a field GitHub omits arrives as null however non-nullable the
		// property looks. The mapping used to call .As<T>() on those directly, which raises the
		// ArgumentException a semantic type owes a caller who passed a bad argument — out of a public
		// hosting method whose documented failure surface is the GitHostingException hierarchy, past
		// every catch (GitHostingException) a caller wrote. ThrowsExactly is what pins that down: it
		// fails on the ArgumentException the old mapping raised.
		(string original, string replacement, string field)[] cases =
		[
			("\"title\": \"Don't build all JIT flavors for clr.aot\",", string.Empty, "pull request title"),
			("\"title\": \"Don't build all JIT flavors for clr.aot\",", "\"title\": null,", "pull request title"),
			("\"ref\": \"example-branch-1\",", string.Empty, "pull request source branch"),
			("\"ref\": \"main\",", string.Empty, "pull request target branch"),
		];

		foreach ((string original, string replacement, string field) in cases)
		{
			using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
				.Respond(HttpStatusCode.OK, SinglePullRequestArrayReplacing(original, replacement), ("Content-Type", "application/json"));
			GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

			GitHostingRequestException exception = await Assert.ThrowsExactlyAsync<GitHostingRequestException>(
				async () => await provider.GetPullRequestsAsync("example-repo".As<GitRepositoryName>(), TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
				.ConfigureAwait(false);

			StringAssert.Contains(exception.Message, $"reported no {field}");
		}
	}

	[TestMethod]
	public async Task ReportsNoAuthorWhenGitHubOmitsTheUserAsync()
	{
		// The other side of the same rule: GitPullRequest.Author is optional, so a pull request whose
		// user carries no login yields null rather than an exception. Without this, routing Author
		// through ToHostValue could be "fixed" by making every field required and nothing would say
		// otherwise.
		using FakeHttpMessageHandler handler = new FakeHttpMessageHandler()
			.Respond(
				HttpStatusCode.OK,
				SinglePullRequestArrayReplacing("\"login\": \"example-user-1\",", string.Empty),
				("Content-Type", "application/json"));
		GitHubProvider provider = new() { Owner = "contoso".As<GitProviderOwner>(), Handler = handler };

		IReadOnlyList<GitPullRequest> pullRequests = await provider
			.GetPullRequestsAsync("example-repo".As<GitRepositoryName>(), TestContext.CancellationTokenSource.Token)
			.ConfigureAwait(false);

		Assert.IsNull(pullRequests[0].Author);
		Assert.AreEqual("Don't build all JIT flavors for clr.aot".As<GitPullRequestTitle>(), pullRequests[0].Title);
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
