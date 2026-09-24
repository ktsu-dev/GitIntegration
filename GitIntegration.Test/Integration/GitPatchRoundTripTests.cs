// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Collections.Generic;
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

		IReadOnlyList<GitDiffEntry> staged = await opened.Diff().Staged().ExecuteAsync().ConfigureAwait(false);

		Assert.AreEqual(0, staged.Count, "--check must change nothing.");
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

	public TestContext TestContext { get; set; } = null!;
}
