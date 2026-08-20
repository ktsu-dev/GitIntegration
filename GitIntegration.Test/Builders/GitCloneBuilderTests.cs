// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

using Testably.Abstractions.Testing;

[TestClass]
public class GitCloneBuilderTests
{
	private static GitRepositoryRemotePath Source =>
		"https://example.com/repo.git".As<GitRepositoryRemotePath>();

	private static AbsoluteDirectoryPath Destination =>
		(OperatingSystem.IsWindows() ? @"C:\dev\clone" : "/dev/clone").As<AbsoluteDirectoryPath>();

	private static FakeFileSystemProvider EmptyFileSystem() => new(new MockFileSystem());

	[TestMethod]
	public void BuildsTheDefaultCloneVectorWithoutRepositoryScoping()
	{
		RecordingGitProcessRunner runner = new();
		GitCloneBuilder builder = new(runner, EmptyFileSystem(), Source, Destination);

		string[] expectedArguments =
		[
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"clone",
			"--end-of-options",
			"https://example.com/repo.git",
			Destination.WeakString,
		];
		CollectionAssert.AreEqual(expectedArguments, builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void SourceComesBeforeDestination()
	{
		// git clone <source> <destination> is positional, and reversing them tries to clone the
		// destination path into a directory named after the URL.
		RecordingGitProcessRunner runner = new();
		GitCloneBuilder builder = new(runner, EmptyFileSystem(), Source, Destination);

		string[] arguments = [.. builder.BuildArguments()];
		int marker = Array.IndexOf(arguments, "--end-of-options");

		Assert.AreEqual("https://example.com/repo.git", arguments[marker + 1]);
		Assert.AreEqual(Destination.WeakString, arguments[marker + 2]);
	}

	[TestMethod]
	public void MapsTheOptionFlagsBeforeTheMarker()
	{
		RecordingGitProcessRunner runner = new();
		GitCloneBuilder builder = new(runner, EmptyFileSystem(), Source, Destination);

		_ = builder.WithBranch("main".As<GitBranchName>()).WithDepth(1).Bare();

		string[] arguments = [.. builder.BuildArguments()];
		int marker = Array.IndexOf(arguments, "--end-of-options");

		Assert.IsTrue(Array.IndexOf(arguments, "--branch") < marker);
		Assert.IsTrue(Array.IndexOf(arguments, "main") < marker);
		Assert.IsTrue(Array.IndexOf(arguments, "--depth=1") < marker);
		Assert.IsTrue(Array.IndexOf(arguments, "--bare") < marker);
	}

	[TestMethod]
	public void RejectsANonPositiveDepth()
	{
		RecordingGitProcessRunner runner = new();
		GitCloneBuilder builder = new(runner, EmptyFileSystem(), Source, Destination);

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = builder.WithDepth(0));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = builder.WithDepth(-1));
	}

	[TestMethod]
	public void RejectsNullArguments()
	{
		RecordingGitProcessRunner runner = new();
		FakeFileSystemProvider fileSystem = EmptyFileSystem();

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new GitCloneBuilder(runner, null!, Source, Destination));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new GitCloneBuilder(runner, fileSystem, null!, Destination));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new GitCloneBuilder(runner, fileSystem, Source, null!));

		GitCloneBuilder builder = new(runner, fileSystem, Source, Destination);
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.WithBranch(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.ReportingProgress(null!));
	}

	[TestMethod]
	public async Task ClonesIntoAMissingDestinationAsync()
	{
		RecordingGitProcessRunner runner = new()
		{
			StandardError = "Cloning into '/dev/clone'...\ndone.\n",
		};
		GitCloneBuilder builder = new(runner, EmptyFileSystem(), Source, Destination);

		GitRepository repository = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(Destination, repository.LocalPath);
		Assert.AreEqual(Source, repository.RemotePath);
		Assert.IsNotNull(repository.ProcessRunner);
	}

	[TestMethod]
	public async Task ClonesIntoAnExistingButEmptyDestinationAsync()
	{
		// git accepts an existing empty directory, so the pre-check must too.
		MockFileSystem mock = new();
		_ = mock.Directory.CreateDirectory(Destination.WeakString);

		RecordingGitProcessRunner runner = new();
		GitCloneBuilder builder = new(runner, new FakeFileSystemProvider(mock), Source, Destination);

		GitRepository repository = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(Destination, repository.LocalPath);
	}

	[TestMethod]
	public async Task RefusesANonEmptyDestinationBeforeRunningGitAsync()
	{
		// The whole point of the pre-check: a doomed clone should not pay its network cost first.
		MockFileSystem mock = new();
		_ = mock.Directory.CreateDirectory(Destination.WeakString);
		await mock.File.WriteAllTextAsync(
			mock.Path.Combine(Destination.WeakString, "existing.txt"), "x", TestContext.CancellationTokenSource.Token)
			.ConfigureAwait(false);

		RecordingGitProcessRunner runner = new();
		GitCloneBuilder builder = new(runner, new FakeFileSystemProvider(mock), Source, Destination);

		GitCommandException exception = await Assert.ThrowsExactlyAsync<GitCommandException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		StringAssert.Contains(exception.Message, Destination.WeakString);
		Assert.IsNull(runner.LastRequest);
	}

	[TestMethod]
	public async Task TryExecuteReportsANonEmptyDestinationAsAResultAsync()
	{
		MockFileSystem mock = new();
		_ = mock.Directory.CreateDirectory(Destination.WeakString);
		await mock.File.WriteAllTextAsync(
			mock.Path.Combine(Destination.WeakString, "existing.txt"), "x", TestContext.CancellationTokenSource.Token)
			.ConfigureAwait(false);

		RecordingGitProcessRunner runner = new();
		GitCloneBuilder builder = new(runner, new FakeFileSystemProvider(mock), Source, Destination);

		GitResult<GitRepository> result =
			await builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsFalse(result.Success);
		Assert.IsNull(runner.LastRequest);
	}

	[TestMethod]
	public async Task ForwardsProgressToTheRequestAsync()
	{
		// git clone writes its entire progress stream to standard error, so a caller that wants to
		// watch a long clone needs the sink wired through to the request. The assertion is on the
		// request rather than on what the sink received: Progress<T> marshals its callback through the
		// synchronization context, so a received-chunks assertion would race the report.
		RecordingGitProcessRunner runner = new() { StandardError = "Receiving objects: 100%\n" };
		GitCloneBuilder builder = new(runner, EmptyFileSystem(), Source, Destination);

		_ = builder.ReportingProgress(new Progress<string>(static _ => { }));
		_ = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsNotNull(runner.LastRequest);
		Assert.IsNotNull(runner.LastRequest.Progress);
	}

	[TestMethod]
	public async Task ThrowsWhenGitRefusesTheCloneAsync()
	{
		RecordingGitProcessRunner runner = new()
		{
			ExitCode = 128,
			StandardError = "fatal: repository 'https://example.com/repo.git' does not exist\n",
		};
		GitCloneBuilder builder = new(runner, EmptyFileSystem(), Source, Destination);

		GitCommandException exception = await Assert.ThrowsExactlyAsync<GitCommandException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.AreEqual(128, exception.ExitCode);
	}

	public TestContext TestContext { get; set; } = null!;
}
