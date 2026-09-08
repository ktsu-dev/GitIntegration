// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

/// <summary>
/// Exercises the verbs against a real git binary, in a throwaway repository per test.
/// </summary>
/// <remarks>
/// Marked as integration tests and skipped when git is not on PATH, so a contributor who has not
/// installed git still sees a green suite rather than a wall of failures. Setting
/// <see cref="IntegrationGitFixture.RequiredEnvironmentVariable"/> reverses that and makes a missing
/// git a hard failure, which is what CI does — a runner without git must not report success having
/// tested nothing.
/// </remarks>
[TestClass]
[TestCategory("Integration")]
public class GitRoundTripTests
{
	private static readonly GitAuthorName AuthorName = "Fixture Author".As<GitAuthorName>();
	private static readonly GitAuthorEmail AuthorEmail = "fixture@example.com".As<GitAuthorEmail>();

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
		GitClient client = IntegrationGitFixture.CreateClient();

		GitInitResult init = await client
			.Init(temporary.Root)
			.WithInitialBranch("main".As<GitBranchName>())
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.IsFalse(init.AlreadyExisted);

		GitRepository repository = init.Repository;

		await IntegrationGitFixture.ConfigureIdentityAsync(repository, AuthorName, AuthorEmail, cancellationToken)
			.ConfigureAwait(false);

		return repository;
	}

	[TestMethod]
	public async Task InitCreatesARepositoryAndReportsItAsFreshAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository temporary = new();
		GitRepository repository = await InitialiseAsync(temporary, cancellationToken).ConfigureAwait(false);

		Assert.IsTrue(await repository.IsClonedAsync(cancellationToken).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task InitReportsAnExistingRepositoryAsAlreadyExistingAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository temporary = new();
		_ = await InitialiseAsync(temporary, cancellationToken).ConfigureAwait(false);

		GitInitResult second = await IntegrationGitFixture.CreateClient()
			.Init(temporary.Root)
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.IsTrue(second.AlreadyExisted);
	}

	[TestMethod]
	public async Task AddAndCommitProduceAReadableCommitAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

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
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

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
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

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
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

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
	public async Task TagCreateListAndDeleteRoundTripAsync()
	{
		// Runs both kinds of tag against a real git, which is the only way to confirm the format
		// string in GitOutputFormats and the parser reading it agree with what git actually emits —
		// including that %(contents:subject) falls through to the commit's own subject for a
		// lightweight tag, which the parser has to suppress.
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository temporary = new();
		GitRepository repository = await InitialiseAsync(temporary, cancellationToken).ConfigureAwait(false);

		temporary.WriteFile("a.txt", "one\n");
		_ = await repository.Add().All().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		GitCommit commit = await repository.Commit("c1".As<GitCommitMessage>())
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		GitTagName lightweight = "v0.1.0".As<GitTagName>();
		GitTagName annotated = "v0.2.0".As<GitTagName>();

		_ = await repository.CreateTag(lightweight).ExecuteAsync(cancellationToken).ConfigureAwait(false);
		_ = await repository.CreateTag(annotated)
			.Annotating("release 0.2.0".As<GitCommitMessage>())
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		IReadOnlyList<GitTag> tags = await repository.Tags().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		Assert.AreEqual(2, tags.Count);

		GitTag light = tags.Single(tag => tag.Name == lightweight);
		Assert.IsFalse(light.IsAnnotated);
		Assert.AreEqual(commit.Sha, light.Sha);

		// The reference points straight at the commit, so there is no separate tag object.
		Assert.AreEqual(light.Sha, light.ObjectSha);

		// The commit's subject is "c1"; a lightweight tag must report no message rather than that.
		Assert.IsNull(light.Message);

		GitTag annotatedTag = tags.Single(tag => tag.Name == annotated);
		Assert.IsTrue(annotatedTag.IsAnnotated);
		Assert.AreEqual("release 0.2.0", annotatedTag.Message);

		// Sha dereferences to the commit while ObjectSha is the tag object git wrote, so the two
		// differ — the distinction the model exists to carry.
		Assert.AreEqual(commit.Sha, annotatedTag.Sha);
		Assert.AreNotEqual(annotatedTag.Sha, annotatedTag.ObjectSha);

		_ = await repository.DeleteTag(lightweight).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		IReadOnlyList<GitTag> afterDelete =
			await repository.Tags().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		Assert.AreEqual(1, afterDelete.Count);
		Assert.AreEqual(annotated, afterDelete[0].Name);
	}

	[TestMethod]
	public async Task CheckoutResolvesATagRatherThanAFileOfTheSameNameAsync()
	{
		// Checkout emits a trailing "--" rather than a leading --end-of-options, and this is the
		// behaviour that choice buys beyond compatibility with git <= 2.43: the operand is read as a
		// revision, so a tag whose name also matches a path on disk resolves to the tag instead of
		// silently restoring the file.
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository temporary = new();
		GitRepository repository = await InitialiseAsync(temporary, cancellationToken).ConfigureAwait(false);

		temporary.WriteFile("a.txt", "one\n");
		_ = await repository.Add().All().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		_ = await repository.Commit("c1".As<GitCommitMessage>()).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		// A committed file and a tag sharing one name is exactly the ambiguity git warns about.
		GitTagName ambiguous = "ambiguous".As<GitTagName>();
		temporary.WriteFile("ambiguous", "file contents\n");
		_ = await repository.Add().All().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		GitCommit second = await repository.Commit("c2".As<GitCommitMessage>())
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		_ = await repository.CreateTag(ambiguous).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		_ = await repository.Checkout("ambiguous".As<GitRefName>())
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		// Resolving the tag detaches HEAD at its commit. Resolving the path instead would have left
		// HEAD on the branch and merely restored the file.
		GitCommitSha head = await repository.RevParse("HEAD".As<GitRefName>())
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);
		Assert.AreEqual(second.Sha, head);

		GitStatus status = await repository.Status().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		Assert.IsTrue(status.IsDetached);
	}

	[TestMethod]
	public async Task RemoteAddSetUrlAndRemoveRoundTripAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

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
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

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

		GitRepository clone = await IntegrationGitFixture.CreateClient()
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
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository source = new();
		_ = await InitialiseAsync(source, cancellationToken).ConfigureAwait(false);

		using TemporaryRepository occupied = new();
		occupied.WriteFile("in-the-way.txt", "x");

		await Assert.ThrowsExactlyAsync<GitCommandException>(
			async () => await IntegrationGitFixture.CreateClient()
				.Clone(source.RootPath.As<GitRepositoryRemotePath>(), occupied.Root)
				.ExecuteAsync(cancellationToken).ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task DiffReportsAStagedRenameAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

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
