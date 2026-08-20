// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

/// <summary>
/// Exercises fetch, pull, and push against a real git binary and a real remote.
/// </summary>
/// <remarks>
/// The remote is a bare repository on the local filesystem, which git treats exactly like any other
/// remote. That gives real push negotiation and real rejection behaviour with no network and no
/// credentials — the two things that would make these tests flaky or unrunnable in CI.
/// </remarks>
[TestClass]
[TestCategory("Integration")]
public class GitRemoteSyncTests
{
	private static readonly GitAuthorName AuthorName = "Fixture Author".As<GitAuthorName>();
	private static readonly GitAuthorEmail AuthorEmail = "fixture@example.com".As<GitAuthorEmail>();
	private static readonly GitRemoteName Origin = "origin".As<GitRemoteName>();
	private static readonly GitBranchName Main = "main".As<GitBranchName>();

	/// <summary>Creates a bare repository that can stand in for a remote.</summary>
	private static async Task<AbsoluteDirectoryPath> CreateBareRemoteAsync(
		TemporaryRepository temporary,
		CancellationToken cancellationToken)
	{
		GitInitResult init = await IntegrationGitFixture.CreateClient()
			.Init(temporary.Root)
			.Bare()
			.WithInitialBranch(Main)
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.IsFalse(init.AlreadyExisted);

		return init.Repository.LocalPath;
	}

	/// <summary>
	/// Creates a working repository with a deterministic identity, wired to a remote.
	/// </summary>
	private static async Task<GitRepository> CreateWorkingCopyAsync(
		TemporaryRepository temporary,
		AbsoluteDirectoryPath remote,
		CancellationToken cancellationToken)
	{
		GitInitResult init = await IntegrationGitFixture.CreateClient()
			.Init(temporary.Root)
			.WithInitialBranch(Main)
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		GitRepository repository = init.Repository;

		await IntegrationGitFixture.ConfigureIdentityAsync(repository, AuthorName, AuthorEmail, cancellationToken)
			.ConfigureAwait(false);

		_ = await repository
			.AddRemote(Origin, remote.WeakString.As<GitRepositoryRemotePath>())
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		return repository;
	}

	private static async Task<GitCommit> CommitFileAsync(
		GitRepository repository,
		TemporaryRepository temporary,
		string name,
		string contents,
		string message,
		CancellationToken cancellationToken)
	{
		temporary.WriteFile(name, contents);

		_ = await repository.Add().All().ExecuteAsync(cancellationToken).ConfigureAwait(false);

		return await repository
			.Commit(message.As<GitCommitMessage>())
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task PushCreatesTheBranchOnTheRemoteAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository remoteDirectory = new();
		using TemporaryRepository workingDirectory = new();
		using TemporaryRepository readerDirectory = new();

		AbsoluteDirectoryPath remote = await CreateBareRemoteAsync(remoteDirectory, cancellationToken).ConfigureAwait(false);
		GitRepository repository = await CreateWorkingCopyAsync(workingDirectory, remote, cancellationToken).ConfigureAwait(false);

		GitCommit committed = await CommitFileAsync(repository, workingDirectory, "a.txt", "one\n", "c1", cancellationToken).ConfigureAwait(false);

		GitPushResult result = await repository.Push()
			.ToRemote(Origin)
			.WithBranch(Main)
			.SettingUpstream()
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.IsFalse(result.HasRejections);
		Assert.AreEqual(1, result.Updates.Count);
		Assert.AreEqual(GitRefUpdateKind.Created, result.Updates[0].Kind);
		Assert.AreEqual("refs/heads/main".As<GitRefName>(), result.Updates[0].Reference);

		// Independent confirmation, following FetchReportsTheUpdatedRemoteTrackingBranchAsync and
		// PullBringsTheOtherRepositorysCommitAcrossAsync: a second repository reads the branch back
		// out of the bare remote rather than trusting push's own parsed porcelain output alone.
		GitRepository reader = await CreateWorkingCopyAsync(readerDirectory, remote, cancellationToken).ConfigureAwait(false);
		_ = await reader.Pull().FromRemote(Origin).WithBranch(Main).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		IReadOnlyList<GitCommit> history = await reader.Log().ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.AreEqual(1, history.Count);
		Assert.AreEqual(committed.Sha, history[0].Sha);
	}

	[TestMethod]
	public async Task PushingTwiceReportsUpToDateAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository remoteDirectory = new();
		using TemporaryRepository workingDirectory = new();
		using TemporaryRepository readerDirectory = new();

		AbsoluteDirectoryPath remote = await CreateBareRemoteAsync(remoteDirectory, cancellationToken).ConfigureAwait(false);
		GitRepository repository = await CreateWorkingCopyAsync(workingDirectory, remote, cancellationToken).ConfigureAwait(false);

		GitCommit committed = await CommitFileAsync(repository, workingDirectory, "a.txt", "one\n", "c1", cancellationToken).ConfigureAwait(false);
		_ = await repository.Push().ToRemote(Origin).WithBranch(Main).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		GitPushResult second = await repository.Push()
			.ToRemote(Origin)
			.WithBranch(Main)
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.AreEqual(GitRefUpdateKind.UpToDate, second.Updates[0].Kind);

		// Independent confirmation that the remote actually holds the branch and its commit, not
		// only that the second push reported it as up to date.
		GitRepository reader = await CreateWorkingCopyAsync(readerDirectory, remote, cancellationToken).ConfigureAwait(false);
		_ = await reader.Pull().FromRemote(Origin).WithBranch(Main).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		IReadOnlyList<GitCommit> history = await reader.Log().ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.AreEqual(1, history.Count);
		Assert.AreEqual(committed.Sha, history[0].Sha);
	}

	[TestMethod]
	public async Task ARejectedPushThrowsAndCarriesTheDetailAsync()
	{
		// The behaviour the whole push design exists for: git exits non-zero and still reports
		// exactly which reference it refused and why.
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository remoteDirectory = new();
		using TemporaryRepository firstDirectory = new();
		using TemporaryRepository secondDirectory = new();

		AbsoluteDirectoryPath remote = await CreateBareRemoteAsync(remoteDirectory, cancellationToken).ConfigureAwait(false);

		GitRepository first = await CreateWorkingCopyAsync(firstDirectory, remote, cancellationToken).ConfigureAwait(false);
		_ = await CommitFileAsync(first, firstDirectory, "a.txt", "one\n", "c1", cancellationToken).ConfigureAwait(false);
		_ = await first.Push().ToRemote(Origin).WithBranch(Main).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		// A second repository — created independently and pulling before it commits — advances the
		// remote, so the first repository's next push is behind.
		GitRepository second = await CreateWorkingCopyAsync(secondDirectory, remote, cancellationToken).ConfigureAwait(false);
		_ = await second.Pull().FromRemote(Origin).WithBranch(Main).ExecuteAsync(cancellationToken).ConfigureAwait(false);
		_ = await CommitFileAsync(second, secondDirectory, "b.txt", "two\n", "c2", cancellationToken).ConfigureAwait(false);
		_ = await second.Push().ToRemote(Origin).WithBranch(Main).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		_ = await CommitFileAsync(first, firstDirectory, "c.txt", "three\n", "c3", cancellationToken).ConfigureAwait(false);

		GitPushRejectedException exception = await Assert.ThrowsExactlyAsync<GitPushRejectedException>(
			async () => await first.Push().ToRemote(Origin).WithBranch(Main)
				.ExecuteAsync(cancellationToken).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.IsNotNull(exception.Result);
		Assert.IsTrue(exception.Result.HasRejections);
		StringAssert.Contains(exception.Result.Updates[0].Summary, "rejected");
	}

	[TestMethod]
	public async Task TryPushReturnsTheRejectionAsAValueAsync()
	{
		// The deliberate divergence between the two entry points, exercised against real git.
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository remoteDirectory = new();
		using TemporaryRepository firstDirectory = new();
		using TemporaryRepository secondDirectory = new();

		AbsoluteDirectoryPath remote = await CreateBareRemoteAsync(remoteDirectory, cancellationToken).ConfigureAwait(false);

		GitRepository first = await CreateWorkingCopyAsync(firstDirectory, remote, cancellationToken).ConfigureAwait(false);
		_ = await CommitFileAsync(first, firstDirectory, "a.txt", "one\n", "c1", cancellationToken).ConfigureAwait(false);
		_ = await first.Push().ToRemote(Origin).WithBranch(Main).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		GitRepository second = await CreateWorkingCopyAsync(secondDirectory, remote, cancellationToken).ConfigureAwait(false);
		_ = await second.Pull().FromRemote(Origin).WithBranch(Main).ExecuteAsync(cancellationToken).ConfigureAwait(false);
		_ = await CommitFileAsync(second, secondDirectory, "b.txt", "two\n", "c2", cancellationToken).ConfigureAwait(false);
		_ = await second.Push().ToRemote(Origin).WithBranch(Main).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		_ = await CommitFileAsync(first, firstDirectory, "c.txt", "three\n", "c3", cancellationToken).ConfigureAwait(false);

		GitResult<GitPushResult> result = await first.Push()
			.ToRemote(Origin)
			.WithBranch(Main)
			.TryExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.IsTrue(result.Success);
		Assert.IsNotNull(result.Value);
		Assert.IsTrue(result.Value.HasRejections);
	}

	[TestMethod]
	public async Task FetchReportsTheUpdatedRemoteTrackingBranchAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository remoteDirectory = new();
		using TemporaryRepository firstDirectory = new();
		using TemporaryRepository secondDirectory = new();

		AbsoluteDirectoryPath remote = await CreateBareRemoteAsync(remoteDirectory, cancellationToken).ConfigureAwait(false);

		GitRepository first = await CreateWorkingCopyAsync(firstDirectory, remote, cancellationToken).ConfigureAwait(false);
		_ = await CommitFileAsync(first, firstDirectory, "a.txt", "one\n", "c1", cancellationToken).ConfigureAwait(false);
		_ = await first.Push().ToRemote(Origin).WithBranch(Main).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		GitRepository second = await CreateWorkingCopyAsync(secondDirectory, remote, cancellationToken).ConfigureAwait(false);

		GitFetchResult fetched = await second.Fetch()
			.FromRemote(Origin)
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		// The itemised account is only available on git 2.41 and above. Below that the fetch still
		// worked, so assert the reference landed either way and the detail only when it is offered.
		if (fetched.DetailAvailable)
		{
			Assert.AreEqual(1, fetched.Updates.Count);
			Assert.AreEqual("refs/remotes/origin/main".As<GitRefName>(), fetched.Updates[0].Reference);
		}

		IReadOnlyList<GitBranch> branches =
			await second.Branches().RemoteOnly().ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.AreEqual(1, branches.Count);
		Assert.IsTrue(branches[0].IsRemote);
	}

	[TestMethod]
	public async Task FetchingTwiceReportsNothingTheSecondTimeAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository remoteDirectory = new();
		using TemporaryRepository firstDirectory = new();
		using TemporaryRepository secondDirectory = new();

		AbsoluteDirectoryPath remote = await CreateBareRemoteAsync(remoteDirectory, cancellationToken).ConfigureAwait(false);

		GitRepository first = await CreateWorkingCopyAsync(firstDirectory, remote, cancellationToken).ConfigureAwait(false);
		_ = await CommitFileAsync(first, firstDirectory, "a.txt", "one\n", "c1", cancellationToken).ConfigureAwait(false);
		_ = await first.Push().ToRemote(Origin).WithBranch(Main).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		GitRepository second = await CreateWorkingCopyAsync(secondDirectory, remote, cancellationToken).ConfigureAwait(false);
		_ = await second.Fetch().FromRemote(Origin).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		GitFetchResult again = await second.Fetch()
			.FromRemote(Origin)
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.AreEqual(0, again.Updates.Count);

		if (again.DetailAvailable)
		{
			Assert.IsTrue(again.IsUpToDate);
		}
	}

	[TestMethod]
	public async Task PullBringsTheOtherRepositorysCommitAcrossAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository remoteDirectory = new();
		using TemporaryRepository firstDirectory = new();
		using TemporaryRepository secondDirectory = new();

		AbsoluteDirectoryPath remote = await CreateBareRemoteAsync(remoteDirectory, cancellationToken).ConfigureAwait(false);

		GitRepository first = await CreateWorkingCopyAsync(firstDirectory, remote, cancellationToken).ConfigureAwait(false);
		GitCommit committed = await CommitFileAsync(first, firstDirectory, "a.txt", "one\n", "c1", cancellationToken).ConfigureAwait(false);
		_ = await first.Push().ToRemote(Origin).WithBranch(Main).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		GitRepository second = await CreateWorkingCopyAsync(secondDirectory, remote, cancellationToken).ConfigureAwait(false);

		_ = await second.Pull()
			.FromRemote(Origin)
			.WithBranch(Main)
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		IReadOnlyList<GitCommit> history =
			await second.Log().ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.AreEqual(1, history.Count);
		Assert.AreEqual(committed.Sha, history[0].Sha);
	}

	[TestMethod]
	public async Task AConflictingPullThrowsAndLeavesAnUnmergedPathAsync()
	{
		// The one pull outcome with its own type, and the reason it has one: the repository is left
		// mid-merge, and Status() is how a caller finds out what needs attention.
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository remoteDirectory = new();
		using TemporaryRepository firstDirectory = new();
		using TemporaryRepository secondDirectory = new();

		AbsoluteDirectoryPath remote = await CreateBareRemoteAsync(remoteDirectory, cancellationToken).ConfigureAwait(false);

		GitRepository first = await CreateWorkingCopyAsync(firstDirectory, remote, cancellationToken).ConfigureAwait(false);
		_ = await CommitFileAsync(first, firstDirectory, "c.txt", "line1\nline2\n", "base", cancellationToken).ConfigureAwait(false);
		_ = await first.Push().ToRemote(Origin).WithBranch(Main).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		GitRepository second = await CreateWorkingCopyAsync(secondDirectory, remote, cancellationToken).ConfigureAwait(false);
		_ = await second.Pull().FromRemote(Origin).WithBranch(Main).ExecuteAsync(cancellationToken).ConfigureAwait(false);
		_ = await CommitFileAsync(second, secondDirectory, "c.txt", "line1\nTHEIRS\n", "theirs", cancellationToken).ConfigureAwait(false);
		_ = await second.Push().ToRemote(Origin).WithBranch(Main).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		_ = await CommitFileAsync(first, firstDirectory, "c.txt", "line1\nMINE\n", "mine", cancellationToken).ConfigureAwait(false);

		// .Merge() states this pull's own intent rather than leaning on the pull.rebase pinned into
		// each repository's config by CreateWorkingCopyAsync to supply it implicitly — the library
		// expressing what it wants is better than a test reaching around it. The config pin stays
		// regardless: the other pulls in this file must not depend on host state either, and this is
		// simply the one pull that actually diverges to prove it.
		await Assert.ThrowsExactlyAsync<GitPullConflictException>(
			async () => await first.Pull().FromRemote(Origin).WithBranch(Main).Merge()
				.ExecuteAsync(cancellationToken).ConfigureAwait(false))
			.ConfigureAwait(false);

		// The repository is mid-merge, and that state is inspectable through the read-only verbs
		// rather than needing any conflict machinery in this library.
		GitStatus status = await first.Status().ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.IsFalse(status.IsClean);
		Assert.IsTrue(status.Entries.Any(entry => entry.IndexState == GitFileState.Unmerged));
	}

	public TestContext TestContext { get; set; } = null!;
}
