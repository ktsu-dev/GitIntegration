// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

/// <summary>
/// Exercises the submodule verbs against a real git binary and real submodules.
/// </summary>
/// <remarks>
/// The unit tests pin this library's reading of captured output; only a real repository can confirm
/// git actually emits that output, and that <c>ls-files</c> and <c>submodule status</c> agree the
/// way the parser assumes. Both commands are read here in every state a submodule can be in.
/// <para>
/// Every git invocation that reaches a submodule's remote runs with
/// <c>protocol.file.allow=always</c>, because these tests use a local directory as the submodule's
/// remote and git has refused the <c>file://</c> transport for submodules by default since the
/// CVE-2022-39253 fix. That is a property of the fixture, not of this library.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("Integration")]
public class GitSubmoduleTests
{
	private static readonly GitAuthorName AuthorName = "Fixture Author".As<GitAuthorName>();
	private static readonly GitAuthorEmail AuthorEmail = "fixture@example.com".As<GitAuthorEmail>();
	private static readonly GitBranchName Main = "main".As<GitBranchName>();

	/// <summary>Creates a repository with one commit, to stand in for a submodule's remote.</summary>
	private static async Task<GitRepository> CreateRepositoryAsync(
		TemporaryRepository temporary,
		string fileName,
		CancellationToken cancellationToken)
	{
		GitInitResult init = await IntegrationGitFixture.CreateClient()
			.Init(temporary.Root)
			.WithInitialBranch(Main)
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		GitRepository repository = init.Repository;

		await IntegrationGitFixture.ConfigureIdentityAsync(repository, AuthorName, AuthorEmail, cancellationToken)
			.ConfigureAwait(false);

		temporary.WriteFile(fileName, "one\n");
		_ = await repository.Add().All().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		_ = await repository.Commit("c1".As<GitCommitMessage>()).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		return repository;
	}

	/// <summary>
	/// Runs <c>git submodule add</c>, which this library does not wrap.
	/// </summary>
	/// <remarks>
	/// Adding a submodule is out of scope for the library — the issues that brought submodules in
	/// asked for listing, updating, and the recursion flags, not for creation. The fixture therefore
	/// sets one up through the runner directly, so the verbs under test have something real to read.
	/// </remarks>
	private static async Task AddSubmoduleAsync(
		GitRepository superproject,
		AbsoluteDirectoryPath submoduleRemote,
		string path,
		CancellationToken cancellationToken)
	{
		GitProcessResult result = await superproject.ProcessRunner!.RunAsync(
			new GitProcessRequest
			{
				Arguments =
				[
					"-C", superproject.LocalPath!.WeakString,
					"-c", "protocol.file.allow=always",
					"submodule", "add", "--", submoduleRemote.WeakString, path,
				],
			},
			cancellationToken).ConfigureAwait(false);

		Assert.IsTrue(result.Success, $"submodule add failed: {result.StandardError}");

		_ = await superproject.Add().All().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		_ = await superproject.Commit("add submodule".As<GitCommitMessage>())
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task ReportsASynchronisedSubmoduleAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository subDirectory = new();
		using TemporaryRepository superDirectory = new();

		GitRepository sub = await CreateRepositoryAsync(subDirectory, "s.txt", cancellationToken).ConfigureAwait(false);
		GitRepository super = await CreateRepositoryAsync(superDirectory, "m.txt", cancellationToken).ConfigureAwait(false);

		await AddSubmoduleAsync(super, sub.LocalPath!, "libs/sub", cancellationToken).ConfigureAwait(false);

		IReadOnlyList<GitSubmodule> submodules =
			await super.Submodules().ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.AreEqual(1, submodules.Count);
		Assert.AreEqual("libs/sub".As<RelativeDirectoryPath>(), submodules[0].Path);

		// The leading-space marker. This is the assertion that fails if the status output is ever
		// trimmed before it reaches the parser, since the marker is itself a space.
		Assert.AreEqual(GitSubmoduleState.InSync, submodules[0].State);
		Assert.AreEqual(submodules[0].Sha, submodules[0].CheckedOutSha);
		Assert.IsNotNull(submodules[0].Describe);
	}

	[TestMethod]
	public async Task ReportsTheGitlinkAndTheCheckoutSeparatelyWhenTheyDivergeAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository subDirectory = new();
		using TemporaryRepository superDirectory = new();

		GitRepository sub = await CreateRepositoryAsync(subDirectory, "s.txt", cancellationToken).ConfigureAwait(false);
		GitRepository super = await CreateRepositoryAsync(superDirectory, "m.txt", cancellationToken).ConfigureAwait(false);

		await AddSubmoduleAsync(super, sub.LocalPath!, "libs/sub", cancellationToken).ConfigureAwait(false);

		IReadOnlyList<GitSubmodule> before =
			await super.Submodules().ExecuteAsync(cancellationToken).ConfigureAwait(false);

		// Commit inside the submodule's own working directory. The superproject's gitlink does not
		// move, so the two ids diverge — which is the whole reason the model carries both.
		GitRepository checkout = new()
		{
			// Path.Join rather than Path.Combine: the intent is "append these segments to this base",
			// and Combine resets to a later segment if one is ever rooted.
			LocalPath = System.IO.Path.Join(superDirectory.RootPath, "libs", "sub").As<AbsoluteDirectoryPath>(),
			ProcessRunner = super.ProcessRunner,
		};

		await IntegrationGitFixture.ConfigureIdentityAsync(checkout, AuthorName, AuthorEmail, cancellationToken)
			.ConfigureAwait(false);

		// A single relative literal: WriteFile combines it under the fixture root itself, and git and
		// both platforms accept a forward slash here.
		superDirectory.WriteFile("libs/sub/s.txt", "two\n");
		_ = await checkout.Add().All().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		GitCommit moved = await checkout.Commit("c2".As<GitCommitMessage>())
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		IReadOnlyList<GitSubmodule> after =
			await super.Submodules().ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.AreEqual(GitSubmoduleState.DifferentCommit, after[0].State);

		// The gitlink is unchanged — the superproject still records what it always did.
		Assert.AreEqual(before[0].Sha, after[0].Sha);

		// The checkout is the new commit, and it is not the gitlink.
		Assert.AreEqual(moved.Sha, after[0].CheckedOutSha);
		Assert.AreNotEqual(after[0].Sha, after[0].CheckedOutSha);
	}

	[TestMethod]
	public async Task ReportsAnUninitializedSubmoduleAndThenUpdatesItAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository subDirectory = new();
		using TemporaryRepository superDirectory = new();
		using TemporaryRepository cloneDirectory = new();

		GitRepository sub = await CreateRepositoryAsync(subDirectory, "s.txt", cancellationToken).ConfigureAwait(false);
		GitRepository super = await CreateRepositoryAsync(superDirectory, "m.txt", cancellationToken).ConfigureAwait(false);

		await AddSubmoduleAsync(super, sub.LocalPath!, "libs/sub", cancellationToken).ConfigureAwait(false);

		// A plain clone leaves the submodule registered but not checked out, which is the state the
		// "-" marker names and the one a caller most needs to be able to detect.
		GitProcessResult cloned = await super.ProcessRunner!.RunAsync(
			new GitProcessRequest
			{
				Arguments =
				[
					"-c", "protocol.file.allow=always",
					"clone", "--", superDirectory.RootPath, cloneDirectory.RootPath,
				],
			},
			cancellationToken).ConfigureAwait(false);
		Assert.IsTrue(cloned.Success, $"clone failed: {cloned.StandardError}");

		GitRepository clone = await IntegrationGitFixture.CreateClient()
			.OpenAsync(cloneDirectory.Root, cancellationToken).ConfigureAwait(false);

		IReadOnlyList<GitSubmodule> before =
			await clone.Submodules().ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.AreEqual(1, before.Count);
		Assert.AreEqual(GitSubmoduleState.Uninitialized, before[0].State);

		// Nothing is checked out, so nothing is reported — even though git prints the recorded
		// gitlink again on that line, which would otherwise make this look synchronised.
		Assert.IsNull(before[0].CheckedOutSha);
		Assert.IsNull(before[0].Describe);

		GitProcessResult updated = await clone.ProcessRunner!.RunAsync(
			new GitProcessRequest
			{
				Arguments =
				[
					"-C", cloneDirectory.RootPath,
					"-c", "protocol.file.allow=always",
					.. clone.UpdateSubmodules().Initialize().Recursive().BuildArguments(),
				],
			},
			cancellationToken).ConfigureAwait(false);
		Assert.IsTrue(updated.Success, $"submodule update failed: {updated.StandardError}");

		IReadOnlyList<GitSubmodule> after =
			await clone.Submodules().ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.AreEqual(GitSubmoduleState.InSync, after[0].State);
		Assert.AreEqual(after[0].Sha, after[0].CheckedOutSha);
	}

	[TestMethod]
	public async Task ReportsAConflictedSubmoduleOnceWithNoCheckedOutCommitAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository subDirectory = new();
		using TemporaryRepository superDirectory = new();

		GitRepository sub = await CreateRepositoryAsync(subDirectory, "s.txt", cancellationToken).ConfigureAwait(false);
		GitRepository super = await CreateRepositoryAsync(superDirectory, "m.txt", cancellationToken).ConfigureAwait(false);

		await AddSubmoduleAsync(super, sub.LocalPath!, "libs/sub", cancellationToken).ConfigureAwait(false);

		// The submodule's own working copy, as a repository in its own right — the same composition a
		// caller uses to recurse, and the only way to move the gitlink somewhere the superproject can
		// then record.
		GitRepository checkout = new()
		{
			LocalPath = System.IO.Path.Join(superDirectory.RootPath, "libs", "sub").As<AbsoluteDirectoryPath>(),
			ProcessRunner = super.ProcessRunner,
		};

		await IntegrationGitFixture.ConfigureIdentityAsync(checkout, AuthorName, AuthorEmail, cancellationToken)
			.ConfigureAwait(false);

		// Both branches are cut from the submodule's single commit before either moves, so the two
		// commits below genuinely diverge. git resolves a submodule merge itself when one side is an
		// ancestor of the other, and a merge it can resolve produces no conflict to read.
		GitBranchName ours = "ours".As<GitBranchName>();
		GitBranchName theirs = "theirs".As<GitBranchName>();

		_ = await checkout.CreateBranch(ours).ExecuteAsync(cancellationToken).ConfigureAwait(false);
		_ = await checkout.CreateBranch(theirs).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		GitCommitSha oursSha = await CommitInSubmoduleAsync(
			checkout, superDirectory, ours, "ours\n", cancellationToken).ConfigureAwait(false);
		GitCommitSha theirsSha = await CommitInSubmoduleAsync(
			checkout, superDirectory, theirs, "theirs\n", cancellationToken).ConfigureAwait(false);

		// A branch of the superproject per side, each recording its own gitlink. "other" is the branch
		// the merge runs on, so its gitlink is the one git stages as stage 2.
		GitBranchName other = "other".As<GitBranchName>();
		_ = await super.CreateBranch(other).ExecuteAsync(cancellationToken).ConfigureAwait(false);
		_ = await super.Checkout("other".As<GitRefName>()).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		await RecordGitlinkAsync(super, checkout, ours, cancellationToken).ConfigureAwait(false);

		_ = await super.Checkout("main".As<GitRefName>()).ExecuteAsync(cancellationToken).ConfigureAwait(false);
		await RecordGitlinkAsync(super, checkout, theirs, cancellationToken).ConfigureAwait(false);

		_ = await super.Checkout("other".As<GitRefName>()).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		// merge is out of scope for this library, so the fixture runs it directly. It is expected to
		// fail: "Recursive merging with submodules currently only supports trivial cases", which is
		// precisely the unmerged index this test needs.
		GitProcessResult merged = await super.ProcessRunner!.RunAsync(
			new GitProcessRequest
			{
				Arguments = ["-C", superDirectory.RootPath, "merge", "--no-edit", "main"],
			},
			cancellationToken).ConfigureAwait(false);

		Assert.IsFalse(merged.Success, "the submodule merge was expected to conflict but succeeded");

		IReadOnlyList<GitSubmodule> submodules =
			await super.Submodules().ExecuteAsync(cancellationToken).ConfigureAwait(false);

		// One entry, not one per merge stage: ls-files emits three records for this path, and the
		// directory they all describe exists once.
		Assert.AreEqual(1, submodules.Count);
		Assert.AreEqual("libs/sub".As<RelativeDirectoryPath>(), submodules[0].Path);
		Assert.AreEqual(GitSubmoduleState.Conflicted, submodules[0].State);

		// Stage 2 — what the branch being merged into records — rather than the merge base or theirs.
		Assert.AreEqual(oursSha, submodules[0].Sha);
		Assert.AreNotEqual(theirsSha, submodules[0].Sha);

		// git prints its null object id here. Reporting it verbatim would present forty zeroes as a
		// commit, which is well-formed enough that nothing downstream would question it.
		Assert.IsNull(submodules[0].CheckedOutSha);
	}

	/// <summary>Commits a change on one of the submodule's branches, and reports the commit.</summary>
	private static async Task<GitCommitSha> CommitInSubmoduleAsync(
		GitRepository checkout,
		TemporaryRepository superDirectory,
		GitBranchName branch,
		string contents,
		CancellationToken cancellationToken)
	{
		_ = await checkout.Checkout(branch.WeakString.As<GitRefName>())
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		superDirectory.WriteFile("libs/sub/s.txt", contents);

		_ = await checkout.Add().All().ExecuteAsync(cancellationToken).ConfigureAwait(false);

		GitCommit commit = await checkout.Commit(branch.WeakString.As<GitCommitMessage>())
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		return commit.Sha;
	}

	/// <summary>Checks a branch out in the submodule and records the result as the superproject's gitlink.</summary>
	private static async Task RecordGitlinkAsync(
		GitRepository super,
		GitRepository checkout,
		GitBranchName branch,
		CancellationToken cancellationToken)
	{
		_ = await checkout.Checkout(branch.WeakString.As<GitRefName>())
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		_ = await super.Add().All().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		_ = await super.Commit($"record {branch.WeakString}".As<GitCommitMessage>())
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task ReportsNoSubmodulesForARepositoryWithNoneAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository temporary = new();
		GitRepository repository = await CreateRepositoryAsync(temporary, "m.txt", cancellationToken).ConfigureAwait(false);

		IReadOnlyList<GitSubmodule> submodules =
			await repository.Submodules().ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.AreEqual(0, submodules.Count);
	}

	public TestContext TestContext { get; set; } = null!;
}
