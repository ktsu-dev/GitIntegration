// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.Collections.Generic;
using System.Threading.Tasks;

[TestClass]
public class GitStatusBuilderTests
{
	private const string Nul = "\u0000";

	[TestMethod]
	public void BuildsTheDefaultStatusVector()
	{
		RecordingGitProcessRunner runner = new();
		GitStatusBuilder builder = new(runner, TestPaths.Root);

		IReadOnlyList<string> arguments = builder.BuildArguments();

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"status",
			"--porcelain=v2",
			"--branch",
			"-z",
		];
		CollectionAssert.AreEqual(expectedArguments, arguments.ToArray());
	}

	[TestMethod]
	public void MapsTheUntrackedFilesModeToItsOption()
	{
		RecordingGitProcessRunner runner = new();

		GitStatusBuilder none = new(runner, TestPaths.Root);
		_ = none.WithUntrackedFiles(GitUntrackedFilesMode.No);
		CollectionAssert.Contains(none.BuildArguments().ToArray(), "--untracked-files=no");

		GitStatusBuilder normal = new(runner, TestPaths.Root);
		_ = normal.WithUntrackedFiles(GitUntrackedFilesMode.Normal);
		CollectionAssert.Contains(normal.BuildArguments().ToArray(), "--untracked-files=normal");

		GitStatusBuilder all = new(runner, TestPaths.Root);
		_ = all.WithUntrackedFiles(GitUntrackedFilesMode.All);
		CollectionAssert.Contains(all.BuildArguments().ToArray(), "--untracked-files=all");
	}

	[TestMethod]
	public void AddsTheIgnoredOptionOnlyWhenAsked()
	{
		RecordingGitProcessRunner runner = new();

		GitStatusBuilder without = new(runner, TestPaths.Root);
		CollectionAssert.DoesNotContain(without.BuildArguments().ToArray(), "--ignored=matching");

		GitStatusBuilder with = new(runner, TestPaths.Root);
		_ = with.IncludeIgnored();
		CollectionAssert.Contains(with.BuildArguments().ToArray(), "--ignored=matching");
	}

	[TestMethod]
	public void ConfigurationMethodsReturnTheSameBuilderForChaining()
	{
		RecordingGitProcessRunner runner = new();
		GitStatusBuilder builder = new(runner, TestPaths.Root);

		IGitStatusBuilder chained = builder
			.WithUntrackedFiles(GitUntrackedFilesMode.All)
			.IncludeIgnored();

		Assert.AreSame(builder, chained);
	}

	[TestMethod]
	public void RejectsAnUnrecognisedUntrackedFilesMode()
	{
		RecordingGitProcessRunner runner = new();
		GitStatusBuilder builder = new(runner, TestPaths.Root);

		// A value outside the enum reaches the builder whenever a caller casts an int, and mapping
		// it to a silent default would send git an option the caller never asked for.
		Assert.ThrowsExactly<System.ComponentModel.InvalidEnumArgumentException>(() => _ = builder.WithUntrackedFiles((GitUntrackedFilesMode)99));
	}

	[TestMethod]
	public async Task ExecuteParsesTheStatusAsync()
	{
		RecordingGitProcessRunner runner = new()
		{
			StandardOutput = "# branch.head main" + Nul + "? untracked.txt" + Nul,
		};
		GitStatusBuilder builder = new(runner, TestPaths.Root);

		GitStatus status = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsFalse(status.IsClean);
		Assert.AreEqual(1, status.Entries.Count);
	}

	public TestContext TestContext { get; set; } = null!;
}
