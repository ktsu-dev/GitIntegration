// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

[TestClass]
public class GitCommandBuilderProgressTests
{
	/// <summary>A builder that exposes the protected progress seam so it can be set from a test.</summary>
	private sealed class ProgressBuilder(IGitProcessRunner runner) : GitCommandBuilder<string>(runner, repositoryPath: null)
	{
		internal void SetProgress(IProgress<string>? progress) => Progress = progress;

		protected override void AppendVerbArguments(ICollection<string> arguments) => arguments.Add("clone");

		protected override string ParseResult(GitProcessResult result) => result.StandardOutput;
	}

	[TestMethod]
	public async Task ForwardsTheProgressSinkIntoTheRequestAsync()
	{
		// RecordingGitProcessRunner replays its canned output through request.Progress, exactly as
		// the real runner streams chunks as git produces them. Before this seam existed no builder
		// could observe a long-running command's output until it exited.
		List<string> reported = [];
		RecordingGitProcessRunner runner = new() { StandardOutput = "Cloning into 'x'..." };
		ProgressBuilder builder = new(runner);
		builder.SetProgress(new Progress<string>(reported.Add));

		_ = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsNotNull(runner.LastRequest);
		Assert.IsNotNull(runner.LastRequest.Progress);
	}

	[TestMethod]
	public async Task LeavesTheRequestProgressNullWhenNoSinkIsSetAsync()
	{
		RecordingGitProcessRunner runner = new() { StandardOutput = "done" };
		ProgressBuilder builder = new(runner);

		_ = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsNotNull(runner.LastRequest);
		Assert.IsNull(runner.LastRequest.Progress);
	}

	[TestMethod]
	public async Task ForwardsTheProgressSinkFromTryExecuteTooAsync()
	{
		RecordingGitProcessRunner runner = new() { StandardOutput = "done" };
		ProgressBuilder builder = new(runner);
		builder.SetProgress(new Progress<string>(_ => { }));

		_ = await builder.TryExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsNotNull(runner.LastRequest);
		Assert.IsNotNull(runner.LastRequest.Progress);
	}

	public TestContext TestContext { get; set; } = null!;
}
