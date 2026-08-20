// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Threading.Tasks;

using ktsu.Semantics.Strings;

[TestClass]
public class GitRemoteWriteBuilderTests
{
	private static GitRemoteName Origin => "origin".As<GitRemoteName>();

	private static GitRepositoryRemotePath Url =>
		"https://example.com/repo.git".As<GitRepositoryRemotePath>();

	private static string[] Prefix =>
	[
		"-C", TestPaths.Root.WeakString,
		"--no-pager",
		"-c", "core.quotepath=false",
		"-c", "color.ui=false",
		"remote",
	];

	[TestMethod]
	public void BuildsTheRemoteAddVector()
	{
		RecordingGitProcessRunner runner = new();
		GitRemoteAddBuilder builder = new(runner, TestPaths.Root, Origin, Url);

		string[] expectedArguments =
		[
			.. Prefix,
			"add",
			"--end-of-options",
			"origin",
			"https://example.com/repo.git",
		];
		CollectionAssert.AreEqual(expectedArguments, builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void BuildsTheRemoteRemoveVector()
	{
		RecordingGitProcessRunner runner = new();
		GitRemoteRemoveBuilder builder = new(runner, TestPaths.Root, Origin);

		string[] expectedArguments =
		[
			.. Prefix,
			"remove",
			"--end-of-options",
			"origin",
		];
		CollectionAssert.AreEqual(expectedArguments, builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void BuildsTheRemoteSetUrlVector()
	{
		RecordingGitProcessRunner runner = new();
		GitRemoteSetUrlBuilder builder = new(runner, TestPaths.Root, Origin, Url);

		string[] expectedArguments =
		[
			.. Prefix,
			"set-url",
			"--end-of-options",
			"origin",
			"https://example.com/repo.git",
		];
		CollectionAssert.AreEqual(expectedArguments, builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void SetUrlForPushOnlyEmitsThePushFlagBeforeTheMarker()
	{
		RecordingGitProcessRunner runner = new();
		GitRemoteSetUrlBuilder builder = new(runner, TestPaths.Root, Origin, Url);

		_ = builder.ForPushOnly();

		string[] arguments = [.. builder.BuildArguments()];
		int marker = Array.IndexOf(arguments, "--end-of-options");

		Assert.IsTrue(Array.IndexOf(arguments, "--push") < marker);
	}

	[TestMethod]
	public void RemoteAddWithFetchEmitsTheFetchFlag()
	{
		RecordingGitProcessRunner runner = new();
		GitRemoteAddBuilder builder = new(runner, TestPaths.Root, Origin, Url);

		_ = builder.WithFetch();

		CollectionAssert.Contains(builder.BuildArguments().ToArray(), "-f");
	}

	[TestMethod]
	public void NameComesBeforeUrl()
	{
		// git remote add <name> <url> is positional, and reversing them produces a remote named
		// after the URL pointing at a path named after the remote.
		RecordingGitProcessRunner runner = new();
		GitRemoteAddBuilder builder = new(runner, TestPaths.Root, Origin, Url);

		string[] arguments = [.. builder.BuildArguments()];
		int marker = Array.IndexOf(arguments, "--end-of-options");

		Assert.AreEqual("origin", arguments[marker + 1]);
		Assert.AreEqual("https://example.com/repo.git", arguments[marker + 2]);
	}

	[TestMethod]
	public void RejectsNullArguments()
	{
		RecordingGitProcessRunner runner = new();

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new GitRemoteAddBuilder(runner, TestPaths.Root, null!, Url));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new GitRemoteAddBuilder(runner, TestPaths.Root, Origin, null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new GitRemoteRemoveBuilder(runner, TestPaths.Root, null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new GitRemoteSetUrlBuilder(runner, TestPaths.Root, null!, Url));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new GitRemoteSetUrlBuilder(runner, TestPaths.Root, Origin, null!));
	}

	[TestMethod]
	public async Task RemoteAddReportsADuplicateAsExitCodeThreeAsync()
	{
		// Captured from git 2.50. The remote commands use exit codes 2 and 3, unlike the 1 and 128
		// seen elsewhere, which is why nothing in this library keys off a particular code.
		RecordingGitProcessRunner runner = new()
		{
			ExitCode = 3,
			StandardError = "error: remote origin already exists.\n",
		};
		GitRemoteAddBuilder builder = new(runner, TestPaths.Root, Origin, Url);

		GitResult<GitCompleted> result =
			await builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsFalse(result.Success);
		Assert.AreEqual(3, result.Error?.ExitCode);
	}

	[TestMethod]
	public async Task RemoteRemoveReportsAMissingRemoteAsExitCodeTwoAsync()
	{
		RecordingGitProcessRunner runner = new()
		{
			ExitCode = 2,
			StandardError = "error: No such remote: 'origin'\n",
		};
		GitRemoteRemoveBuilder builder = new(runner, TestPaths.Root, Origin);

		GitResult<GitCompleted> result =
			await builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsFalse(result.Success);
		Assert.AreEqual(2, result.Error?.ExitCode);
	}

	public TestContext TestContext { get; set; } = null!;
}
