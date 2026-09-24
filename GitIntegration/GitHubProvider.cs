// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Strings;

using Octokit;
using Octokit.Internal;

/// <summary>
/// Provides integration with GitHub repositories using the Octokit library.
/// Handles authentication and repository management for GitHub remote sources.
/// </summary>
public sealed class GitHubProvider : GitProvider
{
	/// <summary>
	/// The transport every <see cref="GitHubProvider"/> call shares when no
	/// <see cref="GitProvider.Handler"/> was injected.
	/// </summary>
	/// <remarks>
	/// Its own instance rather than the one behind <see cref="GitProvider.CreateHttpClient"/>,
	/// because the two never talk to the same hosts and a connection pool is per host anyway. What
	/// matters is that it is one handler for the process rather than one per call, and
	/// <see cref="GitProvider.CreateDefaultHandler"/> is what keeps the two providers' settings from
	/// drifting apart. It replaces a per-call
	/// <c>Octokit.Internal.HttpMessageHandlerFactory.CreateDefault()</c>, whose settings that factory
	/// reproduces.
	/// </remarks>
	private static readonly SocketsHttpHandler SharedHandler = CreateDefaultHandler();

	/// <summary>
	/// Gets the name of this Git provider.
	/// </summary>
	public override GitProviderName Name => "GitHub".As<GitProviderName>();

	/// <inheritdoc/>
	private protected override HttpMessageHandler DefaultHandler => SharedHandler;

	/// <inheritdoc/>
	/// <remarks>
	/// <see langword="false"/>, because GitHub's repository-addressed routes are two routes rather
	/// than one. <c>GET /repos/{owner}/{repo}/pulls</c> takes a repository <b>name</b> in its
	/// <c>{repo}</c> slot; the id-addressed form is the separate <c>GET /repositories/{id}/pulls</c>.
	/// A numeric id in the name slot addresses no repository at all and answers <c>404</c> — which
	/// this provider surfaces as <see cref="GitHostingNotFoundException"/>, indistinguishable from a
	/// repository that really is gone. Azure DevOps, whose one <c>{repositoryId}</c> slot is
	/// documented as taking the id, answers <see langword="true"/> instead.
	/// <para>
	/// Preferring the name does not discard the id: a repository carrying only a
	/// <see cref="GitRepository.HostRepositoryId"/> still arrives here as one, and
	/// <see cref="GetPullRequestsCoreAsync"/> and <see cref="CreatePullRequestCoreAsync"/> answer it
	/// on the id-addressed route through Octokit's <c>long repositoryId</c> overloads.
	/// </para>
	/// </remarks>
	private protected override bool PrefersHostRepositoryId => false;

	/// <inheritdoc/>
	/// <remarks>
	/// Adds to the interface's remarks rather than restating them.
	/// <see cref="IGitHostingProvider.GetRepositoriesAsync"/> states the coverage every provider
	/// promises and points here for the routes this host reaches it by, so the two texts have one
	/// job each and neither is a copy of the other. An edit that moves this explanation must leave
	/// that pointer aimed somewhere real.
	/// <para>
	/// GitHub has no single endpoint that both honours <see cref="GitProvider.Owner"/> and reveals
	/// the private repositories a credential can see, so the route is chosen from what the owner is.
	/// <c>GET /orgs/{org}/repos</c> does both for an organization. <c>GET /user/repos</c> does both
	/// for the credential's own account, but only for that account — it always describes the token's
	/// own repositories regardless of which owner was configured, so it is reached only once the
	/// configured owner has been confirmed to be that account. <c>GET /users/{login}/repos</c>,
	/// which this method used to be alone in calling, honours any owner but is public-only, and
	/// remains the answer for a user who is not the credential's own: that is genuinely all GitHub
	/// offers there.
	/// </para>
	/// <para>
	/// Which one applies is established by <see cref="GetOwnerRepositoriesAsync"/>, whose remarks
	/// carry the per-branch reasoning and the cost each branch pays in requests.
	/// </para>
	/// </remarks>
	public override async Task<IReadOnlyList<GitRepository>> GetRepositoriesAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		(GitHubClient client, IDisposable createdTransport) = CreateClient();
		using IDisposable transport = createdTransport;

		try
		{
			IReadOnlyList<Repository> repositories = await GetOwnerRepositoriesAsync(client).ConfigureAwait(false);
			return [.. repositories.Select(ToGitRepository)];
		}
		catch (ApiException exception)
		{
			throw Translate(exception);
		}
	}

	/// <summary>
	/// Lists <see cref="GitProvider.Owner"/>'s repositories on whichever route reaches the widest
	/// set this provider's credential is entitled to.
	/// </summary>
	/// <remarks>
	/// <para>
	/// An unauthenticated provider skips both probes below and calls the public route directly. This
	/// is not a shortcut taken for speed: without a credential every route collapses to the same
	/// public answer — <c>GET /orgs/{org}/repos</c> and <c>GET /user/repos</c> reveal a private
	/// repository only to a credential entitled to it — so the probes would spend requests
	/// establishing which of three identical answers to ask for.
	/// </para>
	/// <para>
	/// Authenticated, the owner's type decides the route, and it is read from
	/// <c>GET /users/{login}</c> rather than inferred from <c>GET /orgs/{login}/repos</c> answering
	/// <c>404</c>. The inference is the cheaper probe and the wrong one: a token without
	/// <c>read:org</c>, or one not authorised for an organization that enforces SSO, is answered
	/// <c>404</c> by that route for an organization that plainly exists, and the inference would
	/// quietly demote it to the public-only user route — reinstating the exact under-reporting this
	/// method exists to remove, under a condition nothing would report. <c>GET /users/{login}</c> is
	/// a public endpoint whose <c>type</c> no credential's scope can change, and its <c>404</c> says
	/// the owner does not exist, which surfaces as <see cref="GitHostingNotFoundException"/> instead
	/// of an empty list.
	/// </para>
	/// <para>
	/// A user owner then costs a second probe, <c>GET /user</c>, because <c>GET /user/repos</c> is
	/// correct only when the configured owner <b>is</b> the credential's own account, and nothing
	/// short of asking establishes that. A credential that has no user to report — a GitHub App
	/// installation token, whose <c>GET /user</c> is answered <c>403</c> — is not a failure to
	/// surface: it means only that this branch cannot apply, and the public route is what such a
	/// credential could reach anyway, so the enumeration continues there rather than throwing where
	/// it previously succeeded. A <c>401</c> is left to propagate, because a credential GitHub
	/// rejects outright is a failure the caller has to see.
	/// </para>
	/// </remarks>
	/// <param name="client">The client this call's transport is wired to.</param>
	/// <returns>The repositories GitHub reported on the chosen route.</returns>
	private async Task<IReadOnlyList<Repository>> GetOwnerRepositoriesAsync(GitHubClient client)
	{
		string owner = Owner.WeakString;

		if (!IsAuthenticated)
		{
			return await client.Repository.GetAllForUser(owner).ConfigureAwait(false);
		}

		User account = await client.User.Get(owner).ConfigureAwait(false);

		if (IsOrganization(account))
		{
			return await client.Repository.GetAllForOrg(owner).ConfigureAwait(false);
		}

		string? authenticatedLogin;

		try
		{
			authenticatedLogin = (await client.User.Current().ConfigureAwait(false)).Login;
		}
		catch (ForbiddenException)
		{
			authenticatedLogin = null;
		}

		// Ordinal-ignore-case, because GitHub logins are unique case-insensitively and a caller who
		// configured "Octocat" for the account GitHub reports as "octocat" named the same account.
		return string.Equals(authenticatedLogin, owner, StringComparison.OrdinalIgnoreCase)
			// Affiliation rather than the default: GetAllForCurrent() unfiltered also returns
			// repositories the account merely collaborates on or reaches through an organization,
			// which are not Owner's repositories and would report a different owner's work under
			// this owner's name. Owner is the affiliation that makes this route mean what
			// GET /users/{login}/repos means, minus the public-only limit.
			? await client.Repository.GetAllForCurrent(new RepositoryRequest { Affiliation = RepositoryAffiliation.Owner }).ConfigureAwait(false)
			: await client.Repository.GetAllForUser(owner).ConfigureAwait(false);
	}

	/// <summary>
	/// Reports whether an account GitHub described is an organization.
	/// </summary>
	/// <remarks>
	/// Asks whether GitHub said "organization" rather than whether it said "user", so every other
	/// answer routes to the user branches: <see cref="Account.Type"/> is nullable and
	/// <see cref="AccountType"/> already carries <see cref="AccountType.Bot"/> and
	/// <see cref="AccountType.Mannequin"/> alongside the two this decision is really about. An
	/// omitted <c>type</c>, and an account kind GitHub adds later, both land on the route this
	/// method's caller took before any of these branches existed, which is the answer that cannot be
	/// wrong about coverage — only conservative about it.
	/// </remarks>
	/// <param name="account">The account <c>GET /users/{login}</c> reported.</param>
	/// <returns><see langword="true"/> when GitHub called the account an organization; otherwise, <see langword="false"/>.</returns>
	private static bool IsOrganization(User account) => account.Type == AccountType.Organization;

	/// <inheritdoc/>
	internal override async Task<IReadOnlyList<GitPullRequest>> GetPullRequestsCoreAsync(GitRepositoryAddress repositoryAddress, CancellationToken cancellationToken)
	{
		Ensure.NotNull(repositoryAddress.Value);
		cancellationToken.ThrowIfCancellationRequested();

		(GitHubClient client, IDisposable createdTransport) = CreateClient();
		using IDisposable transport = createdTransport;

		try
		{
			// State is requested explicitly rather than left to GitHub's default: IGitHostingProvider's
			// contract is "open pull requests only", and that contract must not quietly track whatever a
			// vendor happens to default to today. Built inside the try, after the transport already
			// exists, so a future validating change to PullRequestRequest can never leak it.
			PullRequestRequest request = new() { State = ItemStateFilter.Open };

			// Two routes, chosen by which form the address carries rather than by what the value looks
			// like: the name overload builds /repos/{owner}/{repo}/pulls, the long overload builds
			// /repositories/{id}/pulls. Sending either form down the other's route is what this
			// provider used to do, and GitHub answers it with a 404 rather than a diagnosable failure.
			IReadOnlyList<PullRequest> pullRequests = await (repositoryAddress.IsHostRepositoryId
				? client.PullRequest.GetAllForRepository(ToOctokitRepositoryId(repositoryAddress), request)
				: client.PullRequest.GetAllForRepository(Owner.WeakString, repositoryAddress.Value, request))
				.ConfigureAwait(false);

			return [.. pullRequests.Select(ToGitPullRequest)];
		}
		catch (ApiException exception)
		{
			throw Translate(exception);
		}
	}

	/// <inheritdoc/>
	internal override async Task<GitPullRequest> CreatePullRequestCoreAsync(GitRepositoryAddress repositoryAddress, GitPullRequestSpecification specification, CancellationToken cancellationToken)
	{
		Ensure.NotNull(repositoryAddress.Value);
		Ensure.NotNull(specification);
		cancellationToken.ThrowIfCancellationRequested();

		(GitHubClient client, IDisposable createdTransport) = CreateClient();
		using IDisposable transport = createdTransport;

		try
		{
			// Source maps to head and Target maps to base: NewPullRequest's positional constructor
			// takes (title, head, base), the same order GitHub's own API expects. Built inside the
			// try, after the transport already exists, so its own argument validation can never leak
			// the transport this method just acquired.
			NewPullRequest newPullRequest = new(specification.Title.WeakString, specification.Source.WeakString, specification.Target.WeakString)
			{
				Body = specification.Description,
				Draft = specification.IsDraft,
			};

			// The same two routes GetPullRequestsCoreAsync chooses between, for the same reason.
			PullRequest created = await (repositoryAddress.IsHostRepositoryId
				? client.PullRequest.Create(ToOctokitRepositoryId(repositoryAddress), newPullRequest)
				: client.PullRequest.Create(Owner.WeakString, repositoryAddress.Value, newPullRequest))
				.ConfigureAwait(false);

			return ToGitPullRequest(created);
		}
		catch (ApiException exception)
		{
			throw Translate(exception);
		}
	}

	/// <summary>
	/// Reads a <see cref="GitRepositoryAddress"/> holding GitHub's own repository id as the
	/// <see langword="long"/> Octokit's id-addressed overloads take.
	/// </summary>
	/// <remarks>
	/// <see cref="GitHostRepositoryId"/> is an unvalidated semantic string, because the two hosts
	/// disagree about what one looks like — Azure DevOps's is a uuid, GitHub's a whole number — so
	/// the type cannot enforce either and this is where GitHub's constraint is checked. A value that
	/// is not a whole number reaches here only from a hand-built <see cref="GitRepository"/>, since
	/// <see cref="ToGitRepository"/> fills the property from <c>Repository.Id</c>, which is a
	/// <see langword="long"/> already. That is a caller's argument rather than something a host
	/// reported, so it raises <see cref="ArgumentException"/> and not a
	/// <see cref="GitHostingException"/> — nothing was sent, and there is no response to describe.
	/// </remarks>
	/// <param name="repositoryAddress">The address, whose <see cref="GitRepositoryAddress.IsHostRepositoryId"/> is <see langword="true"/>.</param>
	/// <returns>The repository id.</returns>
	/// <exception cref="ArgumentException">The id is not a whole number, so GitHub cannot be addressed by it.</exception>
	private static long ToOctokitRepositoryId(GitRepositoryAddress repositoryAddress) =>
		long.TryParse(repositoryAddress.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long repositoryId)
			? repositoryId
			: throw new ArgumentException(
				$"GitHub repository ids are whole numbers, and '{repositoryAddress.Value}' is not one. " +
				"Address the repository by its Name instead.",
				nameof(repositoryAddress));

	/// <summary>
	/// Creates an Octokit client wired to this provider's transport and credential.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The adapter is per call and the handler underneath it is not.
	/// <see cref="HttpClientAdapter"/> disposes whatever <see cref="HttpMessageHandler"/> its factory
	/// produced, and neither handler this method can reach belongs to it: <see cref="SharedHandler"/>
	/// has to outlive every call, and an injected <see cref="GitProvider.Handler"/> belongs to
	/// whoever supplied it. <see cref="NonOwningHandler"/> stands between the adapter and both,
	/// absorbing that teardown without forwarding it, so the caller can still dispose the adapter
	/// after every request.
	/// </para>
	/// <para>
	/// The adapter must be disposed, and there is no "Octokit manages its own transport" free path
	/// to fall back on. <see cref="GitHubClient(ProductHeaderValue)"/> looks like it would give
	/// Octokit that responsibility, but it does not: internally it builds exactly the same
	/// <see cref="HttpClientAdapter"/>-over-<see cref="HttpClient"/> chain this method builds by
	/// hand, and nothing in <see cref="GitHubClient"/> or <see cref="Connection"/> ever disposes it,
	/// because neither type implements <see cref="IDisposable"/>. Every call this provider makes
	/// through a default-constructed client leaks one <see cref="HttpClient"/>.
	/// </para>
	/// <para>
	/// <see cref="GitProvider.ResolveCredential"/> runs before either transport is constructed: it
	/// can throw <see cref="InvalidOperationException"/> for a credential subtype this library does
	/// not recognize, and running it first means that throw can never leave a constructed
	/// <see cref="HttpClientAdapter"/> stranded with nothing left to dispose it — there is nothing to
	/// strand yet.
	/// </para>
	/// </remarks>
	/// <returns>
	/// A client ready to issue requests, with this provider's credential applied, paired with the
	/// transport the caller must dispose once done with the client.
	/// </returns>
	private (GitHubClient Client, IDisposable Transport) CreateClient()
	{
		Credentials credentials = ToOctokitCredentials(ResolveCredential());
		ProductHeaderValue product = new(AppDomain.CurrentDomain.FriendlyName);

		HttpMessageHandler transport = Handler ?? DefaultHandler;
		HttpClientAdapter adapter = new(() => new NonOwningHandler(transport));

		GitHubClient client = new(new Connection(product, adapter)) { Credentials = credentials };

		return (client, adapter);
	}

	/// <summary>
	/// A pass-through transport whose disposal stops at itself, so wrapping an
	/// <see cref="HttpMessageHandler"/> in it never disposes that handler.
	/// </summary>
	/// <remarks>
	/// Built on <see cref="HttpMessageInvoker"/> rather than <see cref="DelegatingHandler"/>:
	/// <see cref="DelegatingHandler"/>'s <c>Dispose(bool)</c> unconditionally disposes
	/// <c>InnerHandler</c>, with no way to opt out short of skipping its base call entirely — which
	/// CA2215 rightly refuses to allow. <see cref="HttpMessageInvoker"/>'s two-argument constructor
	/// takes a <c>disposeHandler</c> flag built for exactly this: forwarding requests to a handler
	/// this instance does not own.
	///
	/// Only this provider needs it. <see cref="GitProvider.CreateHttpClient"/> says the same thing
	/// with <see cref="HttpClient(HttpMessageHandler, bool)"/>'s own <c>disposeHandler</c> flag,
	/// which <see cref="HttpClientAdapter"/> has no equivalent of.
	/// </remarks>
	/// <param name="inner">The handler to forward every request to. Never disposed by this instance.</param>
	private sealed class NonOwningHandler(HttpMessageHandler inner) : HttpMessageHandler
	{
		private readonly HttpMessageInvoker _invoker = new(inner, disposeHandler: false);

		/// <inheritdoc/>
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
			_invoker.SendAsync(request, cancellationToken);

		/// <inheritdoc/>
		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				_invoker.Dispose();
			}

			base.Dispose(disposing);
		}
	}

	/// <summary>
	/// Converts this library's resolved credential into the shape Octokit's client accepts.
	/// </summary>
	/// <param name="credential">The credential this provider resolved.</param>
	/// <returns>The equivalent Octokit credential, or <see cref="Credentials.Anonymous"/> when none was resolved.</returns>
	private static Credentials ToOctokitCredentials(HostingCredential credential) => credential switch
	{
		{ Kind: HostingCredentialKind.Token, Token: string token } => new Credentials(token),
		// AuthenticationType.Bearer rather than the single-argument constructor above, which defaults
		// to Oauth and sends "Token <value>". GitHub requires "Bearer <value>" for a JWT such as a
		// GitHub App installation token, and rejects it under the Token scheme.
		{ Kind: HostingCredentialKind.BearerToken, Token: string bearerToken } => new Credentials(bearerToken, AuthenticationType.Bearer),
		{ Kind: HostingCredentialKind.UsernamePassword, Username: string username, Password: string password } => new Credentials(username, password),
		_ => Credentials.Anonymous,
	};

	/// <summary>
	/// Maps an Octokit repository onto this library's model.
	/// </summary>
	/// <remarks>
	/// <see cref="GitRepository.LocalPath"/> is left unset, because a repository this provider
	/// enumerates from the host has never been cloned and there is no local path to report. It used
	/// to be filled with the destination <c>git clone &lt;url&gt;</c> would pick given no destination
	/// of its own — a subdirectory of the process's current directory — which made the same remote
	/// repository yield a different record depending on when it was enumerated, and needed a
	/// containment guard to keep a name from a remote response from escaping that directory. Saying
	/// "not known" needs neither.
	/// <para>
	/// <see cref="GitRepository.HostRepositoryId"/> carries GitHub's own numeric repository id, so a
	/// caller can address the repository the way GitHub documents rather than by name.
	/// </para>
	/// </remarks>
	/// <param name="repository">The repository Octokit returned.</param>
	/// <returns>The equivalent <see cref="GitRepository"/>.</returns>
	private GitRepository ToGitRepository(Repository repository) => new()
	{
		Name = ToHostValue<GitRepositoryName>(repository.Name, Name, "repository name"),
		HostRepositoryId = ToHostValue<GitHostRepositoryId>(repository.Id.ToString(CultureInfo.InvariantCulture), Name, "repository id"),
		WebURI = ToHostValue<GitRepositoryWebURI>(repository.HtmlUrl, Name, "repository web URI"),
		RemotePath = ToHostValue<GitRepositoryRemotePath>(repository.CloneUrl, Name, "repository remote path"),
	};

	/// <summary>
	/// Maps an Octokit pull request onto this library's model.
	/// </summary>
	/// <remarks>
	/// Every field goes through <c>GitProvider.ToHostValue</c> or
	/// <c>GitProvider.ToRequiredHostValue</c>, never through <c>As&lt;T&gt;()</c>: this is a response
	/// GitHub sent, so a value this library cannot represent is a hosting failure and not a caller's
	/// argument failure. Octokit's model types are all mutable classes with unannotated
	/// <see langword="string"/> members deserialized straight from the response, so nothing about
	/// them guarantees a field GitHub omitted arrives as anything but <see langword="null"/> —
	/// <c>As&lt;T&gt;()</c> on one would throw <see cref="ArgumentException"/> out of a public hosting
	/// method, escaping past every <c>catch (GitHostingException)</c> a caller wrote.
	/// <see cref="PullRequest.Head"/> and <see cref="PullRequest.Base"/> are dereferenced
	/// conditionally for the same reason.
	/// </remarks>
	/// <param name="pullRequest">The pull request Octokit returned.</param>
	/// <returns>The equivalent <see cref="GitPullRequest"/>.</returns>
	private GitPullRequest ToGitPullRequest(PullRequest pullRequest) => new()
	{
		Number = ToRequiredHostValue<GitPullRequestNumber>(pullRequest.Number.ToString(CultureInfo.InvariantCulture), Name, "pull request number"),
		Title = ToRequiredHostValue<GitPullRequestTitle>(pullRequest.Title, Name, "pull request title"),
		Description = pullRequest.Body,
		SourceBranch = ToRequiredHostValue<GitBranchName>(pullRequest.Head?.Ref, Name, "pull request source branch"),
		TargetBranch = ToRequiredHostValue<GitBranchName>(pullRequest.Base?.Ref, Name, "pull request target branch"),
		Author = ToHostValue<GitPullRequestAuthor>(pullRequest.User?.Login, Name, "pull request author"),
		State = ToGitPullRequestState(pullRequest),
		IsDraft = pullRequest.Draft,
		WebURI = ToHostValue<GitPullRequestWebURI>(pullRequest.HtmlUrl, Name, "pull request web URI"),
		CreatedAt = pullRequest.CreatedAt,
	};

	/// <summary>
	/// Maps GitHub's <c>state</c>/<c>merged</c> pair onto <see cref="GitPullRequestState"/>.
	/// </summary>
	/// <remarks>
	/// GitHub reports three logical states through two fields: <c>merged</c> is checked first because
	/// a merged pull request also reports <c>state: closed</c>, and checking state before merged would
	/// misreport every merged pull request as merely closed.
	/// </remarks>
	/// <param name="pullRequest">The pull request Octokit returned.</param>
	/// <returns>The equivalent <see cref="GitPullRequestState"/>.</returns>
	private static GitPullRequestState ToGitPullRequestState(PullRequest pullRequest)
	{
		if (pullRequest.Merged)
		{
			return GitPullRequestState.Merged;
		}

		return pullRequest.State.Value switch
		{
			ItemState.Open => GitPullRequestState.Open,
			ItemState.Closed => GitPullRequestState.Closed,
			// ItemState has exactly these two members, mirroring GitHub's own state field, which is
			// documented to be only ever "open" or "closed" — this is unreachable in practice, but the
			// switch must still be exhaustive.
			_ => throw new NotSupportedException($"GitHub reported an unrecognized pull request state '{pullRequest.State.StringValue}'."),
		};
	}

	/// <summary>
	/// Maps an Octokit API failure onto this library's hosting exception hierarchy.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The arm order is load-bearing. <see cref="RateLimitExceededException"/>,
	/// <see cref="SecondaryRateLimitExceededException"/>, and <see cref="AbuseException"/> all derive
	/// from <see cref="ForbiddenException"/>, so the three specific arms must precede the general one
	/// or a rate-limit failure would be reported as an authentication failure instead. The
	/// <see cref="HttpStatusCode.TooManyRequests"/> arm matches on status rather than on type, so it
	/// sits below all three: a type test is the more specific claim, and a hypothetical 429 carried
	/// by one of those subtypes should still be answered by its own arm.
	/// </para>
	/// <para>
	/// Octokit has no dedicated type for a bare <c>429</c> — it surfaces as a plain
	/// <see cref="ApiException"/>, verified against the Octokit 14 assembly — so without that arm it
	/// would reach <see cref="GitHostingRequestException"/> while Azure DevOps maps the same status
	/// to <see cref="GitHostingRateLimitException"/>. That is the same cross-host divergence this
	/// method's <see cref="ForbiddenException"/> arm exists to remove, at the other rate-limit status.
	/// </para>
	/// <para>
	/// <see cref="ForbiddenException"/> maps to <see cref="GitHostingAuthenticationException"/>, and
	/// not to the default arm, because Octokit's <see cref="AuthorizationException"/> derives from
	/// <see cref="ApiException"/> rather than from <see cref="ForbiddenException"/>. Without its own
	/// arm a plain 403, which is how GitHub reports "resource not accessible by personal access
	/// token", would fall through to <see cref="GitHostingRequestException"/>. A caller writing
	/// host-agnostic <c>catch (GitHostingAuthenticationException)</c> would then handle that failure
	/// on Azure DevOps and miss it on GitHub, and the two providers exist to be interchangeable.
	/// </para>
	/// <para>
	/// Every arm keeps the Octokit exception as the inner exception. This hierarchy carries the
	/// provider, the status code, and the response body, which is enough to reproduce a failure by
	/// hand — but not everything the host supplied. <c>ApiError.Errors</c> is often the only place
	/// GitHub explains what was actually wrong with a request, and Octokit's synthetic 404 carries
	/// no <c>HttpResponse</c> at all, so without the inner exception that failure would reach a
	/// caller with an empty <see cref="GitHostingException.ResponseBody"/> and nothing else to go on.
	/// </para>
	/// </remarks>
	/// <param name="exception">The failure Octokit reported.</param>
	/// <returns>The equivalent <see cref="GitHostingException"/>, ready to throw.</returns>
	private GitHostingException Translate(ApiException exception)
	{
		// Octokit does not always populate HttpResponse — a GET that 404s is reported through a
		// synthetic exception with no response attached — so this falls back to the empty body
		// GitHostingException itself defaults to, rather than throwing while translating a throw.
		string responseBody = exception.HttpResponse?.Body as string ?? string.Empty;

		// A token that is valid but unauthorised for an organization's single sign-on arrives as a
		// plain 403, indistinguishable in status and body from a bad credential. The header is the
		// only thing carrying the URL that resolves it, and that URL is the whole remedy — without
		// it, the two failures a caller most needs to tell apart read identically.
		string? singleSignOnUrl = TryGetSingleSignOnUrl(exception);
		string authenticationMessage = singleSignOnUrl is null
			? exception.Message
			: $"{exception.Message} This organization requires single sign-on authorisation for " +
			  $"this credential. Authorise it at: {singleSignOnUrl}";

		return exception switch
		{
			RateLimitExceededException rateLimit => new GitHostingRateLimitException(exception.Message, Name, exception.StatusCode, responseBody, rateLimit.Reset, exception),
			SecondaryRateLimitExceededException => new GitHostingRateLimitException(exception.Message, Name, exception.StatusCode, responseBody, resetsAt: null, exception),
			AbuseException abuse => new GitHostingRateLimitException(exception.Message, Name, exception.StatusCode, responseBody, ToResetTime(abuse.RetryAfterSeconds), exception),
			{ StatusCode: HttpStatusCode.TooManyRequests } => new GitHostingRateLimitException(exception.Message, Name, exception.StatusCode, responseBody, ToResetTime(TryGetRetryAfterSeconds(exception)), exception),
			AuthorizationException => new GitHostingAuthenticationException(authenticationMessage, Name, exception.StatusCode, responseBody, exception),
			ForbiddenException => new GitHostingAuthenticationException(authenticationMessage, Name, exception.StatusCode, responseBody, exception),
			NotFoundException => new GitHostingNotFoundException(exception.Message, Name, exception.StatusCode, responseBody, exception),
			_ => new GitHostingRequestException(exception.Message, Name, exception.StatusCode, responseBody, exception),
		};
	}

	/// <summary>
	/// Converts an abuse-detection <c>Retry-After</c> delay into the absolute reset time
	/// <see cref="GitHostingRateLimitException.ResetsAt"/> reports.
	/// </summary>
	/// <remarks>
	/// <see cref="AbuseException.RetryAfterSeconds"/> is a relative delay, and
	/// <see cref="GitHostingRateLimitException.ResetsAt"/> is an absolute instant, because that is
	/// what <see cref="RateLimitExceededException.Reset"/> and Azure DevOps's
	/// <c>X-RateLimit-Reset</c> both report. Anchoring to <see cref="DateTimeOffset.UtcNow"/> loses
	/// however long the failed request itself took, which makes the result marginally early rather
	/// than late, and a caller that waits slightly too long is safe where one that waits too little
	/// is not.
	/// </remarks>
	/// <param name="retryAfterSeconds">The delay GitHub asked for, or <see langword="null"/> when it sent no <c>Retry-After</c>.</param>
	/// <returns>The absolute reset time, or <see langword="null"/> when no delay was reported.</returns>
	private static DateTimeOffset? ToResetTime(int? retryAfterSeconds) =>
		retryAfterSeconds is int seconds ? DateTimeOffset.UtcNow.AddSeconds(seconds) : null;

	/// <summary>
	/// Reads a failed response's <c>Retry-After</c> delay, in seconds.
	/// </summary>
	/// <remarks>
	/// Scanned case-insensitively rather than looked up by key. HTTP header names are
	/// case-insensitive, and Octokit's header dictionary compares them ordinally, so a keyed lookup
	/// would work only because <see cref="System.Net.Http.HttpResponseMessage"/> happens to
	/// canonicalise this particular header's casing on the way in. That is an assumption about a
	/// vendor's transport rather than a property of the header, and the collection is a handful of
	/// entries, so scanning costs nothing worth saving.
	///
	/// <c>Retry-After</c> may also carry an HTTP date rather than a delay in seconds. GitHub sends
	/// seconds, and a value that does not parse as seconds yields <see langword="null"/>, so an
	/// unrecognized form leaves <see cref="GitHostingRateLimitException.ResetsAt"/> unset rather than
	/// carrying an invented instant.
	/// </remarks>
	/// <param name="exception">The failure Octokit reported.</param>
	/// <returns>The delay in seconds, or <see langword="null"/> when absent or not a whole number of seconds.</returns>
	private static int? TryGetRetryAfterSeconds(ApiException exception)
	{
		if (exception.HttpResponse?.Headers is not IReadOnlyDictionary<string, string> headers)
		{
			return null;
		}

		foreach (KeyValuePair<string, string> header in headers.Where(
			candidate => candidate.Key.Equals("Retry-After", StringComparison.OrdinalIgnoreCase)))
		{
			if (int.TryParse(header.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds))
			{
				return seconds;
			}
		}

		return null;
	}

	/// <summary>
	/// Reads the authorisation URL from a failed response's single sign-on header, if it carries one.
	/// </summary>
	/// <remarks>
	/// Scanned case-insensitively rather than looked up by key, for the reason
	/// <see cref="TryGetRetryAfterSeconds"/> gives: HTTP header names are case-insensitive and
	/// Octokit's header dictionary compares them ordinally, so a keyed lookup would work only by
	/// matching whatever casing Octokit happens to canonicalise this header to.
	/// <para>
	/// The header's value is a parameter list, <c>required; url=&lt;uri&gt;</c>. Only the URL is read,
	/// and anything else in the list is left alone: the caller's whole use for this is a link to open.
	/// </para>
	/// </remarks>
	/// <param name="exception">The failed response.</param>
	/// <returns>The authorisation URL, or <see langword="null"/> when the header is absent or carries none.</returns>
	private static string? TryGetSingleSignOnUrl(ApiException exception)
	{
		const string headerName = "X-GitHub-SSO";
		const string urlParameter = "url=";

		if (exception.HttpResponse?.Headers is not IReadOnlyDictionary<string, string> headers)
		{
			return null;
		}

		foreach (KeyValuePair<string, string> header in headers.Where(
			candidate => candidate.Key.Equals(headerName, StringComparison.OrdinalIgnoreCase)))
		{
			foreach (string trimmed in header.Value.Split(';').Select(static parameter => parameter.Trim()))
			{
				if (trimmed.StartsWith(urlParameter, StringComparison.OrdinalIgnoreCase))
				{
					string url = trimmed[urlParameter.Length..];
					return url.Length == 0 ? null : url;
				}
			}
		}

		return null;
	}
}
