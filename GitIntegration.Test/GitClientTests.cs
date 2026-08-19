// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

[TestClass]
public class GitClientTests
{
	private const string NotARepository =
		"fatal: not a git repository (or any of the parent directories): .git\n";

	private static string TopLevel =>
		OperatingSystem.IsWindows() ? "C:/dev/fixture-repo" : "/dev/fixture-repo";

	private static AbsoluteDirectoryPath ExpectedTopLevel => TopLevel.As<AbsoluteDirectoryPath>();

	[TestMethod]
	public async Task GetVersionRunsTheVersionCommandAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: "git version 2.50.1.windows.1\n");
		GitClient client = new(runner);

		GitVersion version = await client.GetVersionAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(2, version.Major);
		Assert.AreEqual(50, version.Minor);
		CollectionAssert.Contains(runner.Invocations[0].ToArray(), "--version");
	}

	[TestMethod]
	public async Task IsRepositoryReportsTrueWhenGitSaysSoAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner().Then(standardOutput: "true\n");
		GitClient client = new(runner);

		bool isRepository = await client
			.IsRepositoryAsync(TestPaths.Root, TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsTrue(isRepository);

		string[] arguments = [.. runner.Invocations[0]];
		CollectionAssert.Contains(arguments, "--is-inside-work-tree");
		CollectionAssert.Contains(arguments, "-C");
		CollectionAssert.Contains(arguments, TestPaths.Root.WeakString);
	}

	[TestMethod]
	public async Task IsRepositoryReportsFalseWithoutThrowingWhenGitFailsAsync()
	{
		// Both "there is no repository here" and "that directory does not exist" exit 128, and
		// neither is an error from this method's point of view — the answer is simply no.
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardError: NotARepository, exitCode: 128);
		GitClient client = new(runner);

		bool isRepository = await client
			.IsRepositoryAsync(TestPaths.Root, TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsFalse(isRepository);
	}

	[TestMethod]
	public async Task DiscoverResolvesTheWorkingTreeRootAndBackFillsTheOriginRemoteAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: TopLevel + "\n")
			.Then(standardOutput: "https://github.com/ktsu-dev/GitIntegration.git\n");
		GitClient client = new(runner);

		GitRepository? repository = await client
			.DiscoverAsync(TestPaths.Root, TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsNotNull(repository);
		Assert.AreEqual(ExpectedTopLevel, repository.LocalPath);
		Assert.AreEqual(
			"https://github.com/ktsu-dev/GitIntegration.git".As<GitRepositoryRemotePath>(),
			repository.RemotePath);
		Assert.IsNotNull(repository.ProcessRunner);

		// git's own upward walk is what makes DiscoverAsync work, which is why this phase needs no
		// filesystem abstraction: --show-toplevel from a subdirectory returns the root.
		CollectionAssert.Contains(runner.Invocations[0].ToArray(), "--show-toplevel");
		CollectionAssert.Contains(runner.Invocations[1].ToArray(), "get-url");
	}

	[TestMethod]
	public async Task DiscoverLeavesTheRemotePathNullWhenThereIsNoOriginAsync()
	{
		// "error: No such remote 'origin'" exits 2, not 128, and is not a discovery failure.
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: TopLevel + "\n")
			.Then(standardError: "error: No such remote 'origin'\n", exitCode: 2);
		GitClient client = new(runner);

		GitRepository? repository = await client
			.DiscoverAsync(TestPaths.Root, TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsNotNull(repository);
		Assert.IsNull(repository.RemotePath);
	}

	[TestMethod]
	public async Task DiscoverReturnsNullWhenThereIsNoRepositoryAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardError: NotARepository, exitCode: 128);
		GitClient client = new(runner);

		GitRepository? repository = await client
			.DiscoverAsync(TestPaths.Root, TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsNull(repository);
	}

	[TestMethod]
	public async Task OpenThrowsRepositoryNotFoundWhereDiscoverWouldReturnNullAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardError: NotARepository, exitCode: 128);
		GitClient client = new(runner);

		await Assert.ThrowsExactlyAsync<GitRepositoryNotFoundException>(
			async () => await client
				.OpenAsync(TestPaths.Root, TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task OpenReturnsARepositoryThatCanRunVerbsAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: TopLevel + "\n")
			.Then(standardError: "error: No such remote 'origin'\n", exitCode: 2);
		GitClient client = new(runner);

		GitRepository repository = await client
			.OpenAsync(TestPaths.Root, TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		// The verb is scoped to the discovered root, not to the path that was opened, which is the
		// point of resolving --show-toplevel first.
		CollectionAssert.Contains(repository.Status().BuildArguments().ToArray(), ExpectedTopLevel.WeakString);
	}

	[TestMethod]
	public async Task DiscoverReportsAnUnusableTopLevelAsAParseFailureAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: "not-an-absolute-path\n");
		GitClient client = new(runner);

		await Assert.ThrowsExactlyAsync<GitParseException>(
			async () => await client
				.DiscoverAsync(TestPaths.Root, TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	[TestMethod]
	public void RejectsANullRunnerOrPath()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new GitClient(null!));

		ScriptedGitProcessRunner runner = new();
		GitClient client = new(runner);

		// Discarded rather than returned: a lambda whose body yields a value binds to the
		// Func<object?> overload, which awaits nothing, so the ArgumentNullException raised before
		// the first await would go unobserved. The discard makes it an Action.
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = client.IsRepositoryAsync(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = client.OpenAsync(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = client.DiscoverAsync(null!));
	}

	public TestContext TestContext { get; set; } = null!;
}
