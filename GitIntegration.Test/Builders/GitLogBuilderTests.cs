// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

[TestClass]
public class GitLogBuilderTests
{
	private const string ExpectedFormat =
		"--format=%H%x1f%T%x1f%P%x1f%an%x1f%ae%x1f%aI%x1f%cn%x1f%ce%x1f%cI%x1f%s%x1f%b";

	[TestMethod]
	public void BuildsTheDefaultLogVector()
	{
		RecordingGitProcessRunner runner = new();
		GitLogBuilder builder = new(runner, TestPaths.Root);

		IReadOnlyList<string> arguments = builder.BuildArguments();

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"log",
			"-z",
			ExpectedFormat,
		];
		CollectionAssert.AreEqual(expectedArguments, arguments.ToArray());
	}

	[TestMethod]
	public void PinsTheExactFormatStringSentToGit()
	{
		// Asserted literally, not by referencing GitOutputFormats, so that a change to the format
		// fails here rather than silently changing what every parser fixture is pinned against.
		RecordingGitProcessRunner runner = new();
		GitLogBuilder builder = new(runner, TestPaths.Root);

		CollectionAssert.Contains(builder.BuildArguments().ToArray(), ExpectedFormat);
	}

	[TestMethod]
	public void MapsTakeAndSkipToTheirOptions()
	{
		RecordingGitProcessRunner runner = new();
		GitLogBuilder builder = new(runner, TestPaths.Root);

		_ = builder.Take(5).Skip(2).FirstParentOnly();

		string[] arguments = [.. builder.BuildArguments()];
		CollectionAssert.Contains(arguments, "--max-count=5");
		CollectionAssert.Contains(arguments, "--skip=2");
		CollectionAssert.Contains(arguments, "--first-parent");
	}

	[TestMethod]
	public void PutsARevisionBehindTheEndOfOptionsMarker()
	{
		RecordingGitProcessRunner runner = new();
		GitLogBuilder builder = new(runner, TestPaths.Root);

		_ = builder.ForRevision("main".As<GitRefName>());

		string[] arguments = [.. builder.BuildArguments()];
		int marker = Array.IndexOf(arguments, "--end-of-options");

		Assert.AreNotEqual(-1, marker);
		Assert.AreEqual("main", arguments[marker + 1]);
	}

	[TestMethod]
	public void PutsPathsAfterADoubleDash()
	{
		RecordingGitProcessRunner runner = new();
		GitLogBuilder builder = new(runner, TestPaths.Root);

		_ = builder.ForPath("docs/plan.md".As<RelativeFilePath>());

		string[] arguments = [.. builder.BuildArguments()];
		int separator = Array.IndexOf(arguments, "--");

		Assert.AreNotEqual(-1, separator);
		Assert.AreEqual("docs/plan.md".As<RelativeFilePath>().WeakString, arguments[separator + 1]);
	}

	[TestMethod]
	public void PutsTheRevisionBeforeThePathSeparator()
	{
		RecordingGitProcessRunner runner = new();
		GitLogBuilder builder = new(runner, TestPaths.Root);

		_ = builder.ForRevision("main".As<GitRefName>()).ForPath("docs/plan.md".As<RelativeFilePath>());

		string[] arguments = [.. builder.BuildArguments()];

		// git reads everything after -- as a pathspec, so a revision placed after it is silently
		// treated as a filename and the log comes back empty instead of failing.
		Assert.IsTrue(Array.IndexOf(arguments, "main") < Array.IndexOf(arguments, "--"));
	}

	[TestMethod]
	public void EmitsNoEndOfOptionsMarkerWhenThereAreNoCallerOperands()
	{
		RecordingGitProcessRunner runner = new();
		GitLogBuilder builder = new(runner, TestPaths.Root);

		CollectionAssert.DoesNotContain(builder.BuildArguments().ToArray(), "--end-of-options");
	}

	[TestMethod]
	public void RejectsANegativeTakeOrSkip()
	{
		RecordingGitProcessRunner runner = new();
		GitLogBuilder builder = new(runner, TestPaths.Root);

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = builder.Take(-1));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = builder.Skip(-1));
	}

	[TestMethod]
	public void RejectsANullRevisionOrPath()
	{
		RecordingGitProcessRunner runner = new();
		GitLogBuilder builder = new(runner, TestPaths.Root);

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.ForRevision(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.ForPath(null!));
	}
}
