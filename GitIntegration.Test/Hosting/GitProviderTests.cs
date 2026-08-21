// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using ktsu.CredentialCache;
using ktsu.CredentialCache.Storage;
using ktsu.Semantics.Strings;

[TestClass]
public sealed class GitProviderTests
{
	// CredentialCache.Instance is a process-wide singleton whose default store is the platform's
	// native secret manager. It is switched onto a fresh InMemoryCredentialStore exactly once, in
	// this static constructor, rather than per test: the CLR guarantees a type initializer runs at
	// most once even under concurrent first access, which a per-test
	// ResetSingletonForTesting()+ConfigureStore() pair does not — Microsoft Testing Platform runs
	// this class's test methods in parallel, and two tests each resetting the singleton around a
	// third test's AddOrReplace can wipe its seeded credential before ResolveCredential reads it.
	// Each test still gets its own PersonaGUID, which is all the isolation a shared, thread-safe
	// cache needs between tests that touch different keys.
	static GitProviderTests()
	{
		CredentialCache.ResetSingletonForTesting();
		CredentialCache.ConfigureStore(new InMemoryCredentialStore());
	}

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

	private sealed class UnrecognisedCredential : Credential;

	// The minimal subclass a test needs to reach GitProvider's protected members. Its own three
	// abstract overrides are never exercised here — this class exists only to expose
	// ResolveCredential, which credential resolution is what this test class covers.
	private sealed class TestProvider : GitProvider
	{
		public override GitProviderName Name => "TestProvider".As<GitProviderName>();

		public override Task<IReadOnlyList<GitRepository>> GetRepositoriesAsync(CancellationToken cancellationToken = default) =>
			throw new NotSupportedException("Not exercised by these tests.");

		public override Task<IReadOnlyList<GitPullRequest>> GetPullRequestsAsync(GitRepositoryName repositoryName, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException("Not exercised by these tests.");

		internal override Task<GitPullRequest> CreatePullRequestCoreAsync(GitRepositoryName repositoryName, GitPullRequestSpecification specification, CancellationToken cancellationToken) =>
			throw new NotSupportedException("Not exercised by these tests.");

		public HostingCredential CallResolveCredential() => ResolveCredential();
	}
}
