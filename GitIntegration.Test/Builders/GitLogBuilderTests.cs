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

	[TestMethod]
	public void EmitsAllRefsBeforeTheNegation()
	{
		// Order is the whole correctness question here. git negates everything after a --not, so
		// "--not --remotes --all" excludes every reference instead of including them — and reports an
		// empty log rather than failing, so nothing else would catch the mistake. Verified against
		// git 2.43.
		RecordingGitProcessRunner runner = new();
		GitLogBuilder builder = new(runner, TestPaths.Root);

		_ = builder.IncludingAllRefs().ExcludingRemoteTrackingRefs();

		string[] arguments = [.. builder.BuildArguments()];
		int all = Array.IndexOf(arguments, "--all");
		int not = Array.IndexOf(arguments, "--not");

		Assert.AreNotEqual(-1, all);
		Assert.AreNotEqual(-1, not);
		Assert.IsTrue(all < not, "--all must precede --not or the query means the opposite.");
	}

	[TestMethod]
	public void EmitsTheNegationAsAClosedTriple()
	{
		// --not reverses every revision specifier that follows it until the next --not, so the pair is
		// emitted with a closing --not that scopes the negation to --remotes alone. Without it a
		// revision or pathspec emitted below would be excluded rather than selected.
		RecordingGitProcessRunner runner = new();
		GitLogBuilder builder = new(runner, TestPaths.Root);

		_ = builder.ExcludingRemoteTrackingRefs();

		string[] arguments = [.. builder.BuildArguments()];
		int not = Array.IndexOf(arguments, "--not");

		Assert.AreEqual("--remotes", arguments[not + 1]);
		Assert.AreEqual("--not", arguments[not + 2]);
	}

	[TestMethod]
	public void KeepsARevisionOutsideTheNegation()
	{
		// The failure the closing --not prevents: "git log --not --remotes <revision>" asks for
		// commits in neither, which is a different question that quietly returns nothing. The revision
		// has to land after the negation has been closed.
		RecordingGitProcessRunner runner = new();
		GitLogBuilder builder = new(runner, TestPaths.Root);

		_ = builder.ExcludingRemoteTrackingRefs().ForRevision("HEAD".As<GitRefName>());

		string[] arguments = [.. builder.BuildArguments()];
		int revision = Array.IndexOf(arguments, "HEAD");
		int closingNot = Array.LastIndexOf(arguments, "--not");

		Assert.IsTrue(closingNot < revision, "The negation must be closed before the revision.");
	}

	[TestMethod]
	public void KeepsTheNegationBeforeEveryNonOptionArgument()
	{
		// git refuses --not once a non-option argument has appeared: "git log --end-of-options HEAD
		// --not --remotes" dies with "fatal: option '--not' must come before non-option arguments".
		// That is why the negation cannot simply be deferred to the end of the vector instead.
		RecordingGitProcessRunner runner = new();
		GitLogBuilder builder = new(runner, TestPaths.Root);

		_ = builder.IncludingAllRefs().ExcludingRemoteTrackingRefs().ForRevision("HEAD".As<GitRefName>());

		string[] arguments = [.. builder.BuildArguments()];
		int marker = Array.IndexOf(arguments, "--end-of-options");

		Assert.IsTrue(Array.LastIndexOf(arguments, "--not") < marker);
		Assert.IsTrue(Array.IndexOf(arguments, "--remotes") < marker);
		Assert.IsTrue(Array.IndexOf(arguments, "--all") < marker);
	}

	[TestMethod]
	public void OmitsBothFlagsWhenNeitherWasRequested()
	{
		RecordingGitProcessRunner runner = new();
		GitLogBuilder builder = new(runner, TestPaths.Root);

		string[] arguments = [.. builder.BuildArguments()];

		CollectionAssert.DoesNotContain(arguments, "--all");
		CollectionAssert.DoesNotContain(arguments, "--not");
		CollectionAssert.DoesNotContain(arguments, "--remotes");
	}
}
