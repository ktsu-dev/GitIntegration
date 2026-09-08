// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using ktsu.CredentialCache;
using ktsu.Semantics.Strings;

[TestClass]
public sealed class GitProviderTests
{
	// CredentialCache.Instance is configured onto a fresh InMemoryCredentialStore exactly once,
	// assembly-wide, by CredentialCacheAssemblySetup's [AssemblyInitialize] — see that type's remarks
	// for why a per-class static constructor doing this is not safe under this assembly's
	// method-level test parallelism. Each test here still gets its own PersonaGUID, which is all the
	// isolation a shared, thread-safe cache needs between tests that touch different keys.

	private static PersonaGUID SeedCredential(Credential credential)
	{
		PersonaGUID persona = CredentialCache.CreatePersonaGUID();
		CredentialCache.Instance.AddOrReplace(persona, credential);
		return persona;
	}

	private static TestProvider CreateProvider(PersonaGUID persona) =>
		new() { Owner = "octocat".As<GitProviderOwner>(), PersonaGUID = persona };

	[TestMethod]
	public void UsesATokenCredential()
	{
		PersonaGUID persona = SeedCredential(new CredentialWithToken { Token = "ghp_abc123".As<CredentialToken>() });
		TestProvider provider = CreateProvider(persona);

		HostingCredential resolved = provider.CallResolveCredential();

		Assert.AreEqual(HostingCredentialKind.Token, resolved.Kind);
		Assert.AreEqual("ghp_abc123", resolved.Token);
	}

	[TestMethod]
	public void UsesAUsernamePasswordCredential()
	{
		PersonaGUID persona = SeedCredential(new CredentialWithUsernamePassword
		{
			Username = "octocat".As<CredentialUsername>(),
			Password = "hunter2".As<CredentialPassword>(),
		});
		TestProvider provider = CreateProvider(persona);

		HostingCredential resolved = provider.CallResolveCredential();

		Assert.AreEqual(HostingCredentialKind.UsernamePassword, resolved.Kind);
		Assert.AreEqual("octocat", resolved.Username);
		Assert.AreEqual("hunter2", resolved.Password);
	}

	[TestMethod]
	public void ProceedsUnauthenticatedWhenNoCredentialIsResolved()
	{
		// A fresh persona that nothing ever seeded: TryGet reports false, and ResolveCredential
		// must treat that exactly like a resolved CredentialWithNothing.
		PersonaGUID persona = CredentialCache.CreatePersonaGUID();
		TestProvider provider = CreateProvider(persona);

		HostingCredential resolved = provider.CallResolveCredential();

		Assert.AreEqual(HostingCredentialKind.None, resolved.Kind);
	}

	[TestMethod]
	public void ProceedsUnauthenticatedForCredentialWithNothing()
	{
		PersonaGUID persona = SeedCredential(new CredentialWithNothing());
		TestProvider provider = CreateProvider(persona);

		HostingCredential resolved = provider.CallResolveCredential();

		Assert.AreEqual(HostingCredentialKind.None, resolved.Kind);
	}

	[TestMethod]
	public void ThrowsForAnUnrecognisedCredentialSubtype()
	{
		PersonaGUID persona = SeedCredential(new UnrecognisedCredential());
		TestProvider provider = CreateProvider(persona);

		InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(() => _ = provider.CallResolveCredential());

		StringAssert.Contains(exception.Message, nameof(UnrecognisedCredential));
	}

	[TestMethod]
	public void ReportsAuthenticatedForATokenCredential()
	{
		PersonaGUID persona = SeedCredential(new CredentialWithToken { Token = "ghp_abc123".As<CredentialToken>() });
		TestProvider provider = CreateProvider(persona);

		Assert.IsTrue(provider.IsAuthenticated);
	}

	[TestMethod]
	public void ReportsAuthenticatedForAUsernamePasswordCredential()
	{
		PersonaGUID persona = SeedCredential(new CredentialWithUsernamePassword
		{
			Username = "octocat".As<CredentialUsername>(),
			Password = "hunter2".As<CredentialPassword>(),
		});
		TestProvider provider = CreateProvider(persona);

		Assert.IsTrue(provider.IsAuthenticated);
	}

	[TestMethod]
	public void ReportsUnauthenticatedWhenNoCredentialIsResolved()
	{
		PersonaGUID persona = CredentialCache.CreatePersonaGUID();
		TestProvider provider = CreateProvider(persona);

		Assert.IsFalse(provider.IsAuthenticated);
	}

	[TestMethod]
	public void ReportsUnauthenticatedForCredentialWithNothing()
	{
		// The case that used to report true: the credential cache holds an entry, so a bare
		// TryGetCredential succeeds, but the entry says "proceed unauthenticated" and no request
		// this provider issues will carry anything. ResolveCredential already maps it to
		// HostingCredentialKind.None, and IsAuthenticated must agree with that rather than with the
		// mere presence of an entry.
		PersonaGUID persona = SeedCredential(new CredentialWithNothing());
		TestProvider provider = CreateProvider(persona);

		Assert.IsFalse(provider.IsAuthenticated);
	}

	[TestMethod]
	public void ReportsUnauthenticatedForAnUnrecognisedCredentialSubtype()
	{
		// A subtype ResolveCredential throws on can never be applied to a request, so reporting
		// authenticated would be false. Reported rather than thrown: a property getter that throws
		// would make a plain "if (provider.IsAuthenticated)" a hazard.
		PersonaGUID persona = SeedCredential(new UnrecognisedCredential());
		TestProvider provider = CreateProvider(persona);

		Assert.IsFalse(provider.IsAuthenticated);
	}

	[TestMethod]
	public void TheSharedTransportBoundsItsPooledConnectionLifetime()
	{
		// PooledConnectionLifetime is what makes a handler held for the life of the process safe:
		// without it a pooled connection is kept indefinitely, and the handler goes on using a host's
		// original address long after DNS moves it. Each provider's shared handler is a private static
		// no test can reach, so the factory both are built from is where the settings get pinned.
		using SocketsHttpHandler handler = GitProvider.CreateDefaultHandler();

		Assert.AreEqual(TimeSpan.FromMinutes(2), handler.PooledConnectionLifetime);

		// Reproduce what Octokit's own default handler configures, so GitHubProvider loses nothing by
		// sharing this shape instead of calling that factory per request.
		Assert.IsFalse(handler.AllowAutoRedirect);
		Assert.AreEqual(
			System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
			handler.AutomaticDecompression);
	}

	[TestMethod]
	public void EachProviderOwnsItsSharedTransportRatherThanInheritingOne()
	{
		// GitProvider used to hold one static SocketsHttpHandler that every subclass shared. That was
		// equivalent to per-provider only by accident, because AzureDevOpsProvider was its sole user
		// and GitHubProvider already had its own; adding a third provider would have silently
		// enrolled it in Azure DevOps's connection pool with nothing in the code to notice.
		//
		// Reflection rather than reading DefaultHandler directly, because that member is
		// private protected and this test class is not a derived type. It also pins the property that
		// actually matters — where the handler is declared — rather than what one instance returns.
		Assert.IsFalse(
			typeof(GitProvider).GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
				.Any(field => typeof(HttpMessageHandler).IsAssignableFrom(field.FieldType)),
			"GitProvider must not hold a transport its subclasses would share implicitly.");

		foreach (Type providerType in new[] { typeof(GitHubProvider), typeof(AzureDevOpsProvider) })
		{
			Assert.IsTrue(
				providerType.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
					.Any(field => typeof(HttpMessageHandler).IsAssignableFrom(field.FieldType)),
				$"{providerType.Name} must declare its own shared transport.");
		}
	}

	private sealed class UnrecognisedCredential : Credential;

	// The minimal subclass a test needs to reach GitProvider's protected members. Its own three
	// abstract overrides are never exercised here — this class exists only to expose
	// ResolveCredential, which credential resolution is what this test class covers.
	private sealed class TestProvider : GitProvider
	{
		public override GitProviderName Name => "TestProvider".As<GitProviderName>();

		// Never reached: these tests either inject a Handler or never issue a request at all. The
		// member is abstract so that each real provider has to name its own shared transport rather
		// than inherit one, which is the point of it existing.
		private protected override HttpMessageHandler DefaultHandler =>
			throw new NotSupportedException("Not exercised by these tests.");

		public override Task<IReadOnlyList<GitRepository>> GetRepositoriesAsync(CancellationToken cancellationToken = default) =>
			throw new NotSupportedException("Not exercised by these tests.");

		internal override Task<IReadOnlyList<GitPullRequest>> GetPullRequestsCoreAsync(string repositoryIdentifier, CancellationToken cancellationToken) =>
			throw new NotSupportedException("Not exercised by these tests.");

		internal override Task<GitPullRequest> CreatePullRequestCoreAsync(string repositoryIdentifier, GitPullRequestSpecification specification, CancellationToken cancellationToken) =>
			throw new NotSupportedException("Not exercised by these tests.");

		public HostingCredential CallResolveCredential() => ResolveCredential();
	}
}
