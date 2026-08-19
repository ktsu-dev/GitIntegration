// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Concurrent;

using ktsu.CredentialCache;

/// <summary>
/// Represents a Git service provider that hosts repositories.
/// Provides authentication and repository management functionality.
/// </summary>
public abstract class GitProvider
{
	/// <summary>
	/// Gets the name of the remote Git service provider.
	/// </summary>
	/// <value>The provider name (e.g. GitHub, GitLab, etc.)</value>
	public abstract GitProviderName Name { get; }

	/// <summary>
	/// Gets the owner of the repositories in this provider.
	/// </summary>
	/// <remarks>
	/// Required rather than defaulted. A <c>new GitProviderOwner()</c> default bypasses the
	/// type's own validation and yields an empty value, which every caller would then have to
	/// re-check; making it required moves the failure to construction, where it belongs.
	/// </remarks>
	/// <value>The repository owner name.</value>
	public required GitProviderOwner Owner { get; init; }

	/// <summary>
	/// Gets or initializes the persona GUID used for authentication with the provider.
	/// </summary>
	/// <value>A GUID identifying the authentication persona.</value>
	public PersonaGUID PersonaGUID { get; init; } = CredentialCache.CreatePersonaGUID();

	/// <summary>
	/// Gets a value indicating whether the user is authenticated with the remote service.
	/// </summary>
	/// <value><c>true</c> if authenticated; otherwise, <c>false</c>.</value>
	public bool IsAuthenticated => TryGetCredential(out _);

	/// <summary>
	/// Gets the collection of repositories available from this provider.
	/// </summary>
	/// <value>A thread-safe collection of Git repositories.</value>
	public ConcurrentBag<GitRepository> Repositories { get; } = [];

	/// <summary>
	/// Refreshes the list of remote repositories from the Git provider.
	/// </summary>
	public abstract void RefreshRemoteRepositories();

	/// <summary>
	/// Attempts to retrieve the credential for this provider from the credential cache.
	/// </summary>
	/// <param name="credential">When this method returns, contains the credential if found; otherwise, null.</param>
	/// <returns><c>true</c> if the credential was found; otherwise, <c>false</c>.</returns>
	public bool TryGetCredential(out Credential? credential)
	{
		CredentialCache credentialCache = CredentialCache.Instance;
		return credentialCache.TryGet(PersonaGUID, out credential);
	}
}
