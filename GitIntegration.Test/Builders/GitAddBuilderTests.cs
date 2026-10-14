// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

[TestClass]
public class GitAddBuilderTests
{
	[TestMethod]
	public void BuildsTheAllAddVector()
	{
		RecordingGitProcessRunner runner = new();
		GitAddBuilder builder = new(runner, TestPaths.Root);

		_ = builder.All();

		IReadOnlyList<string> arguments = builder.BuildArguments();

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"add",
			"--all",
		];
		CollectionAssert.AreEqual(expectedArguments, arguments.ToArray());
	}

	[TestMethod]
	public void RejectsAnAddWithNothingToStage()
	{
		// A bare "git add" prints "Nothing specified, nothing added." and exits 0, so without this
		// guard the builder would report success having staged nothing.
		RecordingGitProcessRunner runner = new();
		GitAddBuilder builder = new(runner, TestPaths.Root);

		Assert.ThrowsExactly<InvalidOperationException>(() => _ = builder.BuildArguments());
	}

	[TestMethod]
	public void RejectsAskingForBothAllAndUpdateTrackedOnly()
	{
		// git fails this only as "options '-A' and '-u' cannot be used together" (exit 128), which
		// names neither of the fluent calls that caused it.
		RecordingGitProcessRunner runner = new();
		GitAddBuilder builder = new(runner, TestPaths.Root);

		_ = builder.All().UpdateTrackedOnly();

		Assert.ThrowsExactly<InvalidOperationException>(() => _ = builder.BuildArguments());
	}

	[TestMethod]
	public async Task RefusesAnAddWithNothingToStageBeforeRunningGitAsync()
	{
		RecordingGitProcessRunner runner = new();
		GitAddBuilder builder = new(runner, TestPaths.Root);

		_ = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
			() => builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token)).ConfigureAwait(false);
		Assert.IsNull(runner.LastRequest);
	}

	[TestMethod]
	public void MapsTheOptionFlags()
	{
		RecordingGitProcessRunner runner = new();

		GitAddBuilder all = new(runner, TestPaths.Root);
		_ = all.All();
		CollectionAssert.Contains(all.BuildArguments().ToArray(), "--all");

		GitAddBuilder update = new(runner, TestPaths.Root);
		_ = update.UpdateTrackedOnly();
		CollectionAssert.Contains(update.BuildArguments().ToArray(), "--update");
	}

	[TestMethod]
	public void PutsPathsBehindTheEndOfOptionsMarker()
	{
		// A pathspec is caller-supplied, so a value beginning with a dash would otherwise be read by
		// git as a flag.
		RecordingGitProcessRunner runner = new();
		GitAddBuilder builder = new(runner, TestPaths.Root);

		_ = builder.ForPath("docs/plan.md".As<RelativeFilePath>());

		string[] arguments = [.. builder.BuildArguments()];
		int marker = Array.IndexOf(arguments, "--end-of-options");

		Assert.AreNotEqual(-1, marker);
		Assert.AreEqual("docs/plan.md".As<RelativeFilePath>().WeakString, arguments[marker + 1]);
	}

	[TestMethod]
	public void AccumulatesPathsAcrossCalls()
	{
		RecordingGitProcessRunner runner = new();
		GitAddBuilder builder = new(runner, TestPaths.Root);

		_ = builder.ForPath("a.txt".As<RelativeFilePath>()).ForPath("b.txt".As<RelativeFilePath>());

		string[] arguments = [.. builder.BuildArguments()];
		CollectionAssert.Contains(arguments, "a.txt".As<RelativeFilePath>().WeakString);
		CollectionAssert.Contains(arguments, "b.txt".As<RelativeFilePath>().WeakString);
	}

	[TestMethod]
	public void EmitsNoEndOfOptionsMarkerWhenNoPathIsGiven()
	{
		RecordingGitProcessRunner runner = new();
		GitAddBuilder builder = new(runner, TestPaths.Root);
		_ = builder.All();

		CollectionAssert.DoesNotContain(builder.BuildArguments().ToArray(), "--end-of-options");
	}

	[TestMethod]
	public void ConfigurationMethodsReturnTheSameBuilderForChaining()
	{
		RecordingGitProcessRunner runner = new();
		GitAddBuilder builder = new(runner, TestPaths.Root);

		IGitAddBuilder chained = builder.All().UpdateTrackedOnly();

		Assert.AreSame(builder, chained);
	}

	[TestMethod]
	public void RejectsANullPath()
	{
		RecordingGitProcessRunner runner = new();
		GitAddBuilder builder = new(runner, TestPaths.Root);

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.ForPath(null!));
	}

	[TestMethod]
	public async Task ExecuteReportsTheArgumentVectorOnSuccessAsync()
	{
		// git add writes nothing useful to standard output, so the result carries the vector rather
		// than a parse of output that does not exist.
		RecordingGitProcessRunner runner = new();
		GitAddBuilder builder = new(runner, TestPaths.Root);
		_ = builder.All();

		GitCompleted completed = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		CollectionAssert.AreEqual(builder.BuildArguments().ToArray(), completed.Arguments.ToArray());
	}

	[TestMethod]
	public async Task ExecuteThrowsWhenAPathspecMatchesNothingAsync()
	{
		// Captured from git 2.50: a pathspec matching no file exits 128.
		RecordingGitProcessRunner runner = new()
		{
			ExitCode = 128,
			StandardError = "fatal: pathspec 'no/such/file.txt' did not match any files\n",
		};
		GitAddBuilder builder = new(runner, TestPaths.Root);

		_ = builder.ForPath("no/such/file.txt".As<RelativeFilePath>());

		GitCommandException exception = await Assert.ThrowsExactlyAsync<GitCommandException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.AreEqual(128, exception.ExitCode);
	}

	public TestContext TestContext { get; set; } = null!;
}
