// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Linq;

[TestClass]
public class GitPatchBuilderTests
{
	[TestMethod]
	public void BuildsTheDefaultPatchVector()
	{
		RecordingGitProcessRunner runner = new();
		GitPatchBuilder builder = new(runner, TestPaths.Root);

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"diff",
			"--no-ext-diff",
			"--no-textconv",
			"--no-color",
		];

		Assert.AreSequenceEqual(expectedArguments, builder.BuildArguments());
	}

	[TestMethod]
	public void MapsTheOptionFlags()
	{
		RecordingGitProcessRunner runner = new();

		GitPatchBuilder staged = new(runner, TestPaths.Root);
		_ = staged.Staged();
		Assert.IsTrue(staged.BuildArguments().Contains("--cached"));

		GitPatchBuilder context = new(runner, TestPaths.Root);
		_ = context.WithContext(7);
		Assert.IsTrue(context.BuildArguments().Contains("-U7"));

		GitPatchBuilder renames = new(runner, TestPaths.Root);
		_ = renames.DetectRenames();
		Assert.IsTrue(renames.BuildArguments().Contains("--find-renames"));
	}

	[TestMethod]
	public void NeverEmitsTheNulSeparator()
	{
		RecordingGitProcessRunner runner = new();
		GitPatchBuilder builder = new(runner, TestPaths.Root);

		Assert.IsFalse(
			builder.BuildArguments().Contains("-z"),
			"Patch format is line-based, and -z changes only the name-status framing this builder does not use.");
	}

	[TestMethod]
	public void RefusesANegativeContextCount()
	{
		RecordingGitProcessRunner runner = new();
		GitPatchBuilder builder = new(runner, TestPaths.Root);

		_ = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = builder.WithContext(-1));
	}
}
