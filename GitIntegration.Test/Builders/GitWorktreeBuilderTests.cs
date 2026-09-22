// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using ktsu.Semantics.Strings;

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

	[TestMethod]
	public void BuildsTheMinimalWorktreeAddVector()
	{
		RecordingGitProcessRunner runner = new();
		GitWorktreeAddBuilder builder = new(runner, TestPaths.Root, TestPaths.Worktree);

		CollectionAssert.AreEqual(
			Expect("worktree", "add", "--end-of-options", TestPaths.Worktree.WeakString),
			builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void PutsACheckedOutBranchInTheCommitIshOperand()
	{
		RecordingGitProcessRunner runner = new();
		GitWorktreeAddBuilder builder = new(runner, TestPaths.Root, TestPaths.Worktree);
		_ = builder.CheckingOut("feature".As<GitBranchName>());

		CollectionAssert.AreEqual(
			Expect("worktree", "add", "--end-of-options", TestPaths.Worktree.WeakString, "feature"),
			builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void EmitsTheCreateBranchOptionBeforeThePath()
	{
		RecordingGitProcessRunner runner = new();
		GitWorktreeAddBuilder builder = new(runner, TestPaths.Root, TestPaths.Worktree);
		_ = builder.CreatingBranch("feature".As<GitBranchName>());

		CollectionAssert.AreEqual(
			Expect("worktree", "add", "-b", "feature", "--end-of-options", TestPaths.Worktree.WeakString),
			builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void EmitsTheResettingCreateBranchOption()
	{
		RecordingGitProcessRunner runner = new();
		GitWorktreeAddBuilder builder = new(runner, TestPaths.Root, TestPaths.Worktree);
		_ = builder.CreatingOrResettingBranch("feature".As<GitBranchName>());

		CollectionAssert.AreEqual(
			Expect("worktree", "add", "-B", "feature", "--end-of-options", TestPaths.Worktree.WeakString),
			builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void CombinesBranchCreationWithAStartPoint()
	{
		RecordingGitProcessRunner runner = new();
		GitWorktreeAddBuilder builder = new(runner, TestPaths.Root, TestPaths.Worktree);
		_ = builder.CreatingBranch("feature".As<GitBranchName>()).From("origin/main".As<GitRefName>());

		CollectionAssert.AreEqual(
			Expect("worktree", "add", "-b", "feature", "--end-of-options", TestPaths.Worktree.WeakString, "origin/main"),
			builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void CombinesDetachmentWithACommitIsh()
	{
		RecordingGitProcessRunner runner = new();
		GitWorktreeAddBuilder builder = new(runner, TestPaths.Root, TestPaths.Worktree);
		_ = builder.Detached().From("v1.2.0".As<GitRefName>());

		CollectionAssert.AreEqual(
			Expect("worktree", "add", "--detach", "--end-of-options", TestPaths.Worktree.WeakString, "v1.2.0"),
			builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void TheLastModeSelectionWins()
	{
		// The four modes are one field, resolved rather than accumulated, matching how
		// IGitBranchListBuilder's LocalOnly and RemoteOnly already replace each other. A vector
		// carrying both -b and --detach is one git rejects.
		RecordingGitProcessRunner runner = new();
		GitWorktreeAddBuilder builder = new(runner, TestPaths.Root, TestPaths.Worktree);
		_ = builder.CreatingBranch("feature".As<GitBranchName>()).Detached();

		string[] arguments = [.. builder.BuildArguments()];

		CollectionAssert.Contains(arguments, "--detach");
		CollectionAssert.DoesNotContain(arguments, "-b");
		CollectionAssert.DoesNotContain(arguments, "feature");
	}

	[TestMethod]
	public void TheLastCommitIshSelectionWins()
	{
		// CheckingOut and From write the same operand slot, so the same resolution applies.
		RecordingGitProcessRunner runner = new();
		GitWorktreeAddBuilder builder = new(runner, TestPaths.Root, TestPaths.Worktree);
		_ = builder.CheckingOut("feature".As<GitBranchName>()).From("v1.2.0".As<GitRefName>());

		string[] arguments = [.. builder.BuildArguments()];

		CollectionAssert.Contains(arguments, "v1.2.0");
		CollectionAssert.DoesNotContain(arguments, "feature");
	}

	[TestMethod]
	public void EmitsForceAndNoCheckout()
	{
		RecordingGitProcessRunner runner = new();
		GitWorktreeAddBuilder builder = new(runner, TestPaths.Root, TestPaths.Worktree);
		_ = builder.Force().WithoutCheckout();

		CollectionAssert.AreEqual(
			Expect("worktree", "add", "--force", "--no-checkout", "--end-of-options", TestPaths.Worktree.WeakString),
			builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public async Task ExecuteReportsTheArgumentVectorOnSuccessAsync()
	{
		// git worktree add writes its confirmation to standard error, not a form this library parses,
		// so the result carries the vector rather than a parse of that text.
		RecordingGitProcessRunner runner = new() { StandardError = "Preparing worktree (new branch 'feature')\n" };
		GitWorktreeAddBuilder builder = new(runner, TestPaths.Root, TestPaths.Worktree);

		GitCompleted completed = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		CollectionAssert.AreEqual(builder.BuildArguments().ToArray(), completed.Arguments.ToArray());
	}

	[TestMethod]
	public void BuildsTheWorktreeRemoveVector()
	{
		RecordingGitProcessRunner runner = new();
		GitWorktreeRemoveBuilder builder = new(runner, TestPaths.Root, TestPaths.Worktree);

		CollectionAssert.AreEqual(
			Expect("worktree", "remove", "--end-of-options", TestPaths.Worktree.WeakString),
			builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void EmitsForceOnRemoveBeforeThePath()
	{
		RecordingGitProcessRunner runner = new();
		GitWorktreeRemoveBuilder builder = new(runner, TestPaths.Root, TestPaths.Worktree);
		_ = builder.Force();

		CollectionAssert.AreEqual(
			Expect("worktree", "remove", "--force", "--end-of-options", TestPaths.Worktree.WeakString),
			builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void BuildsTheWorktreePruneVector()
	{
		RecordingGitProcessRunner runner = new();
		GitWorktreePruneBuilder builder = new(runner, TestPaths.Root);

		CollectionAssert.AreEqual(
			Expect("worktree", "prune"),
			builder.BuildArguments().ToArray());
	}

	public TestContext TestContext { get; set; } = null!;
}
