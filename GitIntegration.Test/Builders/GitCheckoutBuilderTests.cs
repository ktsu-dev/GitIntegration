// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Threading.Tasks;

using ktsu.Semantics.Strings;

[TestClass]
public class GitCheckoutBuilderTests
{
	private static GitRefName Main => "main".As<GitRefName>();

	[TestMethod]
	public void BuildsTheDefaultCheckoutVector()
	{
		RecordingGitProcessRunner runner = new();
		GitCheckoutBuilder builder = new(runner, TestPaths.Root, Main);

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"checkout",
			"--end-of-options",
			"main",
		];
		CollectionAssert.AreEqual(expectedArguments, builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void MapsTheOptionFlags()
	{
		RecordingGitProcessRunner runner = new();

		GitCheckoutBuilder creating = new(runner, TestPaths.Root, Main);
		_ = creating.CreatingBranch();
		CollectionAssert.Contains(creating.BuildArguments().ToArray(), "-b");

		GitCheckoutBuilder forced = new(runner, TestPaths.Root, Main);
		_ = forced.Force();
		CollectionAssert.Contains(forced.BuildArguments().ToArray(), "--force");

		GitCheckoutBuilder detached = new(runner, TestPaths.Root, Main);
		_ = detached.Detach();
		CollectionAssert.Contains(detached.BuildArguments().ToArray(), "--detach");
	}

	[TestMethod]
	public void KeepsFlagsBeforeTheEndOfOptionsMarker()
	{
		// Anything after --end-of-options is an operand, so a flag emitted there would be handed to
		// git as a ref name.
		RecordingGitProcessRunner runner = new();
		GitCheckoutBuilder builder = new(runner, TestPaths.Root, Main);

		_ = builder.CreatingBranch().Force();

		string[] arguments = [.. builder.BuildArguments()];
		int marker = Array.IndexOf(arguments, "--end-of-options");

		Assert.IsTrue(Array.IndexOf(arguments, "-b") < marker);
		Assert.IsTrue(Array.IndexOf(arguments, "--force") < marker);
		Assert.AreEqual("main", arguments[marker + 1]);
	}

	[TestMethod]
	public void ConfigurationMethodsReturnTheSameBuilderForChaining()
	{
		RecordingGitProcessRunner runner = new();
		GitCheckoutBuilder builder = new(runner, TestPaths.Root, Main);

		Assert.AreSame(builder, builder.CreatingBranch().Force().Detach());
	}

	[TestMethod]
	public void RejectsANullTarget()
	{
		RecordingGitProcessRunner runner = new();

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new GitCheckoutBuilder(runner, TestPaths.Root, null!));
	}

	[TestMethod]
	public async Task ExecuteSucceedsEvenThoughGitReportsOnStandardErrorAsync()
	{
		// git checkout writes "Switched to branch 'x'" to standard error and exits 0. A builder that
		// treated non-empty stderr as failure would reject every successful checkout.
		RecordingGitProcessRunner runner = new() { StandardError = "Switched to branch 'main'\n" };
		GitCheckoutBuilder builder = new(runner, TestPaths.Root, Main);

		GitCompleted completed = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		CollectionAssert.AreEqual(builder.BuildArguments().ToArray(), completed.Arguments.ToArray());
	}

	[TestMethod]
	public async Task ExecuteThrowsForAnUnknownRefAsync()
	{
		// Captured from git 2.50: an unresolvable checkout target exits 1, not 128.
		RecordingGitProcessRunner runner = new()
		{
			ExitCode = 1,
			StandardError = "error: pathspec 'no-such' did not match any file(s) known to git\n",
		};
		GitCheckoutBuilder builder = new(runner, TestPaths.Root, "no-such".As<GitRefName>());

		GitCommandException exception = await Assert.ThrowsExactlyAsync<GitCommandException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.AreEqual(1, exception.ExitCode);
	}

	public TestContext TestContext { get; set; } = null!;
}
