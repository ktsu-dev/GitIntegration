// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

/// <summary>
/// Exercises bare repositories, which have no working tree, against a real git binary (#263).
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class GitBareRepositoryTests
{
	private static readonly GitAuthorName AuthorName = "Fixture Author".As<GitAuthorName>();
	private static readonly GitAuthorEmail AuthorEmail = "fixture@example.com".As<GitAuthorEmail>();

	private static async Task<AbsoluteDirectoryPath> CreateSourceAsync(
		TemporaryRepository temporary,
		CancellationToken cancellationToken)
	{
		GitInitResult init = await IntegrationGitFixture.CreateClient()
			.Init(temporary.Root)
			.WithInitialBranch("main".As<GitBranchName>())
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);
		GitRepository repository = init.Repository;

		await IntegrationGitFixture.ConfigureIdentityAsync(repository, AuthorName, AuthorEmail, cancellationToken)
			.ConfigureAwait(false);

		temporary.WriteFile("a.txt", "one\n");
		_ = await repository.Add().All().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		_ = await repository.Commit("c1".As<GitCommitMessage>()).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		return temporary.Root;
	}

	[TestMethod]
	public async Task ABareCloneIsClonedAndCanBeReopenedAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository sourceDirectory = new();
		using TemporaryRepository cloneParent = new();
		AbsoluteDirectoryPath source = await CreateSourceAsync(sourceDirectory, cancellationToken).ConfigureAwait(false);
		AbsoluteDirectoryPath destination = Path.Join(cloneParent.RootPath, "b.git").As<AbsoluteDirectoryPath>();
		GitClient client = IntegrationGitFixture.CreateClient();

		GitRepository clone = await client
			.Clone(source.WeakString.As<GitRepositoryRemotePath>(), destination)
			.Bare()
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.IsTrue(await clone.IsClonedAsync(cancellationToken).ConfigureAwait(false));

		GitRepository opened = await client.OpenAsync(destination, cancellationToken).ConfigureAwait(false);
		Assert.AreEqual(destination, opened.LocalPath);

		IReadOnlyList<GitBranch> branches =
			await opened.Branches().LocalOnly().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		Assert.AreEqual(1, branches.Count);
		Assert.AreEqual("main", branches[0].Name.WeakString);
	}

	[TestMethod]
	public async Task DiscoveryFromInsideABareRepositoryFindsItsRootAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository temporary = new();
		GitClient client = IntegrationGitFixture.CreateClient();
		GitInitResult init = await client.Init(temporary.Root).Bare().ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.IsTrue(await init.Repository.IsClonedAsync(cancellationToken).ConfigureAwait(false));

		GitRepository? discovered = await client
			.DiscoverAsync(Path.Join(temporary.RootPath, "refs").As<AbsoluteDirectoryPath>(), cancellationToken)
			.ConfigureAwait(false);
		Assert.IsNotNull(discovered);
		Assert.AreEqual(temporary.Root, discovered.LocalPath);

		// A subdirectory of a bare repository is not itself a clone.
		GitRepository below = new()
		{
			LocalPath = Path.Join(temporary.RootPath, "refs").As<AbsoluteDirectoryPath>(),
			ProcessRunner = init.Repository.ProcessRunner,
		};
		Assert.IsFalse(await below.IsClonedAsync(cancellationToken).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task ANonBareRepositorysGitDirectoryIsNeitherClonedNorDiscoveredAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository temporary = new();
		GitClient client = IntegrationGitFixture.CreateClient();
		GitInitResult init = await client.Init(temporary.Root).ExecuteAsync(cancellationToken).ConfigureAwait(false);
		AbsoluteDirectoryPath gitDirectory = Path.Join(temporary.RootPath, ".git").As<AbsoluteDirectoryPath>();

		GitRepository inside = new() { LocalPath = gitDirectory, ProcessRunner = init.Repository.ProcessRunner };
		Assert.IsFalse(await inside.IsClonedAsync(cancellationToken).ConfigureAwait(false));
		Assert.IsNull(await client.DiscoverAsync(gitDirectory, cancellationToken).ConfigureAwait(false));
	}

	public TestContext TestContext { get; set; } = null!;
}
