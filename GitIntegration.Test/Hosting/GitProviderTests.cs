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
	// native secret manager. Every test resets it onto a fresh InMemoryCredentialStore first, so
	// tests never touch real Windows Credential Manager / Keychain / libsecret state, and each
	// test's persona is unique, so tests sharing the process-wide singleton cannot see each other's
	// seeded credentials.
	private static PersonaGUID SeedCredential(Credential credential)
	{
		CredentialCache.ResetSingletonForTesting();
		CredentialCache.ConfigureStore(new InMemoryCredentialStore());

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
		CredentialCache.ResetSingletonForTesting();
		CredentialCache.ConfigureStore(new InMemoryCredentialStore());
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
