// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;
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
	/// Gets the name of this Git provider.
	/// </summary>
	public override GitProviderName Name => "GitHub".As<GitProviderName>();

	/// <inheritdoc/>
	/// <remarks>
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
	/// </remarks>
	public override async Task<IReadOnlyList<GitRepository>> GetRepositoriesAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		(GitHubClient client, IDisposable transport) = CreateClient();

		try
		{
			IReadOnlyList<Repository> repositories = await client.Repository.GetAllForUser(Owner.WeakString).ConfigureAwait(false);
			return [.. repositories.Select(ToGitRepository)];
		}
		catch (ApiException exception)
		{
			throw Translate(exception);
		}
		finally
		{
			transport.Dispose();
		}
	}

	/// <inheritdoc/>
	public override async Task<IReadOnlyList<GitPullRequest>> GetPullRequestsAsync(GitRepositoryName repositoryName, CancellationToken cancellationToken = default)
	{
		Ensure.NotNull(repositoryName);
		cancellationToken.ThrowIfCancellationRequested();

		(GitHubClient client, IDisposable transport) = CreateClient();

		try
		{
			// State is requested explicitly rather than left to GitHub's default: IGitHostingProvider's
			// contract is "open pull requests only", and that contract must not quietly track whatever a
			// vendor happens to default to today. Built inside the try, after the transport already
			// exists, so a future validating change to PullRequestRequest can never leak it.
			PullRequestRequest request = new() { State = ItemStateFilter.Open };

			IReadOnlyList<PullRequest> pullRequests = await client.PullRequest
				.GetAllForRepository(Owner.WeakString, repositoryName.WeakString, request)
				.ConfigureAwait(false);

			return [.. pullRequests.Select(ToGitPullRequest)];
		}
		catch (ApiException exception)
		{
			throw Translate(exception);
		}
		finally
		{
			transport.Dispose();
		}
	}

	/// <inheritdoc/>
	internal override async Task<GitPullRequest> CreatePullRequestCoreAsync(GitRepositoryName repositoryName, GitPullRequestSpecification specification, CancellationToken cancellationToken)
	{
		Ensure.NotNull(repositoryName);
		Ensure.NotNull(specification);
		cancellationToken.ThrowIfCancellationRequested();

		(GitHubClient client, IDisposable transport) = CreateClient();

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
				.Create(Owner.WeakString, repositoryName.WeakString, newPullRequest)
				.ConfigureAwait(false);

			return ToGitPullRequest(created);
		}
		catch (ApiException exception)
		{
			throw Translate(exception);
		}
		finally
		{
			transport.Dispose();
		}
	}

	/// <summary>
	/// Creates an Octokit client wired to this provider's transport and credential.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Both branches go through <see cref="HttpClientAdapter"/> and always hand back a real,
	/// disposable transport — there is no "Octokit manages its own transport" free path.
	/// <see cref="GitHubClient(ProductHeaderValue)"/> looks like it would give Octokit that
	/// responsibility, but it does not: internally it builds exactly the same
	/// <see cref="HttpClientAdapter"/>-over-<see cref="HttpClient"/> chain this method would build by
	/// hand, and nothing in <see cref="GitHubClient"/> or <see cref="Connection"/> ever disposes it,
	/// because neither type implements <see cref="IDisposable"/>. Every call this provider makes
	/// through a default-constructed client was leaking one <see cref="HttpClient"/> — and its
	/// underlying socket handler — until this method started constructing that same default handler
	/// itself, via <see cref="Octokit.Internal.HttpMessageHandlerFactory.CreateDefault()"/>, so its
	/// caller can dispose it exactly like the injected-<see cref="GitProvider.Handler"/> case.
	/// </para>
	/// <para>
	/// When <see cref="GitProvider.Handler"/> is set — a test's fake, reached only because the test
	/// project has <c>InternalsVisibleTo</c> — <see cref="NonOwningHandler"/> stands between the
	/// adapter and that handler: <see cref="HttpClientAdapter"/>'s <c>Dispose</c> tears down whatever
	/// <see cref="HttpMessageHandler"/> its factory produced, and the injected handler belongs to
	/// whoever supplied it, not to this provider. <see cref="NonOwningHandler"/> absorbs that
	/// teardown without forwarding it.
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

		HttpClientAdapter adapter = Handler is null
			? new(HttpMessageHandlerFactory.CreateDefault)
			: new(() => new NonOwningHandler(Handler));

		GitHubClient client = new(new Connection(product, adapter)) { Credentials = credentials };

		return (client, adapter);
	}

	/// <summary>
	/// A pass-through transport whose disposal stops at itself, so wrapping a caller-owned
	/// <see cref="HttpMessageHandler"/> in it never disposes that handler.
	/// </summary>
	/// <remarks>
	/// Built on <see cref="HttpMessageInvoker"/> rather than <see cref="DelegatingHandler"/>:
	/// <see cref="DelegatingHandler"/>'s <c>Dispose(bool)</c> unconditionally disposes
	/// <c>InnerHandler</c>, with no way to opt out short of skipping its base call entirely — which
	/// CA2215 rightly refuses to allow. <see cref="HttpMessageInvoker"/>'s two-argument constructor
	/// takes a <c>disposeHandler</c> flag built for exactly this: forwarding requests to a handler
	/// this instance does not own.
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
	/// <see cref="GitRepository.LocalPath"/> is required, but a repository this provider enumerates
	/// from the host has never been cloned, so there is no real path to report. This uses the same
	/// destination <c>git clone &lt;url&gt;</c> would pick with no destination argument of its own — a
	/// subdirectory named after the repository, under the current directory — which matches
	/// <see cref="GitRepository.LocalPath"/>'s own documented "or is intended to be, cloned" meaning.
	/// </remarks>
	/// <param name="repository">The repository Octokit returned.</param>
	/// <returns>The equivalent <see cref="GitRepository"/>.</returns>
	private static GitRepository ToGitRepository(Repository repository) => new()
	{
		LocalPath = Path.Combine(Environment.CurrentDirectory, repository.Name).As<AbsoluteDirectoryPath>(),
		Name = repository.Name.As<GitRepositoryName>(),
		WebURI = repository.HtmlUrl.As<GitRepositoryWebURI>(),
		RemotePath = repository.CloneUrl.As<GitRepositoryRemotePath>(),
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
			AuthorizationException => new GitHostingAuthenticationException(exception.Message, Name, exception.StatusCode, responseBody),
			NotFoundException => new GitHostingNotFoundException(exception.Message, Name, exception.StatusCode, responseBody),
			RateLimitExceededException rateLimit => new GitHostingRateLimitException(exception.Message, Name, exception.StatusCode, responseBody, rateLimit.Reset),
			_ => new GitHostingRequestException(exception.Message, Name, exception.StatusCode, responseBody),
		};
	}
}
