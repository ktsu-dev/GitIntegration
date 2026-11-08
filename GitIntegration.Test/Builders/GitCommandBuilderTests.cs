// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

[TestClass]
public class GitCommandBuilderTests
{
	/// <summary>A minimal concrete builder, exercising only the base class behavior.</summary>
	private sealed class EchoBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath? repositoryPath)
		: GitCommandBuilder<string>(runner, repositoryPath)
	{
		protected override void AppendVerbArguments(ICollection<string> arguments) => arguments.Add("status");

		protected override string ParseResult(GitProcessResult result) => result.StandardOutput;
	}

	/// <summary>A builder that routes caller-supplied values through the operand helper.</summary>
	private sealed class OperandBuilder(IGitProcessRunner runner, params string[] operands)
		: GitCommandBuilder<string>(runner, repositoryPath: null)
	{
		protected override void AppendVerbArguments(ICollection<string> arguments)
		{
			arguments.Add("log");
			AppendOperands(arguments, operands);
		}

		protected override string ParseResult(GitProcessResult result) => result.StandardOutput;
	}

	[TestMethod]
	public void AppendOperandsEmitsEndOfOptionsMarkerBeforeOperands()
	{
		RecordingGitProcessRunner runner = new();
		OperandBuilder builder = new(runner, "-f", "main");

		IReadOnlyList<string> arguments = builder.BuildArguments();

		string[] expectedArguments =
		[
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"log",
			"--end-of-options",
			"-f", "main",
		];
		CollectionAssert.AreEqual(expectedArguments, arguments.ToArray());
	}

	[TestMethod]
	public void AppendOperandsEmitsTheMarkerEvenWithNoOperands()
	{
		RecordingGitProcessRunner runner = new();
		OperandBuilder builder = new(runner);

		IReadOnlyList<string> arguments = builder.BuildArguments();

		Assert.AreEqual("--end-of-options", arguments[^1]);
	}

	[TestMethod]
	public void InjectsGlobalArgumentsBeforeTheVerb()
	{
		RecordingGitProcessRunner runner = new();
		EchoBuilder builder = new(runner, TestPaths.Root);

		IReadOnlyList<string> arguments = builder.BuildArguments();

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"status",
		];
		CollectionAssert.AreEqual(expectedArguments, arguments.ToArray());
	}

	[TestMethod]
	public void OmitsRepositoryScopingWhenPathIsNull()
	{
		RecordingGitProcessRunner runner = new();
		EchoBuilder builder = new(runner, repositoryPath: null);

		IReadOnlyList<string> arguments = builder.BuildArguments();

		string[] expectedArguments =
		[
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"status",
		];
		CollectionAssert.AreEqual(expectedArguments, arguments.ToArray());
	}

	[TestMethod]
	public async Task ExecuteReturnsParsedResultOnSuccessAsync()
	{
		RecordingGitProcessRunner runner = new() { StandardOutput = "clean" };
		EchoBuilder builder = new(runner, TestPaths.Root);

		string result = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual("clean", result);

		// The load-bearing invariant of the execution core: what BuildArguments returns is exactly
		// what gets sent. Without this, a builder whose ExecuteAsync assembled a different vector
		// would pass every other test in this file.
		CollectionAssert.AreEqual(builder.BuildArguments().ToArray(), runner.LastArguments?.ToArray());
	}

	[TestMethod]
	public async Task ExecuteThrowsCommandExceptionOnFailureAsync()
	{
		RecordingGitProcessRunner runner = new() { ExitCode = 1, StandardError = "boom" };
		EchoBuilder builder = new(runner, TestPaths.Root);

		GitCommandException exception = await Assert.ThrowsExactlyAsync<GitCommandException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false)).ConfigureAwait(false);

		Assert.AreEqual(1, exception.ExitCode);
		Assert.AreEqual("boom", exception.StandardError);
	}

	[TestMethod]
	public async Task ExecuteThrowsRepositoryNotFoundWhenGitSaysSoAsync()
	{
		RecordingGitProcessRunner runner = new()
		{
			ExitCode = 128,
			StandardError = "fatal: not a git repository (or any of the parent directories): .git",
		};
		EchoBuilder builder = new(runner, TestPaths.Root);

		await Assert.ThrowsExactlyAsync<GitRepositoryNotFoundException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false)).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task ExecuteThrowsACommandFailureWhenASubmoduleGitDirectoryIsMissingAsync()
	{
		// Captured from git 2.43: status in a superproject whose submodule lost .git/modules/sub.
		// The superproject is a good repository, so this must not read as "no repository here".
		using TemporaryRepository temporary = new();
		_ = Directory.CreateDirectory(Path.Join(temporary.RootPath, ".git"));

		RecordingGitProcessRunner runner = new()
		{
			ExitCode = 128,
			StandardError = "fatal: not a git repository: sub/../.git/modules/sub\n",
		};
		EchoBuilder builder = new(runner, temporary.Root);

		GitCommandException exception = await Assert.ThrowsExactlyAsync<GitCommandException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false)).ConfigureAwait(false);

		StringAssert.Contains(exception.Message, "sub/../.git/modules/sub");
	}

	[TestMethod]
	public async Task ExecuteThrowsRepositoryNotFoundWhenTheRepositoryOwnGitDirectoryIsMissingAsync()
	{
		// A linked worktree whose administrative directory has gone: the gitfile names it, and git
		// reports exactly that path.
		using TemporaryRepository temporary = new();
		string gitDirectory = Path.Join(temporary.RootPath, "main", ".git", "worktrees", "wt");
		temporary.WriteFile(".git", $"gitdir: {gitDirectory}\n");

		RecordingGitProcessRunner runner = new()
		{
			ExitCode = 128,
			StandardError = $"fatal: not a git repository: {gitDirectory}\n",
		};
		EchoBuilder builder = new(runner, Path.Join(temporary.RootPath, "deep").As<AbsoluteDirectoryPath>());

		await Assert.ThrowsExactlyAsync<GitRepositoryNotFoundException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false)).ConfigureAwait(false);
	}

	[TestMethod]
	public void ClassifiesBothDiagnosticForms()
	{
		using TemporaryRepository temporary = new();
		_ = Directory.CreateDirectory(Path.Join(temporary.RootPath, ".git"));

		Assert.IsTrue(GitCommandBuilder<string>.IsRepositoryNotFound(
			"fatal: not a git repository (or any of the parent directories): .git\n", temporary.Root));
		Assert.IsTrue(GitCommandBuilder<string>.IsRepositoryNotFound(
			"fatal: not a git repository (or any parent up to mount point /mnt)\n", temporary.Root));
		Assert.IsTrue(GitCommandBuilder<string>.IsRepositoryNotFound(
			$"fatal: not a git repository: {Path.Join(temporary.RootPath, ".git")}\n", temporary.Root));
		Assert.IsFalse(GitCommandBuilder<string>.IsRepositoryNotFound(
			"fatal: not a git repository: sub/../.git/modules/sub\n", temporary.Root));
		Assert.IsFalse(GitCommandBuilder<string>.IsRepositoryNotFound(
			"fatal: not a git repository: /elsewhere/.git/modules/sub\n", temporary.Root));
		Assert.IsFalse(GitCommandBuilder<string>.IsRepositoryNotFound("fatal: boom\n", temporary.Root));
	}

	[TestMethod]
	public void ClassifiesTheEdgeCasesOfTheGitfileForm()
	{
		// No scoped directory, or nothing named after the colon: nothing to compare, so the
		// phrase keeps its old meaning.
		Assert.IsTrue(GitCommandBuilder<string>.IsRepositoryNotFound("fatal: not a git repository: x\n", null));
		Assert.IsTrue(GitCommandBuilder<string>.IsRepositoryNotFound("fatal: not a git repository: \n", TestPaths.Root));
		Assert.IsTrue(GitCommandBuilder<string>.IsRepositoryNotFound("fatal: not a git repository\n", TestPaths.Root));

		// A gitfile with no gitdir line gives nothing to compare against either.
		using TemporaryRepository broken = new();
		broken.WriteFile(".git", "not a gitfile\n");
		Assert.IsTrue(GitCommandBuilder<string>.IsRepositoryNotFound(
			"fatal: not a git repository: sub/../.git/modules/sub\n", broken.Root));

		// A relative gitdir, as a submodule checkout writes it, reported relative to the top of the
		// working tree from a subdirectory.
		using TemporaryRepository submodule = new();
		submodule.WriteFile(".git", "gitdir: ../.git/modules/sub\n");
		AbsoluteDirectoryPath deep = Path.Join(submodule.RootPath, "deep").As<AbsoluteDirectoryPath>();
		Assert.IsTrue(GitCommandBuilder<string>.IsRepositoryNotFound(
			"fatal: not a git repository: ../.git/modules/sub\n", deep));
		Assert.IsFalse(GitCommandBuilder<string>.IsRepositoryNotFound(
			"fatal: not a git repository: ../.git/modules/other\n", deep));
	}

	[TestMethod]
	public async Task TryExecuteReturnsErrorInsteadOfThrowingAsync()
	{
		RecordingGitProcessRunner runner = new() { ExitCode = 1, StandardError = "boom" };
		EchoBuilder builder = new(runner, TestPaths.Root);

		GitResult<string> result = await builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsFalse(result.Success);
		Assert.AreEqual(1, result.Error?.ExitCode);
		Assert.AreEqual("boom", result.Error?.StandardError);
	}

	[TestMethod]
	public async Task TryExecuteReturnsValueOnSuccessAsync()
	{
		RecordingGitProcessRunner runner = new() { StandardOutput = "clean" };
		EchoBuilder builder = new(runner, TestPaths.Root);

		GitResult<string> result = await builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsTrue(result.Success);
		Assert.AreEqual("clean", result.Value);
		CollectionAssert.AreEqual(builder.BuildArguments().ToArray(), runner.LastArguments?.ToArray());
	}

	public TestContext TestContext { get; set; } = null!;
}
