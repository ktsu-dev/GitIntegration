// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using ktsu.CredentialCache;
using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

/// <summary>
/// The shared base of every Git hosting provider: credential resolution, the transport seam, and
/// the pull-request-creation entry point common to every host. Concrete providers (GitHub, Azure
/// DevOps) supply the per-host implementations of repository and pull request retrieval.
/// </summary>
/// <remarks>
/// Not an extension point for assemblies outside this library. <see cref="CreatePullRequestCoreAsync"/>
/// is <see langword="internal"/> because its <see cref="GitPullRequestSpecification"/> parameter is
/// itself <see langword="internal"/> — a member cannot be more accessible than a type in its own
/// signature, and that parameter carries no reason to become public API purely to enable
/// subclassing that nothing has asked for. A type outside this assembly can therefore declare a
/// subclass, but never complete one: the abstract member it cannot see is also one it cannot
/// override, so no externally-defined subclass of <see cref="GitProvider"/> can ever be
/// instantiated. This library ships exactly two providers, GitHub and Azure DevOps, and both live
/// here.
/// </remarks>
public abstract class GitProvider : IGitHostingProvider
{
	/// <summary>
	/// The transport every provider built on <see cref="CreateHttpClient"/> shares when no
	/// <see cref="Handler"/> was injected.
	/// </summary>
	/// <remarks>
	/// One handler for the process, not one per call. A handler owns a connection pool, so building
	/// and disposing one per call tears down that pool each time and leaves its sockets in
	/// <c>TIME_WAIT</c> — a caller looping over a hundred repositories would build and destroy a
	/// hundred pools. See <see cref="CreateDefaultHandler"/> for why it is never disposed.
	/// </remarks>
	private static readonly SocketsHttpHandler SharedHandler = CreateDefaultHandler();

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
	/// <remarks>
	/// Reports whether a request this provider issues would actually carry a credential, which is a
	/// narrower question than whether the credential cache holds an entry. A resolved
	/// <see cref="CredentialWithNothing"/> is an entry that says "proceed unauthenticated", and a
	/// subtype <see cref="ResolveCredential"/> does not recognise is one no request can ever carry,
	/// so both report <see langword="false"/> here. Both share <see cref="ResolveRecognisedCredential"/>
	/// with <see cref="ResolveCredential"/> so the two can never drift apart.
	/// </remarks>
	public bool IsAuthenticated => ResolveRecognisedCredential(out _) is { Kind: not HostingCredentialKind.None };

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
	public IGitPullRequestCreateBuilder CreatePullRequest(GitRepositoryName repositoryName)
	{
		Ensure.NotNull(repositoryName);

		return new GitPullRequestCreateBuilder((specification, cancellationToken) =>
			CreatePullRequestCoreAsync(repositoryName, specification, cancellationToken));
	}

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
	internal HostingCredential ResolveCredential() =>
		ResolveRecognisedCredential(out Credential? credential) ?? throw new InvalidOperationException(
			$"Provider '{Name}' resolved a credential of type '{credential!.GetType()}', which this library does not recognise.");

	/// <summary>
	/// Resolves this provider's credential, reporting an unrecognised <see cref="Credential"/>
	/// subtype as <see langword="null"/> rather than by throwing.
	/// </summary>
	/// <remarks>
	/// The single place the credential table lives. <see cref="ResolveCredential"/> turns the
	/// <see langword="null"/> into its <see cref="InvalidOperationException"/>, and
	/// <see cref="IsAuthenticated"/> reads it as "no credential a request could carry" — a property
	/// getter must not throw, so it needs the non-throwing form, and having it repeat the subtype
	/// list instead would let the two answers drift apart as subtypes are added.
	/// </remarks>
	/// <param name="credential">
	/// When this method returns, contains the raw credential the cache held, which is
	/// non-<see langword="null"/> whenever this method returns <see langword="null"/>.
	/// </param>
	/// <returns>The resolved credential, or <see langword="null"/> for an unrecognised subtype.</returns>
	private HostingCredential? ResolveRecognisedCredential(out Credential? credential)
	{
		if (!TryGetCredential(out credential) || credential is null or CredentialWithNothing)
		{
			return HostingCredential.None;
		}

		return credential switch
		{
			CredentialWithToken token => HostingCredential.FromToken(token.Token.WeakString),
			CredentialWithUsernamePassword usernamePassword => HostingCredential.FromUsernamePassword(usernamePassword.Username.WeakString, usernamePassword.Password.WeakString),
			_ => null,
		};
	}

	/// <summary>
	/// Creates an <see cref="HttpClient"/> for this provider to issue requests through.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The client is per call and the transport underneath it is not. This client never owns its
	/// handler, whichever branch supplied it: an injected <see cref="Handler"/> belongs to whoever
	/// supplied it, and <see cref="SharedHandler"/> has to outlive every call this provider makes.
	/// One unconditional <see langword="false"/> covers both, so there is no branch here for a later
	/// change to get wrong.
	/// </para>
	/// <para>
	/// A per-call client is cheap precisely because it no longer brings a connection pool with it.
	/// Disposing a per-call client that owned its own handler is the other half of the socket
	/// exhaustion antipattern, not a fix for it, since every pool torn down leaves its sockets in
	/// <c>TIME_WAIT</c>.
	/// </para>
	/// <para>
	/// <see cref="GitHubProvider"/> reaches the same outcome by a different route, because Octokit's
	/// <c>HttpClientAdapter</c> exposes no equivalent flag and needs a non-owning wrapper to say the
	/// same thing. Both providers share one handler apiece and dispose neither.
	/// </para>
	/// </remarks>
	/// <returns>An <see cref="HttpClient"/> ready to issue requests, which the caller disposes.</returns>
	protected HttpClient CreateHttpClient() => new(Handler ?? SharedHandler, disposeHandler: false);

	/// <summary>
	/// Builds the local path a repository name maps to under
	/// <see cref="Environment.CurrentDirectory"/>, guaranteeing by construction that the result can
	/// never point outside it.
	/// </summary>
	/// <remarks>
	/// Owns the whole operation rather than deriving a safe leaf and leaving the combine step to each
	/// caller: a helper that only half-solves containment trusts every caller to get the other half
	/// right, and a future provider could call it and forget to combine, or combine against something
	/// other than <see cref="Environment.CurrentDirectory"/>. <see cref="Path.Combine(string, string)"/>
	/// silently discards every earlier argument when a later one is rooted, so combining
	/// <see cref="Environment.CurrentDirectory"/> with a repository name of <c>C:\Windows</c> or
	/// <c>/etc</c> directly would otherwise report a <see cref="GitRepository.LocalPath"/> that points
	/// there instead of under the current directory. <see cref="Path.GetFileName(string)"/> resolves
	/// that half of the problem by stripping every rooted prefix and directory component, but not the
	/// other half: <see cref="Path.GetFileName(string)"/> called on <c>../..</c> still returns
	/// <c>..</c>, which still escapes upward once combined. A repository name comes from a remote
	/// host's response, and <see cref="GitRepository.LocalPath"/> is a value a caller might hand
	/// straight to a clone or a file operation, so a name from which no safe leaf can be derived is
	/// treated as the host having reported something this library cannot represent as a directory —
	/// this throws rather than returning a value that looks safe but is not.
	/// </remarks>
	/// <param name="repositoryName">The repository name a host reported, which may be <see langword="null"/>.</param>
	/// <param name="providerName">
	/// The provider that reported <paramref name="repositoryName"/>, named in the exception thrown
	/// when no leaf can be derived.
	/// </param>
	/// <returns>
	/// The local path under <see cref="Environment.CurrentDirectory"/>, one directory named after the
	/// derived leaf segment of <paramref name="repositoryName"/>.
	/// </returns>
	/// <exception cref="GitHostingRequestException">
	/// <paramref name="repositoryName"/> is <see langword="null"/>, empty, consists only of
	/// whitespace, or is a name from which no directory segment can otherwise be derived.
	/// </exception>
	internal static AbsoluteDirectoryPath ToLocalRepositoryPath(string? repositoryName, GitProviderName providerName)
	{
		string leaf = Path.GetFileName(repositoryName ?? string.Empty);

		if (string.IsNullOrWhiteSpace(leaf) || leaf is "." or "..")
		{
			throw new GitHostingRequestException(
				$"{providerName} reported a repository name '{repositoryName}' that cannot be represented as a local directory.");
		}

		return Path.Combine(Environment.CurrentDirectory, leaf).As<AbsoluteDirectoryPath>();
	}

	/// <summary>
	/// Creates a transport carrying the settings every provider in this library wants of a shared,
	/// long-lived handler.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <see cref="SocketsHttpHandler.PooledConnectionLifetime"/> is the setting that makes a
	/// process-lifetime handler safe. A pooled connection is otherwise kept indefinitely, so the
	/// handler would go on using a host's original address long after DNS moved it. Two minutes
	/// matches the handler lifetime <c>IHttpClientFactory</c> applies for the same reason.
	/// </para>
	/// <para>
	/// <see cref="SocketsHttpHandler.AllowAutoRedirect"/> and
	/// <see cref="SocketsHttpHandler.AutomaticDecompression"/> reproduce what
	/// <c>Octokit.Internal.HttpMessageHandlerFactory.CreateDefault()</c> configures, so
	/// <see cref="GitHubProvider"/> loses nothing by sharing this shape instead of calling that
	/// factory per request. Following redirects is off because an API that answers a request with a
	/// redirect is reporting something the caller needs to see, not a detour to take silently.
	/// </para>
	/// <para>
	/// Never disposed, and deliberately so: the instances built from this are held in
	/// <see langword="static"/> fields for the life of the process, which is exactly the lifetime
	/// they are meant to have, and the operating system reclaims their sockets when it ends.
	/// </para>
	/// <para>
	/// <see langword="internal"/> rather than <see langword="private protected"/>: the shared
	/// instances built from this are private statics no test can reach, so this factory is the only
	/// place their settings can be asserted, and the test assembly is not a derived type.
	/// </para>
	/// </remarks>
	/// <returns>A handler ready to be shared across every call a provider makes.</returns>
	internal static SocketsHttpHandler CreateDefaultHandler() => new()
	{
		PooledConnectionLifetime = TimeSpan.FromMinutes(2),
		AllowAutoRedirect = false,
		AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
	};
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
