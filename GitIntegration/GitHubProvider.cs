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
	public override async Task<IReadOnlyList<GitRepository>> GetRepositoriesAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		(GitHubClient client, IDisposable? transport) = CreateClient();

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
			transport?.Dispose();
		}
	}

	/// <inheritdoc/>
	public override async Task<IReadOnlyList<GitPullRequest>> GetPullRequestsAsync(GitRepositoryName repositoryName, CancellationToken cancellationToken = default)
	{
		Ensure.NotNull(repositoryName);
		cancellationToken.ThrowIfCancellationRequested();

		(GitHubClient client, IDisposable? transport) = CreateClient();

		// State is requested explicitly rather than left to GitHub's default: IGitHostingProvider's
		// contract is "open pull requests only", and that contract must not quietly track whatever a
		// vendor happens to default to today.
		PullRequestRequest request = new() { State = ItemStateFilter.Open };

		try
		{
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
			transport?.Dispose();
		}
	}

	/// <inheritdoc/>
	internal override async Task<GitPullRequest> CreatePullRequestCoreAsync(GitRepositoryName repositoryName, GitPullRequestSpecification specification, CancellationToken cancellationToken)
	{
		Ensure.NotNull(repositoryName);
		Ensure.NotNull(specification);
		cancellationToken.ThrowIfCancellationRequested();

		(GitHubClient client, IDisposable? transport) = CreateClient();

		// Source maps to head and Target maps to base: NewPullRequest's positional constructor takes
		// (title, head, base), the same order GitHub's own API expects.
		NewPullRequest newPullRequest = new(specification.Title.WeakString, specification.Source.WeakString, specification.Target.WeakString)
		{
			Body = specification.Description,
			Draft = specification.IsDraft,
		};

		try
		{
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
			transport?.Dispose();
		}
	}

	/// <summary>
	/// Creates an Octokit client wired to this provider's transport and credential.
	/// </summary>
	/// <remarks>
	/// Octokit's <see cref="Connection"/> constructor that takes an
	/// <see cref="Octokit.Internal.IHttpClient"/> is how a test's <see cref="GitProvider.Handler"/>
	/// reaches Octokit: wrapping it in a <see cref="HttpClientAdapter"/> is the only supported way to
	/// hand Octokit a custom <see cref="HttpMessageHandler"/>. With no handler configured, the
	/// product-header-only constructor builds Octokit's own real transport instead, and the returned
	/// transport is <see langword="null"/> — that path is entirely Octokit's own responsibility to
	/// manage, and this provider never constructed anything of its own to dispose.
	/// </remarks>
	/// <returns>
	/// A client ready to issue requests, with this provider's credential applied, paired with the
	/// disposable transport the caller must dispose once done with the client — or
	/// <see langword="null"/> when nothing needs disposing.
	/// </returns>
	private (GitHubClient Client, IDisposable? Transport) CreateClient()
	{
		ProductHeaderValue product = new(AppDomain.CurrentDomain.FriendlyName);

		if (Handler is null)
		{
			return (new GitHubClient(product) { Credentials = ToOctokitCredentials(ResolveCredential()) }, null);
		}

		// NonOwningHandler stands between HttpClientAdapter and Handler: HttpClientAdapter's Dispose
		// tears down whatever HttpMessageHandler its factory produced, and Handler belongs to whoever
		// supplied it (almost always a test's fake, which the test itself still owns and disposes) —
		// not to this provider. NonOwningHandler absorbs that teardown without forwarding it.
		HttpClientAdapter adapter = new(() => new NonOwningHandler(Handler));
		GitHubClient client = new(new Connection(product, adapter))
		{
			Credentials = ToOctokitCredentials(ResolveCredential()),
		};

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
