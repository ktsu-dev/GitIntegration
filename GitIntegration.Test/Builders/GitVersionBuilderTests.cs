// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Collections.Generic;
using System.Threading.Tasks;

[TestClass]
public class GitVersionBuilderTests
{
	[TestMethod]
	public void BuildsTheVersionArgumentVectorWithoutRepositoryScoping()
	{
		RecordingGitProcessRunner runner = new();
		GitVersionBuilder builder = new(runner);

		IReadOnlyList<string> arguments = builder.BuildArguments();

		// No -C: --version is not repository-scoped, so it must run anywhere, including where no
		// repository exists at all.
		string[] expectedArguments =
		[
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"--version",
		];
		CollectionAssert.AreEqual(expectedArguments, arguments.ToArray());
	}

	[TestMethod]
	public async Task ExecuteParsesTheVersionAsync()
	{
		RecordingGitProcessRunner runner = new() { StandardOutput = "git version 2.50.1.windows.1\n" };
		GitVersionBuilder builder = new(runner);

		GitVersion version = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(2, version.Major);
		Assert.AreEqual(50, version.Minor);
	}

	public TestContext TestContext { get; set; } = null!;
}
