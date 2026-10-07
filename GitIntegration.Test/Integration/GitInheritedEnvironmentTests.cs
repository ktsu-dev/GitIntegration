// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

/// <summary>
/// Runs the verbs with git's own environment variables set in the calling process, as they are
/// inside a git hook. <c>GIT_DIR</c> and <c>GIT_INDEX_FILE</c> used to beat <c>-C</c>, so every
/// verb read the hook's repository, and <c>GIT_DIFF_OPTS</c> beat the pinned <c>-U</c>, so
/// <c>Patch()</c> could return zero-context hunks (ktsu-dev/GitIntegration#139).
/// </summary>
/// <remarks>
/// The variables are set on this test process, which every other test shares, so the class must not
/// run alongside them.
/// </remarks>
[TestClass]
[TestCategory("Integration")]
[DoNotParallelize]
public class GitInheritedEnvironmentTests
{
	private static readonly GitAuthorName AuthorName = "Fixture Author".As<GitAuthorName>();
	private static readonly GitAuthorEmail AuthorEmail = "fixture@example.com".As<GitAuthorEmail>();

	[TestMethod]
	public async Task InheritedGitDirAndIndexFileDoNotRedirectTheVerbsAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository other = new();
		using TemporaryRepository target = new();
		GitClient client = IntegrationGitFixture.CreateClient();

		_ = await SeedAsync(client, other, "A.txt", "a\n", cancellationToken).ConfigureAwait(false);
		_ = await SeedAsync(client, target, "B.txt", "b\n", cancellationToken).ConfigureAwait(false);
		target.WriteFile("B.txt", "b changed\n");

		string otherGitDir = Path.Join(other.RootPath, ".git");

		using (new EnvironmentScope(new()
		{
			["GIT_DIR"] = otherGitDir,
			["GIT_INDEX_FILE"] = Path.Join(otherGitDir, "index"),
		}))
		{
			GitRepository opened = await client.OpenAsync(target.Root).ConfigureAwait(false);

			GitStatus status = await opened.Status().ExecuteAsync(cancellationToken).ConfigureAwait(false);
			GitPatch patch = await opened.Patch().ExecuteAsync(cancellationToken).ConfigureAwait(false);

			GitStatusEntry entry = status.Entries.Single();
			Assert.AreEqual("B.txt", entry.Path.ToString(), "Status must describe the repository that was opened, not the one GIT_DIR names");
			Assert.AreEqual(GitFileState.Modified, entry.WorkTreeState);
			Assert.AreEqual("B.txt", patch.Files.Single().Path.ToString(), "Patch must describe the repository that was opened, not the one GIT_DIR names");
		}
	}

	[TestMethod]
	public async Task InheritedGitDiffOptsDoesNotStripPatchContextAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository repository = new();
		GitClient client = IntegrationGitFixture.CreateClient();

		_ = await SeedAsync(client, repository, "n.txt", "1\n2\n3\n4\n5\n", cancellationToken).ConfigureAwait(false);
		repository.WriteFile("n.txt", "1\n2\nX\n4\n5\n");

		using (new EnvironmentScope(new() { ["GIT_DIFF_OPTS"] = "-u0" }))
		{
			GitRepository opened = await client.OpenAsync(repository.Root).ConfigureAwait(false);

			GitFilePatch file = (await opened.Patch().WithContext(3)
				.ForPath("n.txt".As<RelativeFilePath>())
				.ExecuteAsync(cancellationToken).ConfigureAwait(false)).Files.Single();
			GitHunk hunk = file.Hunks.Single();

			Assert.AreEqual(1, hunk.OldStart, "The hunk should carry the context WithContext(3) asked for, not GIT_DIFF_OPTS's zero lines");
			Assert.AreEqual(5, hunk.OldCount);

			GitResult<GitCompleted> applied = await opened.Apply(file.PatchFor(file.Hunks)).ToIndex()
				.TryExecuteAsync(cancellationToken).ConfigureAwait(false);

			Assert.IsTrue(applied.Success, "A patch read with context should stage cleanly");
		}
	}

	private static async Task<GitRepository> SeedAsync(
		GitClient client, TemporaryRepository repository, string fileName, string contents, CancellationToken cancellationToken)
	{
		GitInitResult init = await client.Init(repository.Root)
			.WithInitialBranch("main".As<GitBranchName>())
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);
		await IntegrationGitFixture.ConfigureIdentityAsync(
			init.Repository, AuthorName, AuthorEmail, cancellationToken).ConfigureAwait(false);

		repository.WriteFile(fileName, contents);
		_ = await init.Repository.Add().All().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		_ = await init.Repository.Commit("seed".As<GitCommitMessage>()).ExecuteAsync(cancellationToken).ConfigureAwait(false);
		return init.Repository;
	}

	/// <summary>Sets environment variables on this process and restores their previous values on dispose.</summary>
	private sealed class EnvironmentScope : IDisposable
	{
		private readonly Dictionary<string, string?> _previous = new(StringComparer.Ordinal);

		public EnvironmentScope(Dictionary<string, string> values)
		{
			foreach ((string name, string value) in values)
			{
				_previous[name] = Environment.GetEnvironmentVariable(name);
				Environment.SetEnvironmentVariable(name, value);
			}
		}

		public void Dispose()
		{
			foreach ((string name, string? value) in _previous)
			{
				Environment.SetEnvironmentVariable(name, value);
			}
		}
	}

	public TestContext TestContext { get; set; } = null!;
}
