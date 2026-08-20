// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

[TestClass]
public class GitInitBuilderTests
{
	private static AbsoluteDirectoryPath Target =>
		(OperatingSystem.IsWindows() ? @"C:\dev\new-repo" : "/dev/new-repo").As<AbsoluteDirectoryPath>();

	[TestMethod]
	public void BuildsTheInitVectorWithoutRepositoryScoping()
	{
		// No -C: the target is not a repository yet, so git would fail trying to change into it
		// before doing any work. The path is an operand instead.
		RecordingGitProcessRunner runner = new();
		GitInitBuilder builder = new(runner, Target);

		string[] expectedArguments =
		[
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"init",
			"--end-of-options",
			Target.WeakString,
		];
		CollectionAssert.AreEqual(expectedArguments, builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void MapsTheOptionFlagsBeforeTheMarker()
	{
		RecordingGitProcessRunner runner = new();
		GitInitBuilder builder = new(runner, Target);

		_ = builder.Bare().WithInitialBranch("main".As<GitBranchName>());

		string[] arguments = [.. builder.BuildArguments()];
		int marker = Array.IndexOf(arguments, "--end-of-options");

		Assert.IsTrue(Array.IndexOf(arguments, "--bare") < marker);
		Assert.IsTrue(Array.IndexOf(arguments, "--initial-branch=main") < marker);
	}

	[TestMethod]
	public void ConfigurationMethodsReturnTheSameBuilderForChaining()
	{
		RecordingGitProcessRunner runner = new();
		GitInitBuilder builder = new(runner, Target);

		Assert.AreSame(builder, builder.Bare().WithInitialBranch("main".As<GitBranchName>()));
	}

	[TestMethod]
	public void RejectsNullArguments()
	{
		RecordingGitProcessRunner runner = new();

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new GitInitBuilder(runner, null!));

		GitInitBuilder builder = new(runner, Target);
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.WithInitialBranch(null!));
	}

	[TestMethod]
	public async Task ProbesBeforeInitialisingAndReportsAFreshRepositoryAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardError: "fatal: not a git repository (or any of the parent directories): .git\n", exitCode: 128)
			.Then(standardOutput: "Initialized empty Git repository in /dev/new-repo/.git/\n");
		GitInitBuilder builder = new(runner, Target);

		GitInitResult result = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsFalse(result.AlreadyExisted);
		Assert.AreEqual(Target, result.Repository.LocalPath);
		Assert.IsNotNull(result.Repository.ProcessRunner);

		Assert.AreEqual(2, runner.Invocations.Count);
		CollectionAssert.Contains(runner.Invocations[0].ToArray(), "--git-dir");
		CollectionAssert.Contains(runner.Invocations[1].ToArray(), "init");
	}

	[TestMethod]
	public async Task ReportsAnExistingRepositoryAsAlreadyExistingAsync()
	{
		// git init on an existing repository exits 0 and only says "Reinitialized" in prose, so the
		// probe is the sole machine-readable signal. ".git" is what --git-dir prints for a non-bare
		// repository at exactly this path.
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: ".git\n")
			.Then(standardOutput: "Reinitialized existing Git repository in /dev/new-repo/.git/\n");
		GitInitBuilder builder = new(runner, Target);

		GitInitResult result = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsTrue(result.AlreadyExisted);
	}

	[TestMethod]
	public async Task StillRunsInitWhenTheRepositoryAlreadyExistsAsync()
	{
		// The probe reports, it does not gate: git init is idempotent and running it is harmless.
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: ".git\n")
			.Then(standardOutput: "Reinitialized existing Git repository\n");
		GitInitBuilder builder = new(runner, Target);

		_ = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(2, runner.Invocations.Count);
		CollectionAssert.Contains(runner.Invocations[1].ToArray(), "init");
	}

	[TestMethod]
	public async Task ReportsAFreshRepositoryWhenTheProbeFindsOnlyAnAncestorRepositoryAsync()
	{
		// --git-dir prints an absolute path when the target is inside a repository rooted somewhere
		// above it: a real repository exists, but not at this path, so init here creates a new one
		// nested inside it. That must not be reported as AlreadyExisted.
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: (OperatingSystem.IsWindows() ? @"C:\dev\.git" : "/dev/.git") + "\n")
			.Then(standardOutput: "Initialized empty Git repository in /dev/new-repo/sub/.git/\n");
		GitInitBuilder builder = new(runner, Target);

		GitInitResult result = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsFalse(result.AlreadyExisted);
	}

	[TestMethod]
	public async Task ReportsABareRepositoryAtTheTargetAsAlreadyExistingAsync()
	{
		// --git-dir prints "." for a bare repository at exactly this path — the case
		// --is-inside-work-tree got backwards, since a bare repository has no working tree to be
		// inside.
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: ".\n")
			.Then(standardOutput: "Reinitialized existing Git repository in /dev/new-repo/\n");
		GitInitBuilder builder = new(runner, Target);

		GitInitResult result = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsTrue(result.AlreadyExisted);
	}

	[TestMethod]
	public async Task ThrowsWhenInitItselfFailsAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardError: "fatal: not a git repository\n", exitCode: 128)
			.Then(standardError: "fatal: cannot mkdir /dev/new-repo: Permission denied\n", exitCode: 128);
		GitInitBuilder builder = new(runner, Target);

		await Assert.ThrowsExactlyAsync<GitCommandException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task TryExecuteReportsAnInitFailureAsAResultAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardError: "fatal: not a git repository\n", exitCode: 128)
			.Then(standardError: "fatal: cannot mkdir: Permission denied\n", exitCode: 128);
		GitInitBuilder builder = new(runner, Target);

		GitResult<GitInitResult> result =
			await builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsFalse(result.Success);
		Assert.AreEqual(128, result.Error?.ExitCode);
	}

	public TestContext TestContext { get; set; } = null!;
}
