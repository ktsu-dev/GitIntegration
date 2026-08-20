// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Threading.Tasks;

using ktsu.Semantics.Strings;

[TestClass]
public class GitCommitBuilderTests
{
	private const string Nul = "\u0000";
	private const string Us = "\u001f";

	private const string Sha = "9429d2063d91f1097de51a196cb8203b06335738";
	private const string Tree = "f3758b7757b1f9bfe8c8e05fc5ac51bf3650c7d5";
	private const string Parent = "94947d6da5c05bf1c86af335b33cff8cee83cb3f";

	/// <summary>One record in the pinned log format, as the readback invocation returns it.</summary>
	private const string ReadBack =
		Sha + Us + Tree + Us + Parent + Us +
		"Fixture Author" + Us + "fixture@example.com" + Us + "2026-08-20T00:05:20+10:00" + Us +
		"Fixture Author" + Us + "fixture@example.com" + Us + "2026-08-20T00:05:20+10:00" + Us +
		"subject here" + Us + "body text" + Nul;

	private static GitCommitMessage Message => "subject here".As<GitCommitMessage>();

	[TestMethod]
	public void BuildsTheDefaultCommitVector()
	{
		RecordingGitProcessRunner runner = new();
		GitCommitBuilder builder = new(runner, TestPaths.Root, Message);

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"commit",
			"--message", "subject here",
		];
		CollectionAssert.AreEqual(expectedArguments, builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void EmitsTheBodyAsASecondMessage()
	{
		// git joins repeated --message values with a blank line, which is exactly the subject/body
		// convention, so the body needs no manual newline handling.
		RecordingGitProcessRunner runner = new();
		GitCommitBuilder builder = new(runner, TestPaths.Root, Message);

		_ = builder.WithBody("body text");

		string[] arguments = [.. builder.BuildArguments()];
		int subject = Array.IndexOf(arguments, "subject here");

		Assert.AreEqual("--message", arguments[subject + 1]);
		Assert.AreEqual("body text", arguments[subject + 2]);
	}

	[TestMethod]
	public void MapsTheOptionFlags()
	{
		RecordingGitProcessRunner runner = new();

		GitCommitBuilder empty = new(runner, TestPaths.Root, Message);
		_ = empty.AllowEmpty();
		CollectionAssert.Contains(empty.BuildArguments().ToArray(), "--allow-empty");

		GitCommitBuilder staged = new(runner, TestPaths.Root, Message);
		_ = staged.StageTrackedFiles();
		CollectionAssert.Contains(staged.BuildArguments().ToArray(), "--all");
	}

	[TestMethod]
	public void FormatsTheAuthorOverrideAsGitExpects()
	{
		// --author takes a single "Name <email>" string. Splitting it into two arguments makes git
		// treat the second as a pathspec.
		RecordingGitProcessRunner runner = new();
		GitCommitBuilder builder = new(runner, TestPaths.Root, Message);

		_ = builder.WithAuthor("Other Name".As<GitAuthorName>(), "other@example.com".As<GitAuthorEmail>());

		CollectionAssert.Contains(builder.BuildArguments().ToArray(), "--author=Other Name <other@example.com>");
	}

	[TestMethod]
	public void ConfigurationMethodsReturnTheSameBuilderForChaining()
	{
		RecordingGitProcessRunner runner = new();
		GitCommitBuilder builder = new(runner, TestPaths.Root, Message);

		Assert.AreSame(builder, builder.WithBody("b").AllowEmpty().StageTrackedFiles());
	}

	[TestMethod]
	public void RejectsNullArguments()
	{
		RecordingGitProcessRunner runner = new();

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new GitCommitBuilder(runner, TestPaths.Root, null!));

		GitCommitBuilder builder = new(runner, TestPaths.Root, Message);
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.WithBody(null!));
		Assert.ThrowsExactly<ArgumentNullException>(
			() => _ = builder.WithAuthor(null!, "e@example.com".As<GitAuthorEmail>()));
		Assert.ThrowsExactly<ArgumentNullException>(
			() => _ = builder.WithAuthor("N".As<GitAuthorName>(), null!));
	}

	[TestMethod]
	public async Task ExecuteRunsCommitThenReadsTheCommitBackAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: "[main 9429d20] subject here\n 1 file changed, 1 insertion(+)\n")
			.Then(standardOutput: ReadBack);
		GitCommitBuilder builder = new(runner, TestPaths.Root, Message);

		GitCommit commit = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(Sha.As<GitCommitSha>(), commit.Sha);
		Assert.AreEqual(Tree.As<GitCommitSha>(), commit.TreeSha);
		Assert.AreEqual("subject here", commit.Subject);
		Assert.AreEqual("body text", commit.Body);

		// Two invocations, in order: the commit, then the readback.
		Assert.AreEqual(2, runner.Invocations.Count);
		CollectionAssert.Contains(runner.Invocations[0].ToArray(), "commit");
		CollectionAssert.Contains(runner.Invocations[1].ToArray(), "log");
	}

	[TestMethod]
	public async Task TheReadBackUsesThePinnedLogFormatAndAsksForOneCommitAsync()
	{
		// Asserted literally so a change to the shared format constant fails here rather than
		// silently returning a differently-shaped commit.
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: "[main 9429d20] subject here\n")
			.Then(standardOutput: ReadBack);
		GitCommitBuilder builder = new(runner, TestPaths.Root, Message);

		_ = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		string[] readBack = [.. runner.Invocations[1]];
		CollectionAssert.Contains(
			readBack,
			"--format=%H%x1f%T%x1f%P%x1f%an%x1f%ae%x1f%aI%x1f%cn%x1f%ce%x1f%cI%x1f%s%x1f%b");
		CollectionAssert.Contains(readBack, "--max-count=1");
		CollectionAssert.Contains(readBack, "-z");
	}

	[TestMethod]
	public async Task ThrowsNothingToCommitWhenTheTreeIsCleanAsync()
	{
		// Captured from git 2.50: the message is on STANDARD OUTPUT with stderr empty, and the exit
		// code is 1. The base class's classifier only reads stderr, which is why this builder
		// overrides CreateException.
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: "On branch main\nnothing to commit, working tree clean\n", exitCode: 1);
		GitCommitBuilder builder = new(runner, TestPaths.Root, Message);

		GitNothingToCommitException exception = await Assert.ThrowsExactlyAsync<GitNothingToCommitException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.AreEqual(1, exception.ExitCode);
	}

	[TestMethod]
	public async Task ThrowsNothingToCommitWhenOnlyUntrackedFilesArePresentAsync()
	{
		// The second of git's two phrases. It does not contain the first, so a classifier matching
		// only "nothing to commit" would miss this case entirely.
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(
				standardOutput:
					"On branch main\nUntracked files:\n\tnew.txt\n\n" +
					"nothing added to commit but untracked files present (use \"git add\" to track)\n",
				exitCode: 1);
		GitCommitBuilder builder = new(runner, TestPaths.Root, Message);

		await Assert.ThrowsExactlyAsync<GitNothingToCommitException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task AnOrdinaryCommitFailureStaysAGenericCommandExceptionAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardError: "fatal: unable to write new index file\n", exitCode: 128);
		GitCommitBuilder builder = new(runner, TestPaths.Root, Message);

		await Assert.ThrowsExactlyAsync<GitCommandException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task TryExecuteReportsNothingToCommitAsAResultAsync()
	{
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: "nothing to commit, working tree clean\n", exitCode: 1);
		GitCommitBuilder builder = new(runner, TestPaths.Root, Message);

		GitResult<GitCommit> result =
			await builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsFalse(result.Success);
		Assert.AreEqual(1, result.Error?.ExitCode);

		// Commit reports "nothing to commit" on standard output with standard error empty, so a
		// failed TryExecuteAsync must fall back to standard output for its diagnostic text — an empty
		// StandardError here would mean the exact caller this result type exists for gets nothing.
		Assert.IsNotNull(result.Error);
		StringAssert.Contains(result.Error.StandardError, "nothing to commit");

		// The readback must not run when the commit itself failed.
		Assert.AreEqual(1, runner.Invocations.Count);
	}

	[TestMethod]
	public async Task ReportsAnEmptyReadBackAsAParseFailureAsync()
	{
		// git said the commit succeeded but log returned nothing. That is a parse problem, not a
		// command problem, and must not masquerade as GitCommandException.
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: "[main 9429d20] subject here\n")
			.Then(standardOutput: string.Empty);
		GitCommitBuilder builder = new(runner, TestPaths.Root, Message);

		await Assert.ThrowsExactlyAsync<GitParseException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	public TestContext TestContext { get; set; } = null!;
}
