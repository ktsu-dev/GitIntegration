// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Collections.Generic;
using System.Threading.Tasks;

using ktsu.Semantics.Strings;

[TestClass]
public class GitRevParseBuilderTests
{
	private const string HeadSha = "9429d2063d91f1097de51a196cb8203b06335738";

	[TestMethod]
	public void BuildsTheRevParseVectorWithTheRevisionBehindTheMarker()
	{
		RecordingGitProcessRunner runner = new();
		GitRevParseBuilder builder = new(runner, TestPaths.Root, "HEAD".As<GitRefName>());

		IReadOnlyList<string> arguments = builder.BuildArguments();

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"rev-parse",
			"--verify",
			"--end-of-options",
			"HEAD",
		];
		CollectionAssert.AreEqual(expectedArguments, arguments.ToArray());
	}

	[TestMethod]
	public async Task ExecuteReturnsTheResolvedObjectIdAsync()
	{
		RecordingGitProcessRunner runner = new() { StandardOutput = HeadSha + "\n" };
		GitRevParseBuilder builder = new(runner, TestPaths.Root, "HEAD".As<GitRefName>());

		GitCommitSha sha = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(HeadSha.As<GitCommitSha>(), sha);
	}

	[TestMethod]
	public async Task TryExecuteReportsAnUnknownRevisionAsAFailureAsync()
	{
		// Captured from git 2.50: an unresolvable revision exits 128 with this message. Callers
		// probing for a revision's existence should get a result, not an exception.
		RecordingGitProcessRunner runner = new()
		{
			ExitCode = 128,
			StandardError = "fatal: Needed a single revision\n",
		};
		GitRevParseBuilder builder = new(runner, TestPaths.Root, "nope".As<GitRefName>());

		GitResult<GitCommitSha> result =
			await builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsFalse(result.Success);
		Assert.AreEqual(128, result.Error?.ExitCode);
	}

	[TestMethod]
	public async Task ExecuteReportsOutputItCannotResolveAsAParseFailureAsync()
	{
		// git exiting zero with something that is not an object id is a parse problem, not a
		// command problem, so it must not masquerade as GitCommandException.
		RecordingGitProcessRunner runner = new() { StandardOutput = "not-a-sha\n" };
		GitRevParseBuilder builder = new(runner, TestPaths.Root, "HEAD".As<GitRefName>());

		await Assert.ThrowsExactlyAsync<GitParseException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	public TestContext TestContext { get; set; } = null!;
}
