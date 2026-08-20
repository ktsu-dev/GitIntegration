// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Essentials.FileSystemProviders.Native;
using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

/// <summary>
/// Exercises the verbs against a real git binary, in a throwaway repository per test.
/// </summary>
/// <remarks>
/// Marked as integration tests and skipped when git is not on PATH, so a machine or a CI job
/// without git still reports a green suite rather than a wall of failures.
/// </remarks>
[TestClass]
[TestCategory("Integration")]
public class GitRoundTripTests
{
	private static readonly GitAuthorName AuthorName = "Fixture Author".As<GitAuthorName>();
	private static readonly GitAuthorEmail AuthorEmail = "fixture@example.com".As<GitAuthorEmail>();

	private static GitClient CreateClient() =>
		new(new RunCommandGitProcessRunner(new GitOptions()), new NativeFileSystemProvider());

	/// <summary>
	/// Skips the calling test when no usable git binary is present.
	/// </summary>
	private static async Task RequireGitAsync(CancellationToken cancellationToken)
	{
		try
		{
			_ = await CreateClient().GetVersionAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (GitExecutableNotFoundException)
		{
			Assert.Inconclusive("git is not on PATH, so the integration tests were skipped.");
		}
	}

	/// <summary>
	/// Initialises a repository with a deterministic identity and initial branch.
	/// </summary>
	/// <remarks>
	/// The identity is written into the repository's own config rather than taken from the host,
	/// so the tests neither depend on a configured user nor disturb one. The initial branch is
	/// named explicitly for the same reason: <c>init.defaultBranch</c> varies by machine.
	/// </remarks>
	private static async Task<GitRepository> InitialiseAsync(
		TemporaryRepository temporary,
		CancellationToken cancellationToken)
	{
		GitClient client = CreateClient();

		GitInitResult init = await client
			.Init(temporary.Root)
			.WithInitialBranch("main".As<GitBranchName>())
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.IsFalse(init.AlreadyExisted);

		GitRepository repository = init.Repository;

		_ = await new GitTextBuilder(
			repository.ProcessRunner!, repository.LocalPath, "config", "user.name", AuthorName.WeakString)
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		_ = await new GitTextBuilder(
			repository.ProcessRunner!, repository.LocalPath, "config", "user.email", AuthorEmail.WeakString)
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		// A developer with commit.gpgsign=true set globally would otherwise have every commit here
		// blocked waiting on a signing key or prompt. Setting it false in the repository's own config
		// overrides that without touching the host's global configuration.
		_ = await new GitTextBuilder(
			repository.ProcessRunner!, repository.LocalPath, "config", "commit.gpgsign", "false")
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		return repository;
	}

	[TestMethod]
	public async Task InitCreatesARepositoryAndReportsItAsFreshAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository temporary = new();
		GitRepository repository = await InitialiseAsync(temporary, cancellationToken).ConfigureAwait(false);

		Assert.IsTrue(await repository.IsClonedAsync(cancellationToken).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task InitReportsAnExistingRepositoryAsAlreadyExistingAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository temporary = new();
		_ = await InitialiseAsync(temporary, cancellationToken).ConfigureAwait(false);

		GitInitResult second = await CreateClient()
			.Init(temporary.Root)
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.IsTrue(second.AlreadyExisted);
	}

	[TestMethod]
	public async Task AddAndCommitProduceAReadableCommitAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository temporary = new();
		GitRepository repository = await InitialiseAsync(temporary, cancellationToken).ConfigureAwait(false);

		temporary.WriteFile("a.txt", "one\n");
		_ = await repository.Add().All().ExecuteAsync(cancellationToken).ConfigureAwait(false);

		GitCommit commit = await repository
			.Commit("first commit".As<GitCommitMessage>())
			.WithBody("A body line.")
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		// The readback is the whole reason Commit runs twice — this asserts it returned real data,
		// not the abbreviated summary git prints.
		Assert.AreEqual("first commit", commit.Subject);
		Assert.AreEqual("A body line.", commit.Body);
		Assert.AreEqual(AuthorName, commit.Author.Name);
		Assert.AreEqual(AuthorEmail, commit.Author.Email);
		Assert.AreEqual(0, commit.ParentShas.Count);
		Assert.AreEqual(40, commit.Sha.WeakString.Length);
	}

	[TestMethod]
	public async Task CommittingWithNothingStagedThrowsTheDedicatedExceptionAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository temporary = new();
		GitRepository repository = await InitialiseAsync(temporary, cancellationToken).ConfigureAwait(false);

		temporary.WriteFile("a.txt", "one\n");
		_ = await repository.Add().All().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		_ = await repository.Commit("c1".As<GitCommitMessage>()).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		// Nothing has changed since, so git exits 1 and says so on standard output.
		await Assert.ThrowsExactlyAsync<GitNothingToCommitException>(
			async () => await repository.Commit("c2".As<GitCommitMessage>())
				.ExecuteAsync(cancellationToken).ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task StatusReflectsStagedAndUntrackedWorkAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository temporary = new();
		GitRepository repository = await InitialiseAsync(temporary, cancellationToken).ConfigureAwait(false);

		temporary.WriteFile("a.txt", "one\n");
		_ = await repository.Add().All().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		_ = await repository.Commit("c1".As<GitCommitMessage>()).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		GitStatus clean = await repository.Status().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		Assert.IsTrue(clean.IsClean);
		Assert.AreEqual("main".As<GitBranchName>(), clean.Branch);

		temporary.WriteFile("untracked.txt", "two\n");
		GitStatus dirty = await repository.Status().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		Assert.IsFalse(dirty.IsClean);
	}

	[TestMethod]
	public async Task BranchCreateCheckoutAndDeleteRoundTripAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository temporary = new();
		GitRepository repository = await InitialiseAsync(temporary, cancellationToken).ConfigureAwait(false);

		temporary.WriteFile("a.txt", "one\n");
		_ = await repository.Add().All().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		_ = await repository.Commit("c1".As<GitCommitMessage>()).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		GitBranchName feature = "feature/x".As<GitBranchName>();
		_ = await repository.CreateBranch(feature).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		IReadOnlyList<GitBranch> afterCreate =
			await repository.Branches().LocalOnly().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		Assert.AreEqual(2, afterCreate.Count);

		_ = await repository.Checkout("feature/x".As<GitRefName>()).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		GitStatus onFeature = await repository.Status().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		Assert.AreEqual(feature, onFeature.Branch);

		_ = await repository.Checkout("main".As<GitRefName>()).ExecuteAsync(cancellationToken).ConfigureAwait(false);
		_ = await repository.DeleteBranch(feature).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		IReadOnlyList<GitBranch> afterDelete =
			await repository.Branches().LocalOnly().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		Assert.AreEqual(1, afterDelete.Count);
	}

	[TestMethod]
	public async Task RemoteAddSetUrlAndRemoveRoundTripAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository temporary = new();
		GitRepository repository = await InitialiseAsync(temporary, cancellationToken).ConfigureAwait(false);

		GitRemoteName origin = "origin".As<GitRemoteName>();
		GitRepositoryRemotePath first = "https://example.com/one.git".As<GitRepositoryRemotePath>();
		GitRepositoryRemotePath second = "https://example.com/two.git".As<GitRepositoryRemotePath>();

		_ = await repository.AddRemote(origin, first).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		IReadOnlyList<GitRemote> added =
			await repository.Remotes().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		Assert.AreEqual(1, added.Count);
		Assert.AreEqual(first, added[0].FetchUrl);

		_ = await repository.SetRemoteUrl(origin, second).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		IReadOnlyList<GitRemote> changed =
			await repository.Remotes().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		Assert.AreEqual(second, changed[0].FetchUrl);

		_ = await repository.RemoveRemote(origin).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		IReadOnlyList<GitRemote> removed =
			await repository.Remotes().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		Assert.AreEqual(0, removed.Count);
	}

	[TestMethod]
	public async Task CloneReproducesTheSourceHistoryAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository source = new();
		GitRepository origin = await InitialiseAsync(source, cancellationToken).ConfigureAwait(false);

		source.WriteFile("a.txt", "one\n");
		_ = await origin.Add().All().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		GitCommit committed = await origin
			.Commit("cloned commit".As<GitCommitMessage>())
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository destinationRoot = new();
		AbsoluteDirectoryPath destination =
			Path.Combine(destinationRoot.RootPath, "copy").As<AbsoluteDirectoryPath>();

		GitRepository clone = await CreateClient()
			.Clone(source.RootPath.As<GitRepositoryRemotePath>(), destination)
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		IReadOnlyList<GitCommit> history =
			await clone.Log().ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.AreEqual(1, history.Count);
		Assert.AreEqual(committed.Sha, history[0].Sha);
	}

	[TestMethod]
	public async Task CloneRefusesANonEmptyDestinationAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository source = new();
		_ = await InitialiseAsync(source, cancellationToken).ConfigureAwait(false);

		using TemporaryRepository occupied = new();
		occupied.WriteFile("in-the-way.txt", "x");

		await Assert.ThrowsExactlyAsync<GitCommandException>(
			async () => await CreateClient()
				.Clone(source.RootPath.As<GitRepositoryRemotePath>(), occupied.Root)
				.ExecuteAsync(cancellationToken).ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task DiffReportsAStagedRenameAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository temporary = new();
		GitRepository repository = await InitialiseAsync(temporary, cancellationToken).ConfigureAwait(false);

		temporary.WriteFile("before.txt", "line1\nline2\nline3\n");
		_ = await repository.Add().All().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		_ = await repository.Commit("c1".As<GitCommitMessage>()).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		// A pure rename: the content must be identical or git scores it below the rename threshold
		// and reports a delete plus an add instead.
		File.Move(
			Path.Combine(temporary.RootPath, "before.txt"),
			Path.Combine(temporary.RootPath, "after.txt"));
		_ = await repository.Add().All().ExecuteAsync(cancellationToken).ConfigureAwait(false);

		IReadOnlyList<GitDiffEntry> changes = await repository.Diff()
			.Staged()
			.DetectRenames()
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.AreEqual(1, changes.Count);
		Assert.AreEqual(GitChangeKind.Renamed, changes[0].Kind);
		Assert.AreEqual("before.txt".As<RelativeFilePath>(), changes[0].OriginalPath);
		Assert.AreEqual("after.txt".As<RelativeFilePath>(), changes[0].Path);
		Assert.AreEqual(100, changes[0].SimilarityPercent);
	}

	public TestContext TestContext { get; set; } = null!;
}
