// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

/// <summary>
/// Checks that cloning a repository a hosting provider described keeps its hosting metadata,
/// against a real git binary (ktsu-dev/GitIntegration#256).
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class GitCloneFromProviderTests
{
	private static readonly GitAuthorName AuthorName = "Fixture Author".As<GitAuthorName>();
	private static readonly GitAuthorEmail AuthorEmail = "fixture@example.com".As<GitAuthorEmail>();

	public TestContext TestContext { get; set; } = null!;

	[TestMethod]
	public async Task CloneFromAProviderRepositoryKeepsItsHostingMetadataAsync()
	{
		// A repository from GetRepositoriesAsync carries a RemotePath and hosting metadata but no
		// LocalPath. Cloning it to a chosen destination must keep that metadata, or the working
		// copy can no longer be opened in a browser or passed back to the provider's PR APIs.
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository source = new();
		GitInitResult init = await IntegrationGitFixture.CreateClient()
			.Init(source.Root)
			.WithInitialBranch("main".As<GitBranchName>())
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);
		GitRepository origin = init.Repository;
		await IntegrationGitFixture.ConfigureIdentityAsync(origin, AuthorName, AuthorEmail, cancellationToken)
			.ConfigureAwait(false);

		source.WriteFile("a.txt", "one\n");
		_ = await origin.Add().All().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		GitCommit committed = await origin
			.Commit("cloned commit".As<GitCommitMessage>())
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		GitRepository enumerated = new()
		{
			Name = "example-repo".As<GitRepositoryName>(),
			HostRepositoryId = "90000001".As<GitHostRepositoryId>(),
			WebURI = "https://github.com/example-user/example-repo".As<GitRepositoryWebURI>(),
			RemotePath = source.RootPath.As<GitRepositoryRemotePath>(),
		};

		using TemporaryRepository destinationRoot = new();
		AbsoluteDirectoryPath destination =
			Path.Combine(destinationRoot.RootPath, "copy").As<AbsoluteDirectoryPath>();

		GitRepository clone = await IntegrationGitFixture.CreateClient()
			.Clone(enumerated, destination)
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.AreEqual(destination, clone.LocalPath);
		Assert.AreEqual(enumerated.RemotePath, clone.RemotePath);
		Assert.AreEqual(enumerated.Name, clone.Name);
		Assert.AreEqual(enumerated.HostRepositoryId, clone.HostRepositoryId);
		Assert.AreEqual(enumerated.WebURI, clone.WebURI);

		IReadOnlyList<GitCommit> history =
			await clone.Log().ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.AreEqual(committed.Sha, history[0].Sha);
	}
}
