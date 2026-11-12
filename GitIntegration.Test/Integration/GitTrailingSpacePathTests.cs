// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

/// <summary>
/// Exercises a repository whose directory name ends in a space against a real git binary (#277).
/// </summary>
/// <remarks>
/// POSIX only: Windows does not allow a file or directory name to end in a space.
/// </remarks>
[TestClass]
[TestCategory("Integration")]
public class GitTrailingSpacePathTests
{
	private static void RequirePosix()
	{
		if (OperatingSystem.IsWindows())
		{
			Assert.Inconclusive("Windows file names cannot end in a space.");
		}
	}

	private static async Task<AbsoluteDirectoryPath> InitAsync(string path, CancellationToken cancellationToken)
	{
		_ = Directory.CreateDirectory(path);
		AbsoluteDirectoryPath root = path.As<AbsoluteDirectoryPath>();
		_ = await IntegrationGitFixture.CreateClient().Init(root).ExecuteAsync(cancellationToken).ConfigureAwait(false);
		return root;
	}

	[TestMethod]
	public async Task OpenKeepsATrailingSpaceInTheRootAsync()
	{
		RequirePosix();
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository parent = new();
		string spaced = Path.Join(parent.RootPath, "repo ");
		_ = await InitAsync(spaced, cancellationToken).ConfigureAwait(false);

		GitRepository repository = await IntegrationGitFixture.CreateClient()
			.OpenAsync(spaced.As<AbsoluteDirectoryPath>(), cancellationToken).ConfigureAwait(false);

		Assert.IsNotNull(repository.LocalPath);
		Assert.EndsWith("repo ", repository.LocalPath.WeakString);
		_ = await repository.Status().ExecuteAsync(cancellationToken).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task OpenDoesNotReturnASiblingWithoutTheTrailingSpaceAsync()
	{
		RequirePosix();
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository parent = new();
		string spaced = Path.Join(parent.RootPath, "repo ");
		_ = await InitAsync(spaced, cancellationToken).ConfigureAwait(false);
		AbsoluteDirectoryPath sibling = await InitAsync(Path.Join(parent.RootPath, "repo"), cancellationToken).ConfigureAwait(false);
		await File.WriteAllTextAsync(Path.Join(sibling.WeakString, "sibling.txt"), "x\n", cancellationToken).ConfigureAwait(false);

		GitRepository repository = await IntegrationGitFixture.CreateClient()
			.OpenAsync(spaced.As<AbsoluteDirectoryPath>(), cancellationToken).ConfigureAwait(false);

		Assert.AreNotEqual(sibling, repository.LocalPath);
		GitStatus status = await repository.Status().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		Assert.IsEmpty(status.Entries);
	}

	[TestMethod]
	public async Task OpenKeepsATrailingSpaceInALocalOriginAsync()
	{
		RequirePosix();
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository parent = new();
		using TemporaryRepository working = new();
		string remote = Path.Join(parent.RootPath, "remote ");
		_ = await InitAsync(remote, cancellationToken).ConfigureAwait(false);
		GitInitResult init = await IntegrationGitFixture.CreateClient().Init(working.Root).ExecuteAsync(cancellationToken).ConfigureAwait(false);
		_ = await new GitTextBuilder(init.Repository.ProcessRunner!, working.Root, "remote", "add", "origin", remote)
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		GitRepository repository = await IntegrationGitFixture.CreateClient()
			.OpenAsync(working.Root, cancellationToken).ConfigureAwait(false);

		Assert.AreEqual(remote, repository.RemotePath?.WeakString);
	}

	private static async Task<AbsoluteDirectoryPath> InitBareAsync(string path, CancellationToken cancellationToken)
	{
		_ = Directory.CreateDirectory(path);
		AbsoluteDirectoryPath root = path.As<AbsoluteDirectoryPath>();
		_ = await IntegrationGitFixture.CreateClient().Init(root).Bare().ExecuteAsync(cancellationToken).ConfigureAwait(false);
		return root;
	}

	[TestMethod]
	public async Task OpenKeepsATrailingSpaceInABareRepositoryAsync()
	{
		RequirePosix();
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository parent = new();
		string spaced = Path.Join(parent.RootPath, "bare ");
		_ = await InitBareAsync(spaced, cancellationToken).ConfigureAwait(false);

		GitRepository repository = await IntegrationGitFixture.CreateClient()
			.OpenAsync(spaced.As<AbsoluteDirectoryPath>(), cancellationToken).ConfigureAwait(false);

		Assert.AreEqual(spaced.As<AbsoluteDirectoryPath>(), repository.LocalPath);
	}

	[TestMethod]
	public async Task OpenAndDiscoverDoNotReturnABareSiblingWithoutTheTrailingSpaceAsync()
	{
		RequirePosix();
		CancellationToken cancellationToken = TestContext.CancellationTokenSource.Token;
		await IntegrationGitFixture.RequireGitAsync(cancellationToken).ConfigureAwait(false);

		using TemporaryRepository parent = new();
		string spaced = Path.Join(parent.RootPath, "bare ");
		AbsoluteDirectoryPath expected = await InitBareAsync(spaced, cancellationToken).ConfigureAwait(false);
		AbsoluteDirectoryPath sibling = await InitBareAsync(Path.Join(parent.RootPath, "bare"), cancellationToken).ConfigureAwait(false);

		GitClient client = IntegrationGitFixture.CreateClient();
		GitRepository opened = await client.OpenAsync(expected, cancellationToken).ConfigureAwait(false);
		GitRepository? discovered = await client
			.DiscoverAsync(Path.Join(spaced, "refs").As<AbsoluteDirectoryPath>(), cancellationToken).ConfigureAwait(false);

		Assert.AreNotEqual(sibling, opened.LocalPath);
		Assert.AreEqual(expected, opened.LocalPath);
		Assert.IsNotNull(discovered);
		Assert.AreEqual(expected, discovered.LocalPath);
	}

	public TestContext TestContext { get; set; } = null!;
}
