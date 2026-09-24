// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using ktsu.Semantics.Strings;

/// <summary>
/// Exercises <c>Apply</c> against a real git binary: the round trip that proves the model, the
/// parser, and the builder kept every byte of a patch between reading it and staging it back.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class GitPatchRoundTripTests
{
	private static readonly GitAuthorName AuthorName = "Fixture Author".As<GitAuthorName>();
	private static readonly GitAuthorEmail AuthorEmail = "fixture@example.com".As<GitAuthorEmail>();

	[TestMethod]
	public async Task StagingOneHunkLeavesTheOtherUnstagedAsync()
	{
		await IntegrationGitFixture.RequireGitAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		using TemporaryRepository repository = new();
		GitClient client = IntegrationGitFixture.CreateClient();

		// Seed a ten-line file, commit it, then change the second and the tenth line so the diff
		// has two separate hunks.
		GitInitResult init = await client.Init(repository.Root)
			.WithInitialBranch("main".As<GitBranchName>())
			.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);
		await IntegrationGitFixture.ConfigureIdentityAsync(
			init.Repository, AuthorName, AuthorEmail, TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		string[] lines = [.. Enumerable.Range(1, 10).Select(number => $"line {number}")];
		repository.WriteFile("f.txt", string.Join('\n', lines) + "\n");
		_ = await init.Repository.Add().All()
			.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);
		_ = await init.Repository.Commit("seed".As<GitCommitMessage>())
			.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		lines[1] = "line 2 changed";
		lines[9] = "line 10 changed";
		repository.WriteFile("f.txt", string.Join('\n', lines) + "\n");

		GitRepository opened = await client.OpenAsync(repository.Root).ConfigureAwait(false);

		GitPatch patch = await opened.Patch().WithContext(1).ExecuteAsync().ConfigureAwait(false);
		GitFilePatch file = patch.Files.Single();

		Assert.AreEqual(2, file.Hunks.Count, "The fixture must produce two hunks, or this proves nothing.");

		_ = await opened.Apply(file.PatchFor([file.Hunks[0]])).ToIndex().ExecuteAsync().ConfigureAwait(false);

		IReadOnlyList<GitDiffEntry> staged = await opened.Diff().Staged().WithLineCounts()
			.ExecuteAsync().ConfigureAwait(false);
		IReadOnlyList<GitDiffEntry> unstaged = await opened.Diff().WithLineCounts()
			.ExecuteAsync().ConfigureAwait(false);

		Assert.AreEqual(1, staged.Single().Insertions);
		Assert.AreEqual(1, unstaged.Single().Insertions);
	}

	[TestMethod]
	public async Task CheckedReportsFailureWhenTheWorkingTreeMovedAsync()
	{
		await IntegrationGitFixture.RequireGitAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		using TemporaryRepository repository = new();
		GitClient client = IntegrationGitFixture.CreateClient();

		// Seed and commit a file, then change it without staging the change.
		GitInitResult init = await client.Init(repository.Root)
			.WithInitialBranch("main".As<GitBranchName>())
			.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);
		await IntegrationGitFixture.ConfigureIdentityAsync(
			init.Repository, AuthorName, AuthorEmail, TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		repository.WriteFile("f.txt", "one\ntwo\nthree\n");
		_ = await init.Repository.Add().All()
			.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);
		_ = await init.Repository.Commit("seed".As<GitCommitMessage>())
			.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		repository.WriteFile("f.txt", "one\nCHANGED\nthree\n");

		GitRepository opened = await client.OpenAsync(repository.Root).ConfigureAwait(false);
		GitFilePatch file = (await opened.Patch().ExecuteAsync().ConfigureAwait(false)).Files.Single();
		string text = file.PatchFor(file.Hunks);

		repository.WriteFile("f.txt", "something else entirely\n");

		// Not ToIndex(): --cached checks only the index entry, which nothing here has touched
		// since the patch was read, so it would always apply cleanly and prove nothing. Checking
		// the default target is what actually sees the working tree that moved.
		GitResult<GitCompleted> result = await opened.Apply(text).Checked()
			.TryExecuteAsync().ConfigureAwait(false);

		Assert.IsFalse(result.Success, "A caller's refusal depends on this reporting failure rather than throwing.");

		string workingTree = await File.ReadAllTextAsync(
			Path.Combine(repository.RootPath, "f.txt"),
			TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(
			"something else entirely\n",
			workingTree,
			"--check must change nothing: the working tree is what it reads, so it is what --check could have disturbed.");
	}

	[TestMethod]
	public async Task CheckedReportsFailureWhenTheIndexMovedAsync()
	{
		await IntegrationGitFixture.RequireGitAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		using TemporaryRepository repository = new();
		GitClient client = IntegrationGitFixture.CreateClient();

		// Seed and commit a file, then change it without staging the change.
		GitInitResult init = await client.Init(repository.Root)
			.WithInitialBranch("main".As<GitBranchName>())
			.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);
		await IntegrationGitFixture.ConfigureIdentityAsync(
			init.Repository, AuthorName, AuthorEmail, TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		repository.WriteFile("f.txt", "one\ntwo\nthree\n");
		_ = await init.Repository.Add().All()
			.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);
		_ = await init.Repository.Commit("seed".As<GitCommitMessage>())
			.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		repository.WriteFile("f.txt", "one\nCHANGED\nthree\n");

		GitRepository opened = await client.OpenAsync(repository.Root).ConfigureAwait(false);
		GitFilePatch file = (await opened.Patch().ExecuteAsync().ConfigureAwait(false)).Files.Single();
		string text = file.PatchFor(file.Hunks);

		// Stage a different edit to the same line, so the index no longer holds the content the
		// patch's preimage expects. --cached never reads the working tree, so only moving the
		// index this way can make ToIndex().Checked() refuse.
		repository.WriteFile("f.txt", "one\nDIFFERENT\nthree\n");
		_ = await opened.Add().All().ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		GitResult<GitCompleted> result = await opened.Apply(text).ToIndex().Checked()
			.TryExecuteAsync().ConfigureAwait(false);

		Assert.IsFalse(result.Success, "A caller's refusal depends on this reporting failure rather than throwing.");

		string indexContent = await new GitTextBuilder(opened.ProcessRunner!, opened.LocalPath, "show", ":f.txt")
			.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(
			"one\nDIFFERENT\nthree",
			indexContent,
			"--check must change nothing: the index should still hold what was staged, not the patch's content.");
	}

	[TestMethod]
	public async Task CarriesCarriageReturnsThroughTheRealRunnerAsync()
	{
		await IntegrationGitFixture.RequireGitAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		using TemporaryRepository repository = new();
		GitClient client = IntegrationGitFixture.CreateClient();

		GitRepository seeded = await SeedAsync(
			client,
			repository,
			[("core.autocrlf", "false"), ("core.eol", "lf")]).ConfigureAwait(false);

		repository.WriteFile("f.txt", "one\r\ntwo\r\nthree\r\n");
		await CommitAllAsync(seeded).ConfigureAwait(false);

		repository.WriteFile("f.txt", "one\r\nTWO\r\nthree\r\n");

		GitRepository opened = await client.OpenAsync(repository.Root).ConfigureAwait(false);
		GitFilePatch file = (await opened.Patch().ExecuteAsync().ConfigureAwait(false)).Files.Single();

		StringAssert.Contains(
			file.Hunks.Single().Text,
			"\r",
			StringComparison.Ordinal,
			"The fixture tier proves the parser keeps carriage returns. Only the real runner proves the process boundary does.");

		_ = await opened.Apply(file.PatchFor(file.Hunks)).ToIndex().ExecuteAsync().ConfigureAwait(false);

		IReadOnlyList<GitDiffEntry> unstaged = await opened.Diff().ExecuteAsync().ConfigureAwait(false);

		Assert.AreEqual(
			0,
			unstaged.Count,
			"The index now matches the working tree byte for byte. A carriage return lost anywhere between reading the patch and staging it would leave a difference here.");
	}

	[TestMethod]
	public async Task RoundTripsUnderHostileDiffConfigurationAsync()
	{
		await IntegrationGitFixture.RequireGitAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		using TemporaryRepository repository = new();
		GitClient client = IntegrationGitFixture.CreateClient();

		// Each of these three rewrites the diff into a shape a reader that trusts git's defaults
		// cannot handle: two change the a/ and b/ path prefixes the header is found by, and the
		// third prints an empty context line as a bare newline that ends the hunk body early.
		GitRepository seeded = await SeedAsync(
			client,
			repository,
			[
				("diff.noprefix", "true"),
				("diff.mnemonicPrefix", "true"),
				("diff.suppressBlankEmpty", "true"),
			]).ConfigureAwait(false);

		repository.WriteFile("f.txt", "one\n\ntwo\nthree\nfour\n");
		await CommitAllAsync(seeded).ConfigureAwait(false);

		repository.WriteFile("f.txt", "one\n\ntwo\nthree\nFOUR\n");

		GitRepository opened = await client.OpenAsync(repository.Root).ConfigureAwait(false);
		GitFilePatch file = (await opened.Patch().ExecuteAsync().ConfigureAwait(false)).Files.Single();

		Assert.AreEqual("f.txt", file.Path.WeakString);
		StringAssert.Contains(
			file.Hunks.Single().Text,
			"FOUR",
			StringComparison.Ordinal,
			"A hunk truncated at the blank context line would stop before the change itself.");

		_ = await opened.Apply(file.PatchFor(file.Hunks)).ToIndex().ExecuteAsync().ConfigureAwait(false);

		IReadOnlyList<GitDiffEntry> unstaged = await opened.Diff().ExecuteAsync().ConfigureAwait(false);

		Assert.AreEqual(
			0,
			unstaged.Count,
			"The patch read under this configuration has to apply back cleanly, or the verbs work only for users whose git is configured the way the tests assume.");
	}

	/// <summary>
	/// Creates an empty repository with the fixture identity and any extra configuration a test
	/// needs, before anything is committed.
	/// </summary>
	/// <param name="client">The client to initialize through.</param>
	/// <param name="repository">The throwaway directory to initialize in.</param>
	/// <param name="configuration">Extra config keys and values to pin locally.</param>
	/// <returns>The initialized repository.</returns>
	private async Task<GitRepository> SeedAsync(
		GitClient client,
		TemporaryRepository repository,
		IEnumerable<(string Key, string Value)> configuration)
	{
		GitInitResult init = await client.Init(repository.Root)
			.WithInitialBranch("main".As<GitBranchName>())
			.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		await IntegrationGitFixture.ConfigureIdentityAsync(
			init.Repository, AuthorName, AuthorEmail, TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		foreach ((string key, string value) in configuration)
		{
			_ = await new GitTextBuilder(init.Repository.ProcessRunner!, init.Repository.LocalPath, "config", key, value)
				.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);
		}

		return init.Repository;
	}

	/// <summary>Stages everything in the working tree and commits it.</summary>
	/// <param name="repository">The repository to commit in.</param>
	private async Task CommitAllAsync(GitRepository repository)
	{
		_ = await repository.Add().All()
			.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);
		_ = await repository.Commit("seed".As<GitCommitMessage>())
			.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);
	}

	public TestContext TestContext { get; set; } = null!;
}
