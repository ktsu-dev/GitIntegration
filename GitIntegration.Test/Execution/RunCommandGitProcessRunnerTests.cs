// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Threading.Tasks;

[TestClass]
public class RunCommandGitProcessRunnerTests
{
	[TestMethod]
	public async Task CapturesStandardOutputAndExitCodeAsync()
	{
		// Uses the host's own dotnet executable rather than git, so this test does not
		// require git to be installed.
		RunCommandGitProcessRunner runner = new(new GitOptions { ExecutablePath = "dotnet" });

		GitProcessResult result = await runner.RunAsync(["--version"], TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(0, result.ExitCode);
		Assert.IsTrue(result.Success);
		Assert.IsFalse(string.IsNullOrWhiteSpace(result.StandardOutput));
	}

	[TestMethod]
	public async Task EchoesArgumentVectorBackOnTheResultAsync()
	{
		RunCommandGitProcessRunner runner = new(new GitOptions { ExecutablePath = "dotnet" });

		GitProcessResult result = await runner.RunAsync(["--version"], TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		string[] expectedArguments = ["--version"];
		CollectionAssert.AreEqual(expectedArguments, result.Arguments.ToArray());
	}

	[TestMethod]
	public async Task ThrowsExecutableNotFoundWhenBinaryIsMissingAsync()
	{
		RunCommandGitProcessRunner runner = new(new GitOptions
		{
			ExecutablePath = "definitely-not-a-real-executable-9f3a2b",
		});

		await Assert.ThrowsExactlyAsync<GitExecutableNotFoundException>(
			async () => await runner.RunAsync(["--version"], TestContext.CancellationTokenSource.Token).ConfigureAwait(false)).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task ReportsNonZeroExitCodeWithoutThrowingAsync()
	{
		RunCommandGitProcessRunner runner = new(new GitOptions { ExecutablePath = "dotnet" });

		GitProcessResult result = await runner.RunAsync(
			["--this-flag-does-not-exist"],
			TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreNotEqual(0, result.ExitCode);
		Assert.IsFalse(result.Success);
	}

	public TestContext TestContext { get; set; } = null!;
}
