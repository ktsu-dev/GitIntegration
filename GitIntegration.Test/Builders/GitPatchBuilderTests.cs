// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;
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
			"-c", "diff.suppressBlankEmpty=false",
			"diff",
			"--no-ext-diff",
			"--no-textconv",
			"--no-color",
			"--src-prefix=a/",
			"--dst-prefix=b/",
		];

		Assert.AreSequenceEqual(expectedArguments, builder.BuildArguments());
	}

	[TestMethod]
	public void AlwaysPinsBothPathPrefixes()
	{
		RecordingGitProcessRunner runner = new();
		GitPatchBuilder builder = new(runner, TestPaths.Root);

		IReadOnlyList<string> arguments = builder.BuildArguments();

		Assert.IsTrue(
			arguments.Contains("--src-prefix=a/"),
			"diff.noprefix emits 'diff --git f.txt f.txt' and diff.mnemonicPrefix emits 'diff --git i/f.txt w/f.txt'. Neither header carries a new-side path this parser can read, and neither patch applies without -p0.");
		Assert.IsTrue(
			arguments.Contains("--dst-prefix=b/"),
			"diff.noprefix emits 'diff --git f.txt f.txt' and diff.mnemonicPrefix emits 'diff --git i/f.txt w/f.txt'. Neither header carries a new-side path this parser can read, and neither patch applies without -p0.");
	}

	[TestMethod]
	public void AlwaysDisablesBlankEmptySuppression()
	{
		RecordingGitProcessRunner runner = new();
		GitPatchBuilder builder = new(runner, TestPaths.Root);

		List<string> arguments = [.. builder.BuildArguments()];
		int setting = arguments.IndexOf("diff.suppressBlankEmpty=false");

		Assert.IsTrue(
			setting > 0 && arguments[setting - 1] == "-c",
			"diff.suppressBlankEmpty prints an empty context line as a bare newline, which ends the hunk body early and truncates the text a round trip depends on.");
		Assert.IsTrue(
			setting < arguments.IndexOf("diff"),
			"Git reads -c only before the subcommand.");
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

	[TestMethod]
	public void RefusesAZeroContextCount()
	{
		RecordingGitProcessRunner runner = new();
		GitPatchBuilder builder = new(runner, TestPaths.Root);

		ArgumentOutOfRangeException thrown = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => _ = builder.WithContext(0),
			"git apply refuses a zero-context patch without --unidiff-zero, which IGitApplyBuilder does not offer, so this is the one pairing of the library's own two verbs that could never work.");

		StringAssert.Contains(thrown.Message, "--unidiff-zero", StringComparison.Ordinal);
	}
}
