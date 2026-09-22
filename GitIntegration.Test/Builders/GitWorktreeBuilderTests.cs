// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Collections.Generic;
using System.Linq;

[TestClass]
public sealed class GitWorktreeBuilderTests
{
	private static readonly string[] GlobalArguments =
	[
		"-C", TestPaths.Root.WeakString,
		"--no-pager",
		"-c", "core.quotepath=false",
		"-c", "color.ui=false",
	];

	private static string[] Expect(params string[] verbArguments) =>
		[.. GlobalArguments, .. verbArguments];

	[TestMethod]
	public void BuildsTheWorktreeListVector()
	{
		RecordingGitProcessRunner runner = new();
		GitWorktreeListBuilder builder = new(runner, TestPaths.Root);

		IReadOnlyList<string> arguments = builder.BuildArguments();

		CollectionAssert.AreEqual(Expect("worktree", "list", "--porcelain"), arguments.ToArray());
	}
}
