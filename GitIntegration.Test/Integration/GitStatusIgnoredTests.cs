// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Strings;

/// <summary>
/// Checks what <see cref="IGitStatusBuilder.IncludeIgnored"/> reports, against a real git binary.
/// </summary>
/// <remarks>
/// The builder emits <c>--ignored=matching</c>, whose output depends on the ignore rules rather than
/// on the untracked mode, so the documented shape is pinned here (ktsu-dev/GitIntegration#295).
/// </remarks>
[TestClass]
[TestCategory("Integration")]
public class GitStatusIgnoredTests
{
	private static readonly GitAuthorName AuthorName = "Fixture Author".As<GitAuthorName>();
	private static readonly GitAuthorEmail AuthorEmail = "fixture@example.com".As<GitAuthorEmail>();
	private static readonly string[] ExpectedIgnored = ["build", "src/debug.log"];

	[TestMethod]
	[DataRow(GitUntrackedFilesMode.Normal)]
	[DataRow(GitUntrackedFilesMode.All)]
	public async Task IncludeIgnoredReportsAnIgnoredDirectoryOnceAsync(GitUntrackedFilesMode mode)
	{
		// A directory the ignore rules name is one entry, however many files it holds, under All as
		// well as Normal. A file ignored by a pattern that names files is still listed on its own.
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository temporary = new();
		GitClient client = IntegrationGitFixture.CreateClient();
		GitInitResult init = await client
			.Init(temporary.Root)
			.WithInitialBranch("main".As<GitBranchName>())
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);
		GitRepository repository = init.Repository;
		await IntegrationGitFixture.ConfigureIdentityAsync(repository, AuthorName, AuthorEmail, cancellationToken)
			.ConfigureAwait(false);

		temporary.WriteFile(".gitignore", "build/\n*.log\n");
		_ = await repository.Add().All().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		_ = await repository.Commit("c1".As<GitCommitMessage>()).ExecuteAsync(cancellationToken).ConfigureAwait(false);

		temporary.WriteFile("build/a", "a\n");
		temporary.WriteFile("build/sub/b", "b\n");
		temporary.WriteFile("src/debug.log", "log\n");

		GitStatus status = await repository.Status()
			.WithUntrackedFiles(mode)
			.IncludeIgnored()
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		string[] ignored = [.. status.Entries
			.Where(entry => entry.WorkTreeState == GitFileState.Ignored)
			.Select(entry => entry.Path.WeakString.Replace('\\', '/'))
			.Order(StringComparer.Ordinal)];

		CollectionAssert.AreEqual(ExpectedIgnored, ignored, $"Ignored entries under {mode}: {string.Join(", ", ignored)}");
	}

	public TestContext TestContext { get; set; } = null!;
}
