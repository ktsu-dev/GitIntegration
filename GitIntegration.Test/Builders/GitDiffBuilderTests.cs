// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

[TestClass]
public class GitDiffBuilderTests
{
	[TestMethod]
	public void BuildsTheDefaultDiffVector()
	{
		RecordingGitProcessRunner runner = new();
		GitDiffBuilder builder = new(runner, TestPaths.Root);

		IReadOnlyList<string> arguments = builder.BuildArguments();

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"diff",
			"--name-status",
			"-z",
		];
		CollectionAssert.AreEqual(expectedArguments, arguments.ToArray());
	}

	[TestMethod]
	public void MapsTheOptionFlags()
	{
		RecordingGitProcessRunner runner = new();
		GitDiffBuilder builder = new(runner, TestPaths.Root);

		_ = builder.Staged().DetectRenames().DetectCopies();

		string[] arguments = [.. builder.BuildArguments()];
		CollectionAssert.Contains(arguments, "--cached");
		CollectionAssert.Contains(arguments, "--find-renames");
		CollectionAssert.Contains(arguments, "--find-copies");
	}

	[TestMethod]
	public void PutsASingleRevisionBehindTheEndOfOptionsMarker()
	{
		RecordingGitProcessRunner runner = new();
		GitDiffBuilder builder = new(runner, TestPaths.Root);

		_ = builder.Against("HEAD".As<GitRefName>());

		string[] arguments = [.. builder.BuildArguments()];
		int marker = Array.IndexOf(arguments, "--end-of-options");

		Assert.AreNotEqual(-1, marker);
		Assert.AreEqual("HEAD", arguments[marker + 1]);
	}

	[TestMethod]
	public void PutsBothRevisionsInOrderBehindTheEndOfOptionsMarker()
	{
		RecordingGitProcessRunner runner = new();
		GitDiffBuilder builder = new(runner, TestPaths.Root);

		_ = builder.Between("main".As<GitRefName>(), "feature/x".As<GitRefName>());

		string[] arguments = [.. builder.BuildArguments()];
		int marker = Array.IndexOf(arguments, "--end-of-options");

		Assert.AreEqual("main", arguments[marker + 1]);
		Assert.AreEqual("feature/x", arguments[marker + 2]);
	}

	[TestMethod]
	public void LastRevisionSelectionWins()
	{
		// Against and Between set the same slot, so a caller that calls both gets the later one
		// rather than a vector carrying three revisions that git would reject.
		RecordingGitProcessRunner runner = new();
		GitDiffBuilder builder = new(runner, TestPaths.Root);

		_ = builder.Between("main".As<GitRefName>(), "feature/x".As<GitRefName>())
			.Against("HEAD".As<GitRefName>());

		string[] arguments = [.. builder.BuildArguments()];

		CollectionAssert.Contains(arguments, "HEAD");
		CollectionAssert.DoesNotContain(arguments, "main");
		CollectionAssert.DoesNotContain(arguments, "feature/x");
	}

	[TestMethod]
	public void PutsPathsAfterADoubleDash()
	{
		RecordingGitProcessRunner runner = new();
		GitDiffBuilder builder = new(runner, TestPaths.Root);

		_ = builder.ForPath("docs/plan.md".As<RelativeFilePath>());

		string[] arguments = [.. builder.BuildArguments()];
		int separator = Array.IndexOf(arguments, "--");

		Assert.AreNotEqual(-1, separator);
		Assert.AreEqual("docs/plan.md".As<RelativeFilePath>().WeakString, arguments[separator + 1]);
	}

	[TestMethod]
	public void RejectsNullArguments()
	{
		RecordingGitProcessRunner runner = new();
		GitDiffBuilder builder = new(runner, TestPaths.Root);

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.Against(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.Between(null!, "main".As<GitRefName>()));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.Between("main".As<GitRefName>(), null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.ForPath(null!));
	}

	[TestMethod]
	public void SwitchesToRawAndNumstatWhenLineCountsAreAskedFor()
	{
		// --name-status and --numstat are both display formats and git lets the last one win, so
		// asking for both would silently produce only one section. --raw is the form that combines.
		RecordingGitProcessRunner runner = new();
		GitDiffBuilder builder = new(runner, TestPaths.Root);

		_ = builder.WithLineCounts();

		string[] arguments = [.. builder.BuildArguments()];

		CollectionAssert.Contains(arguments, "--raw");
		CollectionAssert.Contains(arguments, "--numstat");
		CollectionAssert.DoesNotContain(arguments, "--name-status");
		CollectionAssert.Contains(arguments, "-z");
	}

	[TestMethod]
	public void LeavesTheDefaultVectorUnchangedWhenLineCountsAreNotAskedFor()
	{
		// The option is opt-in precisely so the command and its output volume stay as they were for
		// callers that only want the path list.
		RecordingGitProcessRunner runner = new();
		GitDiffBuilder builder = new(runner, TestPaths.Root);

		string[] arguments = [.. builder.BuildArguments()];

		CollectionAssert.Contains(arguments, "--name-status");
		CollectionAssert.DoesNotContain(arguments, "--raw");
		CollectionAssert.DoesNotContain(arguments, "--numstat");
	}

	[TestMethod]
	public async Task ReportsNoLineCountsUnlessTheyWereAskedForAsync()
	{
		// The default parser reads --name-status output, which carries no counts at all, so both
		// fields stay null rather than defaulting to zero.
		RecordingGitProcessRunner runner = new() { StandardOutput = "M\0a.txt\0" };
		GitDiffBuilder builder = new(runner, TestPaths.Root);

		IReadOnlyList<GitDiffEntry> entries =
			await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsNull(entries[0].Insertions);
		Assert.IsNull(entries[0].Deletions);
	}

	[TestMethod]
	public async Task ReportsLineCountsWhenTheyWereAskedForAsync()
	{
		RecordingGitProcessRunner runner = new()
		{
			StandardOutput = ":100644 100644 366fd40 fbeb5f4 M\0a.txt\0" + "3\t2\ta.txt\0",
		};
		GitDiffBuilder builder = new(runner, TestPaths.Root);

		_ = builder.WithLineCounts();

		IReadOnlyList<GitDiffEntry> entries =
			await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(3, entries[0].Insertions);
		Assert.AreEqual(2, entries[0].Deletions);
	}

	[TestMethod]
	public void WithLineCountsReturnsTheSameBuilderForChaining()
	{
		RecordingGitProcessRunner runner = new();
		GitDiffBuilder builder = new(runner, TestPaths.Root);

		Assert.AreSame(builder, builder.WithLineCounts());
	}

	public TestContext TestContext { get; set; } = null!;
}
