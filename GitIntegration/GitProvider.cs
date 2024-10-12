namespace ktsu.GitIntegration;

using System.Collections.Concurrent;
using ktsu.CredentialCache;
using ktsu.StrongStrings;

public sealed record class GitProviderGUID : StrongStringAbstract<GitProviderGUID> { }
public sealed record class GitProviderName : StrongStringAbstract<GitProviderName> { }
public sealed record class GitProviderOwner : StrongStringAbstract<GitProviderOwner> { }

public abstract class GitProvider
{
	/// <summary>
	/// The name of the remote service provider
	/// </summary>
	public abstract GitProviderName Name { get; }

	/// <summary>
	/// The name of the entity that owns the remote repositories
	/// </summary>
	public GitProviderOwner Owner { get; init; } = new();

	public PersonaGUID PersonaGUID { get; init; } = CredentialCache.CreatePersonaGUID();

	/// <summary>
	/// Whether the user is authenticated with the remote service
	/// </summary>
	public bool IsAuthenticated => TryGetCredential(out _);

	public ConcurrentBag<GitRepository> Repositories { get; } = [];

	public abstract void RefreshRemoteRepositories();

	public bool TryGetCredential(out Credential? credential)
	{
		var credentialCache = CredentialCache.GetInstance();
		return credentialCache.TryGet(PersonaGUID, out credential);
	}
}
