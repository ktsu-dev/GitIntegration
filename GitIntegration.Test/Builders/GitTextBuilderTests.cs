// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Threading.Tasks;

[TestClass]
public class GitTextBuilderTests
{
	[TestMethod]
	public void BuildsAFixedVectorWithoutRepositoryScoping()
	{
		RecordingGitProcessRunner runner = new();
		GitTextBuilder builder = new(runner, null, "rev-parse", "--show-toplevel");

		string[] expectedArguments =
		[
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"rev-parse",
			"--show-toplevel",
		];
		CollectionAssert.AreEqual(expectedArguments, builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void ScopesToTheRepositoryWhenGivenAPath()
	{
		RecordingGitProcessRunner runner = new();
		GitTextBuilder builder = new(runner, TestPaths.Root, "remote", "get-url", "origin");

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"remote", "get-url", "origin",
		];
		CollectionAssert.AreEqual(expectedArguments, builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public async Task TrimsTheOutputAsync()
	{
		// Every probe command produces one line with a trailing newline and every caller compares the
		// value against a literal, so trimming belongs here rather than at each call site.
		RecordingGitProcessRunner runner = new() { StandardOutput = "  true " + (char)10 };
		GitTextBuilder builder = new(runner, TestPaths.Root, "rev-parse", "--is-inside-work-tree");

		string value = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual("true", value);
	}

	public TestContext TestContext { get; set; } = null!;
}
