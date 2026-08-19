// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Collections.Generic;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;

[TestClass]
public class GitCommandBuilderTests
{
	/// <summary>A minimal concrete builder, exercising only the base class behaviour.</summary>
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
	}

	public TestContext TestContext { get; set; } = null!;
}
