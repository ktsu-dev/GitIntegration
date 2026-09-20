// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using ktsu.CredentialCache;
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

	/// <summary>
	/// Gets or initializes a callback supplying this provider's credential directly, bypassing the
	/// credential cache, or <see langword="null"/> to resolve through <see cref="PersonaGUID"/> as
	/// usual.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The seam for a credential the credential cache cannot hold. <see cref="PersonaGUID"/> reads
	/// the host's native keyring, which suits a long-lived secret such as a personal access token,
	/// and suits nothing about a short-lived one: an Entra ID access token is minted per session by
	/// an identity library, expires within the hour, and writing it to a keyring would persist a
	/// secret that is stale before it is read again.
	/// </para>
	/// <para>
	/// A callback rather than a stored <see cref="HostingCredential"/>, and consulted on every
	/// resolution rather than cached, precisely so an expiring token can be refreshed: a caller
	/// returns whatever its identity library hands it at the moment of the call. The cost is that
	/// the callback runs inline on the request path, so it should return a cached-and-still-valid
	/// token rather than block on a fresh network round trip each time — which is what
	/// <c>TokenCredential.GetToken</c> and its equivalents already do.
	/// </para>
	/// <para>
	/// Takes precedence over the credential cache when set, so a caller supplying one never has to
	/// also clear whatever the keyring happens to hold for its persona. Returning
	/// <see cref="HostingCredential.None"/> is how it says "proceed unauthenticated"; returning
	/// <see langword="null"/> is a caller bug and makes <see cref="ResolveCredential"/> throw.
	/// </para>
	/// </remarks>
	/// <value>The callback, or <see langword="null"/> to use the credential cache.</value>
	public Func<HostingCredential>? CredentialSource { get; init; }

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
	public Task<IReadOnlyList<GitPullRequest>> GetPullRequestsAsync(GitRepositoryName repositoryName, CancellationToken cancellationToken = default) =>
		GetPullRequestsCoreAsync(GitRepositoryAddress.ByName(Ensure.NotNull(repositoryName).WeakString), cancellationToken);

	/// <inheritdoc/>
	public Task<IReadOnlyList<GitPullRequest>> GetPullRequestsAsync(GitRepository repository, CancellationToken cancellationToken = default) =>
		GetPullRequestsCoreAsync(ToRepositoryAddress(repository), cancellationToken);

	/// <inheritdoc/>
	public IGitPullRequestCreateBuilder CreatePullRequest(GitRepositoryName repositoryName)
	{
		GitRepositoryAddress address = GitRepositoryAddress.ByName(Ensure.NotNull(repositoryName).WeakString);

		return new GitPullRequestCreateBuilder((specification, cancellationToken) =>
			CreatePullRequestCoreAsync(address, specification, cancellationToken));
	}

	/// <inheritdoc/>
	public IGitPullRequestCreateBuilder CreatePullRequest(GitRepository repository)
	{
		GitRepositoryAddress address = ToRepositoryAddress(repository);

		return new GitPullRequestCreateBuilder((specification, cancellationToken) =>
			CreatePullRequestCoreAsync(address, specification, cancellationToken));
	}

	/// <summary>
	/// Gets a value indicating whether this host's repository-addressed routes want
	/// <see cref="GitRepository.HostRepositoryId"/> in preference to <see cref="GitRepository.Name"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Per-provider rather than shared, because the two hosts genuinely disagree and a single answer
	/// was wrong for one of them. Azure DevOps types its <c>{repositoryId}</c> path parameter as
	/// <c>string (uuid)</c> and draws an explicit id-or-name distinction for the sibling
	/// <c>project</c> parameter while withholding it here, so an id is what its documented schema
	/// asks for. GitHub's <c>{repo}</c> slot in <c>/repos/{owner}/{repo}</c> takes a repository
	/// <b>name</b> only — its id-addressed route is the separate <c>/repositories/{id}</c>, so
	/// putting GitHub's numeric id in the name slot addresses nothing and answers <c>404</c> for a
	/// repository that plainly exists.
	/// </para>
	/// <para>
	/// A preference rather than a requirement: this only decides which candidate wins when a
	/// <see cref="GitRepository"/> carries both. Which of the two a provider was handed reaches it on
	/// <see cref="GitRepositoryAddress.IsHostRepositoryId"/>, so a provider whose two routes differ
	/// can still address either form correctly rather than guessing from the value's shape.
	/// </para>
	/// <para>
	/// <see langword="private protected"/> for the reason <see cref="DefaultHandler"/> gives: every
	/// provider this library ships lives in this assembly, and no externally-defined subclass can be
	/// instantiated anyway.
	/// </para>
	/// </remarks>
	/// <value><see langword="true"/> to prefer the host's own id; <see langword="false"/> to prefer the name.</value>
	private protected abstract bool PrefersHostRepositoryId { get; }

	/// <summary>
	/// Chooses how a repository is addressed in a request path, in this host's preferred order.
	/// </summary>
	/// <remarks>
	/// Falls back to whichever form was not preferred rather than requiring the preferred one,
	/// because a caller may legitimately have constructed a <see cref="GitRepository"/> by hand
	/// carrying only one of the two. The result says which form it carries, so a provider whose id
	/// and name routes differ answers the fallback with the right route instead of sending one form
	/// down the other's route — which is exactly the failure that made this per-provider.
	/// </remarks>
	/// <param name="repository">The repository to address.</param>
	/// <returns>The value identifying the repository, and which of the two forms it is.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="repository"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="repository"/> carries neither a <see cref="GitRepository.HostRepositoryId"/>
	/// nor a <see cref="GitRepository.Name"/>, so there is nothing to address it by.
	/// </exception>
	private GitRepositoryAddress ToRepositoryAddress(GitRepository repository)
	{
		Ensure.NotNull(repository);

		GitRepositoryAddress? byHostId = repository.HostRepositoryId?.WeakString is string hostId
			? GitRepositoryAddress.ByHostRepositoryId(hostId)
			: null;

		GitRepositoryAddress? byName = repository.Name?.WeakString is string name
			? GitRepositoryAddress.ByName(name)
			: null;

		GitRepositoryAddress? chosen = PrefersHostRepositoryId
			? byHostId ?? byName
			: byName ?? byHostId;

		return chosen ?? throw new ArgumentException(
			"The repository carries neither a HostRepositoryId nor a Name, so there is no way to " +
			"address it on the host.",
			nameof(repository));
	}

	/// <summary>
	/// Retrieves the open pull requests for the repository an address identifies.
	/// </summary>
	/// <remarks>
	/// The single implementation behind both public overloads, so the two cannot answer the same
	/// question differently. Takes the resolved <see cref="GitRepositoryAddress"/> rather than a
	/// <see cref="GitRepositoryName"/> or a <see cref="GitRepository"/>, because choosing between an
	/// id and a name is a decision that belongs in one place — <see cref="ToRepositoryAddress"/> —
	/// rather than repeated in each provider. The address carries which form was chosen, so a
	/// provider that addresses ids and names through different routes picks the right one.
	/// </remarks>
	/// <param name="repositoryAddress">The address identifying the repository.</param>
	/// <param name="cancellationToken">A token to cancel the request.</param>
	/// <returns>The repository's open pull requests, as reported by the host.</returns>
	internal abstract Task<IReadOnlyList<GitPullRequest>> GetPullRequestsCoreAsync(GitRepositoryAddress repositoryAddress, CancellationToken cancellationToken);

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
	/// <param name="repositoryAddress">
	/// The address identifying the repository the pull request is opened against, already chosen by
	/// <see cref="ToRepositoryAddress"/> or taken from a caller-supplied name.
	/// </param>
	/// <param name="specification">The pull request's finished configuration.</param>
	/// <param name="cancellationToken">A token to cancel the request.</param>
	/// <returns>The pull request as the host reports it after creation.</returns>
	internal abstract Task<GitPullRequest> CreatePullRequestCoreAsync(GitRepositoryAddress repositoryAddress, GitPullRequestSpecification specification, CancellationToken cancellationToken);

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
		HostingCredential? resolved = ResolveRecognisedCredential(out Credential? credential);
		if (resolved is not null)
		{
			return resolved;
		}

		// Two ways to get here, and they are different caller mistakes, so they get different
		// messages. A null credential means CredentialSource returned null, because that path never
		// assigns the out parameter; a non-null one means the cache held a subtype not in the table.
		throw new InvalidOperationException(credential is null
			? $"Provider '{Name}' has a {nameof(CredentialSource)} that returned null. Return {nameof(HostingCredential)}.{nameof(HostingCredential.None)} to proceed unauthenticated."
			: $"Provider '{Name}' resolved a credential of type '{credential.GetType()}', which this library does not recognise.");
	}

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
		credential = null;

		// Checked before the cache, not merged with it: a caller that supplied a source has said
		// where its credential comes from, and falling back to the keyring when the source returns
		// None would silently authenticate as somebody else.
		if (CredentialSource is not null)
		{
			// A null return leaves `credential` null, which is what tells ResolveCredential to
			// report this as a CredentialSource bug rather than an unrecognised cache subtype. It
			// reaches IsAuthenticated as "no credential a request could carry", which is a property
			// getter and so must not throw.
			return CredentialSource();
		}

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
	/// supplied it, and <see cref="DefaultHandler"/> has to outlive every call this provider makes.
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
	protected HttpClient CreateHttpClient() => new(Handler ?? DefaultHandler, disposeHandler: false);

	/// <summary>
	/// Gets the transport this provider's calls share when no <see cref="Handler"/> was injected.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Abstract rather than a shared <see langword="static"/> field on this base class, so each
	/// provider states which handler is its own. A single field here would have every provider
	/// deriving from <see cref="GitProvider"/> share one connection pool — which is what this
	/// library did while <see cref="AzureDevOpsProvider"/> was the only provider using it, so the
	/// sharing was accidental and invisible rather than intended. Adding a third provider would have
	/// silently enrolled it in Azure DevOps's pool, with nothing in the code to notice.
	/// </para>
	/// <para>
	/// Each implementation is expected to return one process-lifetime instance built by
	/// <see cref="CreateDefaultHandler"/>, not a fresh handler per read. A handler owns a connection
	/// pool, so building and disposing one per call tears down that pool each time and leaves its
	/// sockets in <c>TIME_WAIT</c> — a caller looping over a hundred repositories would build and
	/// destroy a hundred pools. See <see cref="CreateDefaultHandler"/> for why they are never
	/// disposed.
	/// </para>
	/// <para>
	/// <see langword="private protected"/> rather than <see langword="protected"/>: every provider
	/// this library ships lives in this assembly, and no externally-defined subclass can be
	/// instantiated anyway, so widening this member's reach would add public API surface nothing can
	/// use.
	/// </para>
	/// </remarks>
	/// <value>The handler shared across this provider's calls.</value>
	private protected abstract HttpMessageHandler DefaultHandler { get; }

	/// <summary>
	/// Converts a field a host reported into a semantic value, reporting a value this library's own
	/// validation rejects as a hosting failure rather than an argument failure.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The hosting counterpart to <c>GitParseValues.ToSemantic</c>, and it exists for the same reason:
	/// a value arriving from outside is data to be validated, not an argument a caller got wrong.
	/// Calling <c>As&lt;T&gt;()</c> directly on a host's response would throw
	/// <see cref="ArgumentException"/> out of a public hosting method, whose documented failure
	/// surface is the <see cref="GitHostingException"/> hierarchy — the same rule that makes
	/// <c>AzureDevOpsProvider.DeserializeSuccessBody</c> wrap a <c>JsonException</c> rather than let
	/// it escape.
	/// </para>
	/// <para>
	/// A <see langword="null"/> or absent field is not a failure: every field this converts is
	/// optional on <see cref="GitRepository"/>, and a host omitting one is ordinary. Only a field the
	/// host did report and this library cannot represent is an error worth raising, because that is
	/// the case where silently dropping the value would hide a real mismatch between this library's
	/// model and what the host actually sends.
	/// </para>
	/// </remarks>
	/// <typeparam name="TSemantic">The semantic string type to produce.</typeparam>
	/// <param name="value">The raw field as the host reported it, which may be <see langword="null"/>.</param>
	/// <param name="providerName">The provider that reported it, named in the exception.</param>
	/// <param name="description">What the field is, used in the failure message.</param>
	/// <returns>The converted value, or <see langword="null"/> when the host reported none.</returns>
	/// <exception cref="GitHostingRequestException">
	/// <paramref name="value"/> is non-null but fails <typeparamref name="TSemantic"/>'s validation.
	/// </exception>
	private protected static TSemantic? ToHostValue<TSemantic>(string? value, GitProviderName providerName, string description)
		where TSemantic : SemanticString<TSemantic>, new()
	{
		if (value is null)
		{
			return null;
		}

		// Called on the constructed base type rather than as TSemantic.TryCreate, for the reason
		// GitParseValues.ToSemantic gives: TryCreate is a plain static method rather than a static
		// abstract interface member, so invoking it through the type parameter is CS0704.
		if (SemanticString<TSemantic>.TryCreate(value, out TSemantic? result) && result is not null)
		{
			return result;
		}

		throw new GitHostingRequestException(
			$"{providerName} reported a {description} this library cannot represent: '{value}'.");
	}

	/// <summary>
	/// Converts a field a host reported into a semantic value this library's model requires, reporting
	/// an omitted field as a hosting failure rather than letting it reach a <see langword="required"/>
	/// property as <see langword="null"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <see cref="ToHostValue{TSemantic}"/> treats an absent field as ordinary, which is right for
	/// every optional property on <see cref="GitRepository"/> and for
	/// <see cref="GitPullRequest.Author"/> and <see cref="GitPullRequest.WebURI"/>. It is not right
	/// for <see cref="GitPullRequest.Number"/>, <see cref="GitPullRequest.Title"/>,
	/// <see cref="GitPullRequest.SourceBranch"/> and <see cref="GitPullRequest.TargetBranch"/>:
	/// those are <see langword="required"/>, so "not reported" is not a state the model can hold.
	/// </para>
	/// <para>
	/// The alternative — making those four nullable — was considered and rejected. Both hosts
	/// document all four as part of what a pull request *is*, and neither has ever been observed to
	/// omit one; Azure DevOps's DTO declares them <see langword="string?"/> only because it mirrors
	/// the wire shape defensively. Relaxing the model would push a null check onto every caller for a
	/// case no host produces, and would silently turn a host contract violation into a
	/// half-populated record. Raising it keeps the violation visible, and keeps it inside the
	/// <see cref="GitHostingException"/> hierarchy a public hosting method documents.
	/// </para>
	/// </remarks>
	/// <typeparam name="TSemantic">The semantic string type to produce.</typeparam>
	/// <param name="value">The raw field as the host reported it, which may be <see langword="null"/>.</param>
	/// <param name="providerName">The provider that reported it, named in the exception.</param>
	/// <param name="description">What the field is, used in the failure message.</param>
	/// <returns>The converted value.</returns>
	/// <exception cref="GitHostingRequestException">
	/// <paramref name="value"/> is <see langword="null"/>, or is non-null and fails
	/// <typeparamref name="TSemantic"/>'s validation.
	/// </exception>
	private protected static TSemantic ToRequiredHostValue<TSemantic>(string? value, GitProviderName providerName, string description)
		where TSemantic : SemanticString<TSemantic>, new() =>
		ToHostValue<TSemantic>(value, providerName, description)
			?? throw new GitHostingRequestException(
				$"{providerName} reported no {description}, which this library requires.");

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
/// How one repository is addressed on a host: the value to send, and which of the two forms —
/// the host's own id, or the repository's name — that value is.
/// </summary>
/// <remarks>
/// <para>
/// The discriminator is the whole point of this type. A bare string cannot say which form it
/// carries, and a provider cannot recover that from the value's shape: GitHub's repository ids are
/// decimal digits, and a repository may legitimately be <i>named</i> one. Passing the two forms
/// interchangeably is exactly how a numeric id came to be sent where GitHub's <c>/repos/{owner}/{repo}</c>
/// route wants a name, which answers <c>404</c> rather than failing in any way a caller could read.
/// </para>
/// <para>
/// A provider whose id and name routes are the same path — Azure DevOps's <c>{repositoryId}</c> slot
/// — can ignore <see cref="IsHostRepositoryId"/> entirely and send <see cref="Value"/>.
/// </para>
/// </remarks>
/// <param name="Value">The value identifying the repository, unescaped.</param>
/// <param name="IsHostRepositoryId">
/// <see langword="true"/> when <paramref name="Value"/> is the host's own repository id;
/// <see langword="false"/> when it is the repository's name.
/// </param>
internal readonly record struct GitRepositoryAddress(string Value, bool IsHostRepositoryId)
{
	/// <summary>Addresses a repository by the host's own repository id.</summary>
	/// <param name="hostRepositoryId">The host's repository id.</param>
	/// <returns>The address.</returns>
	public static GitRepositoryAddress ByHostRepositoryId(string hostRepositoryId) => new(hostRepositoryId, IsHostRepositoryId: true);

	/// <summary>Addresses a repository by its name.</summary>
	/// <param name="repositoryName">The repository's name.</param>
	/// <returns>The address.</returns>
	public static GitRepositoryAddress ByName(string repositoryName) => new(repositoryName, IsHostRepositoryId: false);
}

/// <summary>
/// The outcome of resolving a provider's credential: a bearer token, a username and password, or
/// nothing — proceeding unauthenticated.
/// </summary>
/// <remarks>
/// A record with a <see cref="HostingCredentialKind"/> discriminator rather than a type hierarchy:
/// a provider applying this result switches on <see cref="Kind"/> exactly once, so a closed set of
/// fields on one type reads as clearly as a hierarchy would and avoids a second type family
/// alongside <see cref="Credential"/>.
/// <para>
/// Public rather than internal, because <see cref="GitProvider.CredentialSource"/> is the vocabulary
/// a caller supplies a credential in. It was internal while the credential cache was the only way in
/// and this type was purely an implementation detail of that path.
/// </para>
/// </remarks>
public sealed record HostingCredential
{
	/// <summary>The singleton result for "no credential to apply".</summary>
	public static readonly HostingCredential None = new() { Kind = HostingCredentialKind.None };

	/// <summary>Gets which of the credential's fields are populated.</summary>
	public required HostingCredentialKind Kind { get; init; }

	/// <summary>
	/// Gets the token, when <see cref="Kind"/> is <see cref="HostingCredentialKind.Token"/> or
	/// <see cref="HostingCredentialKind.BearerToken"/>.
	/// </summary>
	/// <remarks>
	/// One field for both kinds, because both carry exactly one opaque string and it is
	/// <see cref="Kind"/>, not the value, that decides which scheme a provider sends it under.
	/// </remarks>
	public string? Token { get; init; }

	/// <summary>Gets the username, when <see cref="Kind"/> is <see cref="HostingCredentialKind.UsernamePassword"/>.</summary>
	public string? Username { get; init; }

	/// <summary>Gets the password, when <see cref="Kind"/> is <see cref="HostingCredentialKind.UsernamePassword"/>.</summary>
	public string? Password { get; init; }

	/// <summary>Creates a result carrying a host-native token, such as a personal access token.</summary>
	/// <remarks>
	/// Not a bearer token, despite what this method was once documented as: Azure DevOps sends this
	/// kind as Basic with an empty username, the scheme its personal access tokens require, and
	/// GitHub sends it as Octokit's <c>Token</c> scheme. Use <see cref="FromBearerToken(string)"/>
	/// for a credential that must travel as <c>Authorization: Bearer</c>.
	/// </remarks>
	/// <param name="token">The token.</param>
	/// <returns>The resolved credential.</returns>
	public static HostingCredential FromToken(string token) => new() { Kind = HostingCredentialKind.Token, Token = token };

	/// <summary>Creates a result carrying a token to send as <c>Authorization: Bearer</c>.</summary>
	/// <remarks>
	/// Distinct from <see cref="FromToken(string)"/> because the two travel under different schemes
	/// and hosts do not accept them interchangeably. An Entra ID access token authenticates against
	/// Azure DevOps only as Bearer, and fails outright in the Basic slot a personal access token
	/// uses; on GitHub this maps to Octokit's <c>Bearer</c> authentication type, which is what a
	/// GitHub App installation token requires.
	/// </remarks>
	/// <param name="token">The token.</param>
	/// <returns>The resolved credential.</returns>
	public static HostingCredential FromBearerToken(string token) =>
		new() { Kind = HostingCredentialKind.BearerToken, Token = token };

	/// <summary>Creates a result carrying a username and password.</summary>
	/// <param name="username">The username.</param>
	/// <param name="password">The password.</param>
	/// <returns>The resolved credential.</returns>
	public static HostingCredential FromUsernamePassword(string username, string password) =>
		new() { Kind = HostingCredentialKind.UsernamePassword, Username = username, Password = password };
}

/// <summary>Which fields a <see cref="HostingCredential"/> carries, and under which scheme it travels.</summary>
public enum HostingCredentialKind
{
	/// <summary>No credential — proceed unauthenticated.</summary>
	None,

	/// <summary>
	/// A host-native token, in <see cref="HostingCredential.Token"/>, such as a personal access
	/// token. Azure DevOps sends it as Basic with an empty username; GitHub sends it as Octokit's
	/// <c>Token</c> scheme.
	/// </summary>
	Token,

	/// <summary>
	/// A token, in <see cref="HostingCredential.Token"/>, to send as <c>Authorization: Bearer</c> —
	/// an Entra ID access token against Azure DevOps, or a GitHub App token against GitHub.
	/// </summary>
	BearerToken,

	/// <summary>A username and password, in <see cref="HostingCredential.Username"/> and <see cref="HostingCredential.Password"/>.</summary>
	UsernamePassword,
}
