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
			"main",
			"--",
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
	public void KeepsFlagsBeforeTheTargetAndTerminatesWithADoubleDash()
	{
		// The target must be the last thing before the "--" terminator, with every flag ahead of it:
		// a flag emitted after the target would be handed to git as a pathspec.
		RecordingGitProcessRunner runner = new();
		GitCheckoutBuilder builder = new(runner, TestPaths.Root, Main);

		_ = builder.CreatingBranch().Force();

		string[] arguments = [.. builder.BuildArguments()];
		int target = Array.IndexOf(arguments, "main");

		Assert.IsTrue(Array.IndexOf(arguments, "-b") < target);
		Assert.IsTrue(Array.IndexOf(arguments, "--force") < target);
		Assert.AreEqual("--", arguments[target + 1]);
		Assert.AreEqual(arguments.Length - 1, target + 1);
	}

	[TestMethod]
	public void DoesNotEmitTheEndOfOptionsMarker()
	{
		// git <= 2.43 leaves --end-of-options in checkout's own operand list, because checkout sets
		// PARSE_OPT_KEEP_DASHDASH and that release only stripped the marker when the flag was unset.
		// The marker then reaches git as a pathspec: "error: pathspec '--end-of-options' did not
		// match any file(s) known to git". git 2.44 changed the condition, but emitting the marker
		// would make Checkout unusable on every git before it — including Ubuntu 24.04 LTS's stock
		// 2.43. GitRefName's NotAnOptionAttribute is what keeps a dash-leading target out of the
		// vector instead.
		RecordingGitProcessRunner runner = new();
		GitCheckoutBuilder builder = new(runner, TestPaths.Root, Main);

		CollectionAssert.DoesNotContain(builder.BuildArguments().ToArray(), "--end-of-options");
	}

	[TestMethod]
	public void ConfigurationMethodsReturnTheSameBuilderForChaining()
	{
		// Deliberately not chaining CreatingBranch and Detach together: that combination is one git
		// refuses, and BuildArguments rejects it. Chaining it here to check a fluent return value
		// would read as an endorsement of a vector that can never run.
		RecordingGitProcessRunner runner = new();
		GitCheckoutBuilder builder = new(runner, TestPaths.Root, Main);

		Assert.AreSame(builder, builder.CreatingBranch().Force());
		Assert.AreSame(builder, builder.Detach());
	}

	[TestMethod]
	public void RejectsAskingForBothCreatingBranchAndDetach()
	{
		// Real git refuses "-b <name> --detach <target>" with
		// "fatal: '--detach' cannot be used with '-b/-B/--orphan'" (verified against git 2.43), so
		// without this guard the contradiction surfaces only as an opaque GitCommandException from a
		// process that was already spawned. Fetch and pull reject their own equivalent
		// contradictions before spawning, and checkout should be no less consistent.
		RecordingGitProcessRunner runner = new();
		GitCheckoutBuilder builder = new(runner, TestPaths.Root, Main);

		_ = builder.CreatingBranch().Detach();

		Assert.ThrowsExactly<InvalidOperationException>(() => _ = builder.BuildArguments());
	}

	[TestMethod]
	public void RejectsTheContradictionRegardlessOfTheOrderItWasConfiguredIn()
	{
		// A caller may set either first, so only the finished configuration can detect the
		// contradiction — the reason the guard lives in BuildArguments rather than in each setter.
		RecordingGitProcessRunner runner = new();
		GitCheckoutBuilder builder = new(runner, TestPaths.Root, Main);

		_ = builder.Detach().CreatingBranch();

		Assert.ThrowsExactly<InvalidOperationException>(() => _ = builder.BuildArguments());
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
