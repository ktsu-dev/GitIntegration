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
	/// Adds to the interface's remarks rather than restating them.
	/// <see cref="IGitHostingProvider.GetRepositoriesAsync"/> says only that implementations differ
	/// in coverage and points here for this host's specifics, so the two texts have one job each and
	/// neither is a copy of the other. An edit that moves this explanation must leave that pointer
	/// aimed somewhere real.
	/// <para>
	/// Calls GitHub's <c>GET /users/{login}/repos</c>, which returns only <see cref="GitProvider.Owner"/>'s
	/// <b>public</b> repositories. Supplying a token does not widen this: that endpoint does not
	/// honour authentication to reveal private repositories the way <c>GET /user/repos</c> would for
	/// the token's own account, and switching to that endpoint would silently stop honouring
	/// <see cref="GitProvider.Owner"/> — it always describes the token's own repositories, regardless
	/// of which owner was configured, which would break callers who name someone else's owner on
	/// purpose. A caller that needs private repositories for a specific owner has no equivalent
	/// through this provider today; do not assume this method's coverage matches an
	/// <c>AzureDevOpsProvider</c> equivalent, whose token can see everything it has access to under
	/// the same interface.
	/// </para>
	/// </remarks>
	public override async Task<IReadOnlyList<GitRepository>> GetRepositoriesAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		(GitHubClient client, IDisposable createdTransport) = CreateClient();
		using IDisposable transport = createdTransport;

		try
		{
			IReadOnlyList<Repository> repositories = await client.Repository.GetAllForUser(Owner.WeakString).ConfigureAwait(false);
			return [.. repositories.Select(ToGitRepository)];
		}
		catch (ApiException exception)
		{
			throw Translate(exception);
		}
	}

	/// <inheritdoc/>
	internal override async Task<IReadOnlyList<GitPullRequest>> GetPullRequestsCoreAsync(string repositoryIdentifier, CancellationToken cancellationToken)
	{
		Ensure.NotNull(repositoryIdentifier);
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

			IReadOnlyList<PullRequest> pullRequests = await client.PullRequest
				.GetAllForRepository(Owner.WeakString, repositoryIdentifier, request)
				.ConfigureAwait(false);

			return [.. pullRequests.Select(ToGitPullRequest)];
		}
		catch (ApiException exception)
		{
			throw Translate(exception);
		}
	}

	/// <inheritdoc/>
	internal override async Task<GitPullRequest> CreatePullRequestCoreAsync(string repositoryIdentifier, GitPullRequestSpecification specification, CancellationToken cancellationToken)
	{
		Ensure.NotNull(repositoryIdentifier);
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

			PullRequest created = await client.PullRequest
				.Create(Owner.WeakString, repositoryIdentifier, newPullRequest)
				.ConfigureAwait(false);

			return ToGitPullRequest(created);
		}
		catch (ApiException exception)
		{
			throw Translate(exception);
		}
	}

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
	/// not recognise, and running it first means that throw can never leave a constructed
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
	/// <param name="pullRequest">The pull request Octokit returned.</param>
	/// <returns>The equivalent <see cref="GitPullRequest"/>.</returns>
	private static GitPullRequest ToGitPullRequest(PullRequest pullRequest) => new()
	{
		Number = pullRequest.Number.ToString(CultureInfo.InvariantCulture).As<GitPullRequestNumber>(),
		Title = pullRequest.Title.As<GitPullRequestTitle>(),
		Description = pullRequest.Body,
		SourceBranch = pullRequest.Head.Ref.As<GitBranchName>(),
		TargetBranch = pullRequest.Base.Ref.As<GitBranchName>(),
		Author = pullRequest.User?.Login?.As<GitPullRequestAuthor>(),
		State = ToGitPullRequestState(pullRequest),
		IsDraft = pullRequest.Draft,
		WebURI = pullRequest.HtmlUrl?.As<GitPullRequestWebURI>(),
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
			_ => throw new NotSupportedException($"GitHub reported an unrecognised pull request state '{pullRequest.State.StringValue}'."),
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

		return exception switch
		{
			RateLimitExceededException rateLimit => new GitHostingRateLimitException(exception.Message, Name, exception.StatusCode, responseBody, rateLimit.Reset, exception),
			SecondaryRateLimitExceededException => new GitHostingRateLimitException(exception.Message, Name, exception.StatusCode, responseBody, resetsAt: null, exception),
			AbuseException abuse => new GitHostingRateLimitException(exception.Message, Name, exception.StatusCode, responseBody, ToResetTime(abuse.RetryAfterSeconds), exception),
			{ StatusCode: HttpStatusCode.TooManyRequests } => new GitHostingRateLimitException(exception.Message, Name, exception.StatusCode, responseBody, ToResetTime(TryGetRetryAfterSeconds(exception)), exception),
			AuthorizationException => new GitHostingAuthenticationException(exception.Message, Name, exception.StatusCode, responseBody, exception),
			ForbiddenException => new GitHostingAuthenticationException(exception.Message, Name, exception.StatusCode, responseBody, exception),
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
	/// unrecognised form leaves <see cref="GitHostingRateLimitException.ResetsAt"/> unset rather than
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
}
