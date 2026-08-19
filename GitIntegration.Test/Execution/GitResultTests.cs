// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

[TestClass]
public class GitResultTests
{
	[TestMethod]
	public void FromValueReportsSuccess()
	{
		GitResult<string> result = GitResult<string>.FromValue("ok");

		Assert.IsTrue(result.Success);
		Assert.AreEqual("ok", result.Value);
		Assert.IsNull(result.Error);
	}

	[TestMethod]
	public void FromErrorReportsFailure()
	{
		GitCommandError error = new()
		{
			ExitCode = 128,
			Arguments = ["status"],
			StandardError = "fatal: not a git repository",
		};

		GitResult<string> result = GitResult<string>.FromError(error);

		Assert.IsFalse(result.Success);
		Assert.IsNull(result.Value);
		Assert.AreEqual(128, result.Error?.ExitCode);
	}

	[TestMethod]
	public void ProcessResultReportsSuccessOnZeroExitCode()
	{
		GitProcessResult result = new()
		{
			ExitCode = 0,
			StandardOutput = string.Empty,
			StandardError = string.Empty,
			Arguments = ["status"],
		};

		Assert.IsTrue(result.Success);
	}

	[TestMethod]
	public void ProcessResultReportsFailureOnNonZeroExitCode()
	{
		GitProcessResult result = new()
		{
			ExitCode = 1,
			StandardOutput = string.Empty,
			StandardError = string.Empty,
			Arguments = ["status"],
		};

		Assert.IsFalse(result.Success);
	}

	[TestMethod]
	public void CommandExceptionCarriesDiagnosticContext()
	{
		GitCommandException exception = new("git failed", 128, ["status"], "fatal: not a git repository");

		Assert.AreEqual(128, exception.ExitCode);
		Assert.AreEqual("fatal: not a git repository", exception.StandardError);
		string[] expectedArguments = ["status"];
		CollectionAssert.AreEqual(expectedArguments, exception.Arguments.ToArray());
	}

	[TestMethod]
	public void RepositoryNotFoundIsACommandException()
	{
		GitRepositoryNotFoundException exception = new("not a repo", 128, ["status"], "fatal:");

		Assert.IsInstanceOfType<GitCommandException>(exception);
	}
}
