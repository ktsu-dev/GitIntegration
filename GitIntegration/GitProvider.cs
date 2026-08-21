// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using ktsu.CredentialCache;

/// <summary>
/// The shared base of every Git hosting provider: credential resolution, the transport seam, and
/// the pull-request-creation entry point common to every host. Concrete providers (GitHub, Azure
/// DevOps) supply the per-host implementations of repository and pull request retrieval.
/// </summary>
public abstract class GitProvider : IGitHostingProvider
{
	/// <inheritdoc/>
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

	/// <inheritdoc/>
	public bool IsAuthenticated => TryGetCredential(out _);

	/// <summary>
	/// Gets or initializes the transport this provider issues HTTP requests through, or
	/// <see langword="null"/> to construct a real one.
	/// </summary>
	/// <remarks>
	/// <see langword="internal"/> rather than public, so no transport type appears in this
	/// library's public API. The test project has <c>InternalsVisibleTo</c>, so tests inject a
	/// fake here without anything public changing shape — the same seam
	/// <see cref="IGitProcessRunner"/> provides for the local layer.
	/// </remarks>
	internal HttpMessageHandler? Handler { get; init; }

	/// <inheritdoc/>
	public abstract Task<IReadOnlyList<GitRepository>> GetRepositoriesAsync(CancellationToken cancellationToken = default);

	/// <inheritdoc/>
	public abstract Task<IReadOnlyList<GitPullRequest>> GetPullRequestsAsync(GitRepositoryName repositoryName, CancellationToken cancellationToken = default);

	/// <inheritdoc/>
	public IGitPullRequestCreateBuilder CreatePullRequest(GitRepositoryName repositoryName) =>
		// Replaced with construction of the concrete builder once it exists; until then there is
		// nothing this method can correctly return.
		throw new NotImplementedException("Pull request creation is not yet implemented.");

	/// <summary>
	/// Creates the pull request a finished <see cref="IGitPullRequestCreateBuilder"/> describes.
	/// </summary>
	/// <remarks>
	/// <see langword="internal"/> rather than <see langword="protected internal"/>: its
	/// <see cref="GitPullRequestSpecification"/> parameter is itself <see langword="internal"/>, and
	/// a member cannot be more broadly accessible than a type in its own signature. Every provider
	/// this library ships lives in this assembly, so nothing outside it needs to implement this
	/// member.
	/// </remarks>
	/// <param name="repositoryName">The repository the pull request is opened against.</param>
	/// <param name="specification">The pull request's finished configuration.</param>
	/// <param name="cancellationToken">A token to cancel the request.</param>
	/// <returns>The pull request as the host reports it after creation.</returns>
	internal abstract Task<GitPullRequest> CreatePullRequestCoreAsync(GitRepositoryName repositoryName, GitPullRequestSpecification specification, CancellationToken cancellationToken);

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

	/// <summary>
	/// Resolves this provider's credential into the shape a subclass applies to its transport,
	/// recognising every <see cref="Credential"/> subtype this library understands.
	/// </summary>
	/// <remarks>
	/// No credential at all, or a resolved <see cref="CredentialWithNothing"/>, both proceed
	/// <see cref="HostingCredentialKind.None"/> — enumerating public repositories without
	/// credentials is legitimate, and refusing it would break a real use. An unrecognised
	/// <see cref="Credential"/> subtype throws instead of doing the same: the caller configured
	/// something this library does not understand, and proceeding as though nothing were
	/// configured would hide that rather than surface it.
	///
	/// <see langword="internal"/> rather than <see langword="protected"/>: the returned
	/// <see cref="HostingCredential"/> is itself <see langword="internal"/>, and a member cannot be
	/// more broadly accessible than a type in its own signature.
	/// </remarks>
	/// <returns>The resolved credential.</returns>
	/// <exception cref="InvalidOperationException">
	/// A credential was resolved whose runtime type is not one this method recognises.
	/// </exception>
	internal HostingCredential ResolveCredential()
	{
		if (!TryGetCredential(out Credential? credential) || credential is null or CredentialWithNothing)
		{
			return HostingCredential.None;
		}

		return credential switch
		{
			CredentialWithToken token => HostingCredential.FromToken(token.Token.WeakString),
			CredentialWithUsernamePassword usernamePassword => HostingCredential.FromUsernamePassword(usernamePassword.Username.WeakString, usernamePassword.Password.WeakString),
			_ => throw new InvalidOperationException(
				$"Provider '{Name}' resolved a credential of type '{credential.GetType()}', which this library does not recognise."),
		};
	}

	/// <summary>
	/// Creates an <see cref="HttpClient"/> for this provider to issue requests through.
	/// </summary>
	/// <remarks>
	/// Returns a client over <see cref="Handler"/> when a test has set one, or a real client
	/// otherwise. The returned client owns its handler exactly when it constructed one — passing
	/// <see langword="true"/> for a caller-supplied handler would dispose a handler this provider
	/// does not own and a later request would reuse.
	/// </remarks>
	/// <returns>An <see cref="HttpClient"/> ready to issue requests.</returns>
	protected HttpClient CreateHttpClient() =>
		Handler is null ? new HttpClient() : new HttpClient(Handler, disposeHandler: false);
}

/// <summary>
/// The outcome of resolving a provider's credential: a bearer token, a username and password, or
/// nothing — proceeding unauthenticated.
/// </summary>
/// <remarks>
/// A record with a <see cref="HostingCredentialKind"/> discriminator rather than a type hierarchy:
/// a provider applying this result switches on <see cref="Kind"/> exactly once, so a closed set of
/// fields on one type reads as clearly as a hierarchy would and avoids a second type family
/// alongside <see cref="Credential"/> for what is, here, an internal implementation detail.
/// </remarks>
internal sealed record HostingCredential
{
	/// <summary>The singleton result for "no credential to apply".</summary>
	public static readonly HostingCredential None = new() { Kind = HostingCredentialKind.None };

	/// <summary>Gets which of the credential's fields are populated.</summary>
	public required HostingCredentialKind Kind { get; init; }

	/// <summary>Gets the bearer token, when <see cref="Kind"/> is <see cref="HostingCredentialKind.Token"/>.</summary>
	public string? Token { get; init; }

	/// <summary>Gets the username, when <see cref="Kind"/> is <see cref="HostingCredentialKind.UsernamePassword"/>.</summary>
	public string? Username { get; init; }

	/// <summary>Gets the password, when <see cref="Kind"/> is <see cref="HostingCredentialKind.UsernamePassword"/>.</summary>
	public string? Password { get; init; }

	/// <summary>Creates a result carrying a bearer token.</summary>
	/// <param name="token">The token.</param>
	/// <returns>The resolved credential.</returns>
	public static HostingCredential FromToken(string token) => new() { Kind = HostingCredentialKind.Token, Token = token };

	/// <summary>Creates a result carrying a username and password.</summary>
	/// <param name="username">The username.</param>
	/// <param name="password">The password.</param>
	/// <returns>The resolved credential.</returns>
	public static HostingCredential FromUsernamePassword(string username, string password) =>
		new() { Kind = HostingCredentialKind.UsernamePassword, Username = username, Password = password };
}

/// <summary>Which fields a <see cref="HostingCredential"/> carries.</summary>
internal enum HostingCredentialKind
{
	/// <summary>No credential — proceed unauthenticated.</summary>
	None,

	/// <summary>A bearer token, in <see cref="HostingCredential.Token"/>.</summary>
	Token,

	/// <summary>A username and password, in <see cref="HostingCredential.Username"/> and <see cref="HostingCredential.Password"/>.</summary>
	UsernamePassword,
}
