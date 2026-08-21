// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Strings;

/// <summary>
/// Provides integration with GitHub repositories using the Octokit library.
/// Handles authentication and repository management for GitHub remote sources.
/// </summary>
public class GitHubProvider : GitProvider
{
	/// <summary>
	/// Gets the name of this Git provider.
	/// </summary>
	public override GitProviderName Name => "GitHub".As<GitProviderName>();

	/// <inheritdoc/>
	// The real implementation authenticates GitHubClient from ResolveCredential and enumerates
	// this provider's Owner's repositories; it is not written yet.
	public override Task<IReadOnlyList<GitRepository>> GetRepositoriesAsync(CancellationToken cancellationToken = default) =>
		throw new NotImplementedException("GitHub repository enumeration is not yet implemented.");

	/// <inheritdoc/>
	// The real implementation lists the repository's open pull requests through Octokit; it is
	// not written yet.
	public override Task<IReadOnlyList<GitPullRequest>> GetPullRequestsAsync(GitRepositoryName repositoryName, CancellationToken cancellationToken = default) =>
		throw new NotImplementedException("GitHub pull request listing is not yet implemented.");

	/// <inheritdoc/>
	// The real implementation submits the specification through Octokit and maps the result back
	// to GitPullRequest; it is not written yet.
	internal override Task<GitPullRequest> CreatePullRequestCoreAsync(GitRepositoryName repositoryName, GitPullRequestSpecification specification, CancellationToken cancellationToken) =>
		throw new NotImplementedException("GitHub pull request creation is not yet implemented.");
}
