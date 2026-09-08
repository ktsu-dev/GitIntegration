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
			LocalPath = System.IO.Path.Combine(superDirectory.RootPath, "libs", "sub").As<AbsoluteDirectoryPath>(),
			ProcessRunner = super.ProcessRunner,
		};

		await IntegrationGitFixture.ConfigureIdentityAsync(checkout, AuthorName, AuthorEmail, cancellationToken)
			.ConfigureAwait(false);

		superDirectory.WriteFile(System.IO.Path.Combine("libs", "sub", "s.txt"), "two\n");
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
	public async Task ReportsAnUninitialisedSubmoduleAndThenUpdatesItAsync()
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
		Assert.AreEqual(GitSubmoduleState.Uninitialised, before[0].State);

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
					.. clone.UpdateSubmodules().Initialise().Recursive().BuildArguments(),
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
