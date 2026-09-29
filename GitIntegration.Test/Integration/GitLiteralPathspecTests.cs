// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

/// <summary>
/// Checks that a file path given to a verb names that one file, against a real git binary.
/// </summary>
/// <remarks>
/// git reads its path operands as pathspecs by default, so "file[1].txt" is a glob that also
/// matches file1.txt, and a leading colon introduces pathspec magic (ktsu-dev/GitIntegration#125).
/// </remarks>
[TestClass]
[TestCategory("Integration")]
public class GitLiteralPathspecTests
{
	private static readonly GitAuthorName AuthorName = "Fixture Author".As<GitAuthorName>();
	private static readonly GitAuthorEmail AuthorEmail = "fixture@example.com".As<GitAuthorEmail>();

	private static async Task<GitRepository> InitializeWithCommitAsync(
		TemporaryRepository temporary,
		CancellationToken cancellationToken)
	{
		GitClient client = IntegrationGitFixture.CreateClient();

		GitInitResult init = await client
			.Init(temporary.Root)
			.WithInitialBranch("main".As<GitBranchName>())
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		GitRepository repository = init.Repository;
		await IntegrationGitFixture.ConfigureIdentityAsync(repository, AuthorName, AuthorEmail, cancellationToken)
			.ConfigureAwait(false);

		temporary.WriteFile("seed.txt", "seed\n");
		_ = await repository.Add().All().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		_ = await repository.Commit("seed".As<GitCommitMessage>()).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		return repository;
	}

	private static async Task<string> StagedPathsAsync(GitRepository repository, CancellationToken cancellationToken)
	{
		GitStatus status = await repository.Status().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		return string.Join(
			',',
			status.Entries
				.Where(entry => entry.IndexState == GitFileState.Added)
				.Select(entry => entry.Path.WeakString)
				.Order(StringComparer.Ordinal));
	}

	[TestMethod]
	public async Task UnstageOfABracketedNameLeavesTheFileItAlsoMatchesStagedAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository temporary = new();
		GitRepository repository = await InitializeWithCommitAsync(temporary, cancellationToken).ConfigureAwait(false);

		temporary.WriteFile("file[1].txt", "a\n");
		temporary.WriteFile("file1.txt", "b\n");
		_ = await repository.Add().All().ExecuteAsync(cancellationToken).ConfigureAwait(false);

		_ = await repository.Unstage("file[1].txt".As<RelativeFilePath>()).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.AreEqual("file1.txt", await StagedPathsAsync(repository, cancellationToken).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task UnstageOfAColonLeadingNameUnstagesThatFileAsync()
	{
		if (OperatingSystem.IsWindows())
		{
			Assert.Inconclusive("Windows file names cannot contain a colon.");
		}

		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository temporary = new();
		GitRepository repository = await InitializeWithCommitAsync(temporary, cancellationToken).ConfigureAwait(false);

		temporary.WriteFile(":foo", "a\n");
		temporary.WriteFile("foo", "b\n");
		_ = await repository.Add().All().ExecuteAsync(cancellationToken).ConfigureAwait(false);

		_ = await repository.Unstage(":foo".As<RelativeFilePath>()).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.AreEqual("foo", await StagedPathsAsync(repository, cancellationToken).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task AddOfABracketedNameStagesOnlyThatFileAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository temporary = new();
		GitRepository repository = await InitializeWithCommitAsync(temporary, cancellationToken).ConfigureAwait(false);

		temporary.WriteFile("file[1].txt", "a\n");
		temporary.WriteFile("file1.txt", "b\n");

		_ = await repository.Add().ForPath("file[1].txt".As<RelativeFilePath>()).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.AreEqual("file[1].txt", await StagedPathsAsync(repository, cancellationToken).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task PatchForABracketedNameReturnsOnlyThatFileAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository temporary = new();
		GitRepository repository = await InitializeWithCommitAsync(temporary, cancellationToken).ConfigureAwait(false);

		temporary.WriteFile("file[1].txt", "a\n");
		temporary.WriteFile("file1.txt", "b\n");
		_ = await repository.Add().All().ExecuteAsync(cancellationToken).ConfigureAwait(false);

		GitPatch patch = await repository.Patch().Staged().ForPath("file[1].txt".As<RelativeFilePath>())
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.AreEqual("file[1].txt", string.Join(',', patch.Files.Select(file => file.Path.WeakString)));
	}

	public TestContext TestContext { get; set; } = null!;
}
