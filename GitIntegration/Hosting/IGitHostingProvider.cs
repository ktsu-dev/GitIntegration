// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using ktsu.CredentialCache;

/// <summary>
/// The contract a Git hosting provider implements: repository enumeration, pull request listing,
/// and pull request creation, over whichever transport and authentication scheme the host requires.
/// </summary>
/// <remarks>
/// The reads take no options, so a builder would be ceremony — they are plain async methods, the
/// same idiom <see cref="IGitClient"/> already uses for <c>GetVersionAsync</c> and
/// <c>DiscoverAsync</c>. Creating a pull request has two required inputs and three optional ones,
/// which is where a builder earns its keep, so <see cref="CreatePullRequest(GitRepositoryName)"/>
/// alone returns one.
/// </remarks>
public interface IGitHostingProvider
{
	/// <summary>
	/// Gets the name of the remote Git service provider.
	/// </summary>
	/// <value>The provider name (e.g. GitHub, GitLab, etc.)</value>
	public GitProviderName Name { get; }

	/// <summary>
	/// Gets the owner of the repositories in this provider.
	/// </summary>
	/// <value>The repository owner name.</value>
	public GitProviderOwner Owner { get; }

	/// <summary>
	/// Gets the persona GUID used for authentication with the provider.
	/// </summary>
	/// <value>A GUID identifying the authentication persona.</value>
	public PersonaGUID PersonaGUID { get; }

	/// <summary>
	/// Gets a value indicating whether the user is authenticated with the remote service.
	/// </summary>
	/// <value><c>true</c> if authenticated; otherwise, <c>false</c>.</value>
	public bool IsAuthenticated { get; }

	/// <summary>
	/// Retrieves the repositories this provider's owner has, from the remote service.
	/// </summary>
	/// <remarks>
	/// Implementations are not guaranteed to agree on exactly which of the owner's repositories this
	/// returns — each host's own API shapes that. GitHub's implementation, in particular, returns
	/// only the owner's <b>public</b> repositories, even when an authenticated credential is
	/// supplied; see <see cref="GitHubProvider.GetRepositoriesAsync"/> for why. A caller that needs a
	/// specific host's exact coverage should consult that provider's own remarks rather than assume
	/// parity across hosts.
	/// </remarks>
	/// <param name="cancellationToken">A token to cancel the request.</param>
	/// <returns>The repositories reported by the host.</returns>
	public Task<IReadOnlyList<GitRepository>> GetRepositoriesAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Retrieves the open pull requests for a repository.
	/// </summary>
	/// <remarks>
	/// Returns <b>open</b> pull requests only. Both hosts happen to default to open/active when
	/// unfiltered, but relying on that default would make this method's contract a restatement of
	/// two vendors' current behaviour, either of which could change without notice — so the filter
	/// is requested explicitly of each host rather than left implicit. Listing merged or abandoned
	/// pull requests is a filtering feature that can be added later without breaking this contract.
	/// </remarks>
	/// <param name="repositoryName">The repository to list pull requests for.</param>
	/// <param name="cancellationToken">A token to cancel the request.</param>
	/// <returns>The repository's open pull requests, as reported by the host.</returns>
	public Task<IReadOnlyList<GitPullRequest>> GetPullRequestsAsync(GitRepositoryName repositoryName, CancellationToken cancellationToken = default);

	/// <summary>
	/// Starts building a pull request for a repository.
	/// </summary>
	/// <param name="repositoryName">The repository the pull request is opened against.</param>
	/// <returns>A builder that collects the pull request's details before submitting it.</returns>
	public IGitPullRequestCreateBuilder CreatePullRequest(GitRepositoryName repositoryName);
}

/// <summary>
/// Collects the details of a pull request before submitting it to the host.
/// </summary>
/// <remarks>
/// <see cref="From(GitBranchName)"/>, <see cref="Into(GitBranchName)"/>, and
/// <see cref="Titled(GitPullRequestTitle)"/> are required; <see cref="Describing(string)"/> and
/// <see cref="AsDraft"/> are optional. A missing required value throws
/// <see cref="System.InvalidOperationException"/> from <see cref="ExecuteAsync(CancellationToken)"/>
/// rather than from the setter that would leave it unset, because a caller may legally supply the
/// parts in any order and only the finished configuration knows whether it is complete — the same
/// reasoning, and the same documentation requirement, as <c>IGitPullBuilder</c>. Like the local
/// layer's builders, an instance is single-use and not thread-safe.
/// </remarks>
public interface IGitPullRequestCreateBuilder
{
	/// <summary>Sets the branch the pull request merges from.</summary>
	/// <remarks>
	/// Required, but not checked here: a caller may legally supply <see cref="From(GitBranchName)"/>,
	/// <see cref="Into(GitBranchName)"/>, and <see cref="Titled(GitPullRequestTitle)"/> in any order,
	/// so only the finished configuration knows whether it is complete. Leaving the source branch
	/// unset makes <see cref="ExecuteAsync(CancellationToken)"/> throw
	/// <see cref="System.InvalidOperationException"/> instead.
	/// </remarks>
	/// <param name="source">The source branch.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="System.ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
	public IGitPullRequestCreateBuilder From(GitBranchName source);

	/// <summary>Sets the branch the pull request merges into.</summary>
	/// <remarks>
	/// Required, but not checked here, for the same reason as <see cref="From(GitBranchName)"/>:
	/// leaving the target branch unset makes <see cref="ExecuteAsync(CancellationToken)"/> throw
	/// <see cref="System.InvalidOperationException"/> instead.
	/// </remarks>
	/// <param name="target">The target branch.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="System.ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
	public IGitPullRequestCreateBuilder Into(GitBranchName target);

	/// <summary>Sets the pull request's title.</summary>
	/// <remarks>
	/// Required, but not checked here, for the same reason as <see cref="From(GitBranchName)"/>:
	/// leaving the title unset makes <see cref="ExecuteAsync(CancellationToken)"/> throw
	/// <see cref="System.InvalidOperationException"/> instead.
	/// </remarks>
	/// <param name="title">The title.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="System.ArgumentNullException"><paramref name="title"/> is <see langword="null"/>.</exception>
	public IGitPullRequestCreateBuilder Titled(GitPullRequestTitle title);

	/// <summary>Sets the pull request's description.</summary>
	/// <param name="description">The description.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="System.ArgumentNullException"><paramref name="description"/> is <see langword="null"/>.</exception>
	public IGitPullRequestCreateBuilder Describing(string description);

	/// <summary>Marks the pull request as a draft.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitPullRequestCreateBuilder AsDraft();

	/// <summary>
	/// Submits the pull request to the host.
	/// </summary>
	/// <param name="cancellationToken">A token to cancel the request.</param>
	/// <returns>The pull request as the host reports it after creation.</returns>
	/// <exception cref="System.InvalidOperationException">
	/// A required part — the source branch, the target branch, or the title — was never supplied.
	/// </exception>
	public Task<GitPullRequest> ExecuteAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The finished configuration collected by <see cref="IGitPullRequestCreateBuilder"/>, handed to a
/// provider's <c>CreatePullRequestCoreAsync</c> once every required part is known to be present.
/// </summary>
/// <remarks>
/// Declared alongside <see cref="IGitPullRequestCreateBuilder"/> rather than next to its
/// implementation: <c>GitProvider.CreatePullRequestCoreAsync</c> takes this type as a parameter, so
/// it must exist wherever that abstract member is declared.
/// </remarks>
/// <param name="Source">The branch the pull request merges from.</param>
/// <param name="Target">The branch the pull request merges into.</param>
/// <param name="Title">The pull request's title.</param>
/// <param name="Description">The pull request's description, or <see langword="null"/> when none was given.</param>
/// <param name="IsDraft">Whether the pull request should be created as a draft.</param>
internal sealed record GitPullRequestSpecification(GitBranchName Source, GitBranchName Target, GitPullRequestTitle Title, string? Description, bool IsDraft);
