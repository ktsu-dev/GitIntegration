// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Exercises <c>Init(path).Bare()</c> against a real git binary, on targets that already hold a
/// repository.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class GitBareInitTests
{
	[TestMethod]
	public async Task BareInitOnAWorkingTreeRootFailsAndWritesNothingAsync()
	{
		// git init --bare on an existing working-tree root does not convert it: it writes a second,
		// bare repository's HEAD, config, objects/ and the rest into the working tree and exits 0.
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository temporary = new();
		GitInitResult init = await IntegrationGitFixture.CreateClient()
			.Init(temporary.Root)
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);
		Assert.IsFalse(init.AlreadyExisted);
		temporary.WriteFile("readme", "x\n");
		string[] before = [.. Directory.GetFileSystemEntries(temporary.RootPath).Order(StringComparer.Ordinal)];

		GitCommandException exception = await Assert.ThrowsExactlyAsync<GitCommandException>(
			async () => await IntegrationGitFixture.CreateClient()
				.Init(temporary.Root)
				.Bare()
				.ExecuteAsync(cancellationToken).ConfigureAwait(false))
			.ConfigureAwait(false);
		Assert.AreEqual(-1, exception.ExitCode);

		GitResult<GitInitResult> result = await IntegrationGitFixture.CreateClient()
			.Init(temporary.Root)
			.Bare()
			.TryExecuteAsync(cancellationToken).ConfigureAwait(false);
		Assert.IsFalse(result.Success);
		Assert.AreEqual(-1, result.Error?.ExitCode);

		string[] after = [.. Directory.GetFileSystemEntries(temporary.RootPath).Order(StringComparer.Ordinal)];
		CollectionAssert.AreEqual(before, after);
	}

	[TestMethod]
	public async Task BareInitOnAnExistingBareRepositoryReinitializesItAsync()
	{
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository temporary = new();
		GitClient client = IntegrationGitFixture.CreateClient();

		GitInitResult first = await client.Init(temporary.Root).Bare().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		GitInitResult second = await client.Init(temporary.Root).Bare().ExecuteAsync(cancellationToken).ConfigureAwait(false);

		Assert.IsFalse(first.AlreadyExisted);
		Assert.IsTrue(second.AlreadyExisted);
	}

	public TestContext TestContext { get; set; } = null!;
}
