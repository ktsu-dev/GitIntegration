// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Collections.Generic;

[TestClass]
public class GitBranchListBuilderTests
{
	private const string ExpectedFormat =
		"--format=%(refname)%1f%(refname:short)%1f%(objectname)%1f%(upstream:short)%1f%(HEAD)";

	[TestMethod]
	public void BuildsTheDefaultBranchListVector()
	{
		RecordingGitProcessRunner runner = new();
		GitBranchListBuilder builder = new(runner, TestPaths.Root);

		IReadOnlyList<string> arguments = builder.BuildArguments();

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"for-each-ref",
			ExpectedFormat,
			"refs/heads",
			"refs/remotes",
		];
		CollectionAssert.AreEqual(expectedArguments, arguments.ToArray());
	}

	[TestMethod]
	public void PinsTheExactFormatStringSentToGit()
	{
		// Asserted literally rather than through GitOutputFormats. The leading %(refname) is what
		// lets the parser tell a local branch from a remote-tracking one and drop the remote HEAD
		// symbolic reference, so silently losing it would break both behaviours at once.
		RecordingGitProcessRunner runner = new();
		GitBranchListBuilder builder = new(runner, TestPaths.Root);

		CollectionAssert.Contains(builder.BuildArguments().ToArray(), ExpectedFormat);
	}

	[TestMethod]
	public void LimitsTheReferencePrefixesOnRequest()
	{
		RecordingGitProcessRunner runner = new();

		GitBranchListBuilder local = new(runner, TestPaths.Root);
		_ = local.LocalOnly();
		string[] localArguments = [.. local.BuildArguments()];
		CollectionAssert.Contains(localArguments, "refs/heads");
		CollectionAssert.DoesNotContain(localArguments, "refs/remotes");

		GitBranchListBuilder remote = new(runner, TestPaths.Root);
		_ = remote.RemoteOnly();
		string[] remoteArguments = [.. remote.BuildArguments()];
		CollectionAssert.Contains(remoteArguments, "refs/remotes");
		CollectionAssert.DoesNotContain(remoteArguments, "refs/heads");
	}

	[TestMethod]
	public void TheLastPrefixSelectionWins()
	{
		RecordingGitProcessRunner runner = new();
		GitBranchListBuilder builder = new(runner, TestPaths.Root);

		IGitBranchListBuilder chained = builder.LocalOnly().RemoteOnly();

		Assert.AreSame(builder, chained);
		CollectionAssert.DoesNotContain(builder.BuildArguments().ToArray(), "refs/heads");
	}
}
