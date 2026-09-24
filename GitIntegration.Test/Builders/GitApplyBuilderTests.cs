// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.IO;
using System.Threading.Tasks;

[TestClass]
public class GitApplyBuilderTests
{
	[TestMethod]
	public void MapsTheOptionFlags()
	{
		RecordingGitProcessRunner runner = new();

		GitApplyBuilder index = new(runner, TestPaths.Root, "patch");
		_ = index.ToIndex();
		Assert.IsTrue(index.BuildArguments().Contains("--cached"));

		GitApplyBuilder reversed = new(runner, TestPaths.Root, "patch");
		_ = reversed.Reversed();
		Assert.IsTrue(reversed.BuildArguments().Contains("--reverse"));

		GitApplyBuilder checkOnly = new(runner, TestPaths.Root, "patch");
		_ = checkOnly.Checked();
		Assert.IsTrue(checkOnly.BuildArguments().Contains("--check"));
	}

	[TestMethod]
	public void AlwaysSuppressesWhitespaceWarnings()
	{
		RecordingGitProcessRunner runner = new();
		GitApplyBuilder builder = new(runner, TestPaths.Root, "patch");

		Assert.IsTrue(
			builder.BuildArguments().Contains("--whitespace=nowarn"),
			"Staging content already on disk is not the moment to enforce a whitespace policy the user configured for authoring.");
	}

	[TestMethod]
	public void RefusesEmptyPatchText()
	{
		RecordingGitProcessRunner runner = new();
		GitRepository repository = new() { LocalPath = TestPaths.Root, ProcessRunner = runner };

		_ = Assert.ThrowsExactly<ArgumentException>(() => _ = repository.Apply("   "));
	}

	[TestMethod]
	public async Task DeletesTheTemporaryFileEvenWhenGitFailsAsync()
	{
		RecordingGitProcessRunner runner = new() { ExitCode = 1, StandardError = "error: corrupt patch" };
		GitApplyBuilder builder = new(runner, TestPaths.Root, "not a patch\n");

		_ = await Assert.ThrowsExactlyAsync<GitCommandException>(
			async () => await builder.ExecuteAsync().ConfigureAwait(false)).ConfigureAwait(false);

		string path = runner.LastArguments![^1];

		Assert.IsFalse(
			File.Exists(path),
			"A failing patch must not leave files behind, and the failure the caller sees is git's, not the cleanup's.");
	}
}
