// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;

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
}
