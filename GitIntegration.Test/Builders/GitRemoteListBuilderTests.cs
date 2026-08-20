// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Collections.Generic;
using System.Threading.Tasks;

[TestClass]
public class GitRemoteListBuilderTests
{
	[TestMethod]
	public void BuildsTheRemoteListVector()
	{
		RecordingGitProcessRunner runner = new();
		GitRemoteListBuilder builder = new(runner, TestPaths.Root);

		IReadOnlyList<string> arguments = builder.BuildArguments();

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"remote",
			"-v",
		];
		CollectionAssert.AreEqual(expectedArguments, arguments.ToArray());
	}

	[TestMethod]
	public async Task ExecuteParsesTheRemotesAsync()
	{
		RecordingGitProcessRunner runner = new()
		{
			StandardOutput =
				"origin\thttps://example.com/repo.git (fetch)\n" +
				"origin\thttps://example.com/repo.git (push)\n",
		};
		GitRemoteListBuilder builder = new(runner, TestPaths.Root);

		IReadOnlyList<GitRemote> remotes =
			await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(1, remotes.Count);
	}

	public TestContext TestContext { get; set; } = null!;
}
