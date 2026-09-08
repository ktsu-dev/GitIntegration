// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

using Testably.Abstractions.Testing;

[TestClass]
public class GitSubmoduleListBuilderTests
{
	[TestMethod]
	public void BuildsThePlumbingVectorRatherThanTheWrapperOne()
	{
		// ls-files is plumbing and NUL-terminated, so a submodule path may contain anything at all.
		// submodule status has no -z form and no stability guarantee, which is why it is consulted
		// only for state, never for the authoritative path list.
		RecordingGitProcessRunner runner = new();
		GitSubmoduleListBuilder builder = new(runner, TestPaths.Root);

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"ls-files",
			"--stage",
			"-z",
		];
		CollectionAssert.AreEqual(expectedArguments, builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public async Task SkipsEverythingThatIsNotAGitlinkAsync()
	{
		// ls-files lists the whole index, so the 160000 mode filter is what turns it into a submodule
		// listing. A repository with no submodules also costs only this one invocation.
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: "100644 abc1234000000000000000000000000000000000 0\tREADME.md\0" +
				"100755 def5678000000000000000000000000000000000 0\tbuild.sh\0");
		GitSubmoduleListBuilder builder = new(runner, TestPaths.Root);

		IReadOnlyList<GitSubmodule> submodules =
			await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(0, submodules.Count);
	}

	[TestMethod]
	public async Task ResolvesStateFromTheStatusInvocationAsync()
	{
		// Captured from git 2.43. The leading space marks a submodule whose checkout matches the
		// gitlink the superproject records.
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: "160000 1f2bee80cfcf06ee5ba820b17fe3b6ddca460915 0\tlibs/sub\0")
			.Then(standardOutput: " 1f2bee80cfcf06ee5ba820b17fe3b6ddca460915 libs/sub (heads/master)\n");
		GitSubmoduleListBuilder builder = new(runner, TestPaths.Root);

		IReadOnlyList<GitSubmodule> submodules =
			await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(1, submodules.Count);
		Assert.AreEqual("libs/sub".As<RelativeDirectoryPath>(), submodules[0].Path);
		Assert.AreEqual(GitSubmoduleState.InSync, submodules[0].State);
		Assert.AreEqual(submodules[0].Sha, submodules[0].CheckedOutSha);
		Assert.AreEqual("heads/master", submodules[0].Describe);
	}

	[TestMethod]
	public async Task KeepsTheRecordedGitlinkApartFromTheCheckedOutCommitAsync()
	{
		// The "+" marker means a different commit is checked out than the superproject records, and
		// the two commands genuinely report different ids in that case: ls-files reports the gitlink,
		// submodule status reports the checkout. Collapsing them into one field would lose the fact
		// that the superproject is out of date.
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: "160000 1f2bee80cfcf06ee5ba820b17fe3b6ddca460915 0\tlibs/sub\0")
			.Then(standardOutput: "+120669ec6b336c651886335053ac0644d7821e09 libs/sub (heads/master)\n");
		GitSubmoduleListBuilder builder = new(runner, TestPaths.Root);

		IReadOnlyList<GitSubmodule> submodules =
			await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(GitSubmoduleState.DifferentCommit, submodules[0].State);
		Assert.AreEqual("1f2bee80cfcf06ee5ba820b17fe3b6ddca460915".As<GitCommitSha>(), submodules[0].Sha);
		Assert.AreEqual("120669ec6b336c651886335053ac0644d7821e09".As<GitCommitSha>(), submodules[0].CheckedOutSha);
	}

	[TestMethod]
	public async Task ReportsNoCheckedOutCommitForAnUninitialisedSubmoduleAsync()
	{
		// git prints the recorded gitlink again for an uninitialised submodule, which would make it
		// indistinguishable from a synchronised one if it were reported verbatim. Nothing is checked
		// out there, so nothing is reported. Note there is no describe suffix either.
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: "160000 1f2bee80cfcf06ee5ba820b17fe3b6ddca460915 0\tlibs/sub\0")
			.Then(standardOutput: "-1f2bee80cfcf06ee5ba820b17fe3b6ddca460915 libs/sub\n");
		GitSubmoduleListBuilder builder = new(runner, TestPaths.Root);

		IReadOnlyList<GitSubmodule> submodules =
			await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(GitSubmoduleState.Uninitialised, submodules[0].State);
		Assert.IsNull(submodules[0].CheckedOutSha);
		Assert.IsNull(submodules[0].Describe);
	}

	[TestMethod]
	public async Task ReadsAPathContainingTheDescribeDelimiterAsync()
	{
		// The reason this parser matches lines against known paths instead of splitting them. A
		// submodule at "libs/sub (old)" produces a status line whose path and describe suffix cannot
		// be told apart by inspection — but the path is already known exactly from ls-files, so no
		// guess is needed.
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: "160000 1f2bee80cfcf06ee5ba820b17fe3b6ddca460915 0\tlibs/sub (old)\0")
			.Then(standardOutput: " 1f2bee80cfcf06ee5ba820b17fe3b6ddca460915 libs/sub (old) (heads/master)\n");
		GitSubmoduleListBuilder builder = new(runner, TestPaths.Root);

		IReadOnlyList<GitSubmodule> submodules =
			await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual("libs/sub (old)".As<RelativeDirectoryPath>(), submodules[0].Path);
		Assert.AreEqual(GitSubmoduleState.InSync, submodules[0].State);
		Assert.AreEqual("heads/master", submodules[0].Describe);
	}

	[TestMethod]
	public async Task KeepsAGitlinkWithNoStatusLineAsUnknownAsync()
	{
		// A submodule removed from .gitmodules while its gitlink remains. Dropping the entry would
		// hide it from a caller deciding whether the directory on disk is safe to delete, which is
		// exactly the question this verb exists to answer.
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: "160000 1f2bee80cfcf06ee5ba820b17fe3b6ddca460915 0\tlibs/orphan\0")
			.Then(standardOutput: string.Empty);
		GitSubmoduleListBuilder builder = new(runner, TestPaths.Root);

		IReadOnlyList<GitSubmodule> submodules =
			await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(1, submodules.Count);
		Assert.AreEqual(GitSubmoduleState.Unknown, submodules[0].State);
		Assert.IsNull(submodules[0].CheckedOutSha);
	}

	[TestMethod]
	public async Task KeepsTheGitlinksWhenTheStatusWrapperFailsAsync()
	{
		// The gitlinks are the authoritative half of the answer and are already in hand, so failing
		// the whole listing because the wrapper could not run would discard a correct result over a
		// missing embellishment. Unknown already means exactly "git did not say".
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: "160000 1f2bee80cfcf06ee5ba820b17fe3b6ddca460915 0\tlibs/sub\0")
			.Then(standardError: "fatal: not a git repository\n", exitCode: 128);
		GitSubmoduleListBuilder builder = new(runner, TestPaths.Root);

		IReadOnlyList<GitSubmodule> submodules =
			await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(1, submodules.Count);
		Assert.AreEqual(GitSubmoduleState.Unknown, submodules[0].State);
	}

	[TestMethod]
	public void MatchesAStatusLineAgainstGitsOwnSpellingOfThePath()
	{
		// git prints a path with forward slashes on every platform, but RelativeDirectoryPath
		// canonicalises to the *platform's* separator — so on Windows the gitlink is "libs\sub" here
		// and "libs/sub" in the status output, and matching the two naively fails. That left every
		// submodule Unknown on Windows and nowhere else, which no POSIX run could ever catch.
		//
		// This assertion is trivially satisfied on a POSIX host, where the two spellings coincide, and
		// load-bearing on Windows, where CI runs it — the only place the bug can appear. It is written
		// as a nested path so the separator is actually exercised there rather than absent.
		IReadOnlyList<GitSubmodule> gitlinks = GitSubmoduleParser.ParseGitlinks(
			"160000 1f2bee80cfcf06ee5ba820b17fe3b6ddca460915 0\tlibs/nested/sub\0");

		IReadOnlyList<GitSubmodule> resolved = GitSubmoduleParser.ApplyStatus(
			gitlinks,
			" 1f2bee80cfcf06ee5ba820b17fe3b6ddca460915 libs/nested/sub (heads/master)\n");

		Assert.AreEqual(1, resolved.Count);
		Assert.AreEqual(GitSubmoduleState.InSync, resolved[0].State);
		Assert.AreEqual("heads/master", resolved[0].Describe);
		Assert.AreEqual("libs/nested/sub".As<RelativeDirectoryPath>(), resolved[0].Path);
	}

	[TestMethod]
	public void ThrowsForAMalformedGitlinkRecord()
	{
		_ = Assert.ThrowsExactly<GitParseException>(
			() => _ = GitSubmoduleParser.ParseGitlinks("160000 abc 0 no-tab-here\0"));
	}

	[TestMethod]
	public void ThrowsForAMalformedStatusLine()
	{
		IReadOnlyList<GitSubmodule> gitlinks =
			GitSubmoduleParser.ParseGitlinks("160000 1f2bee80cfcf06ee5ba820b17fe3b6ddca460915 0\tlibs/sub\0");

		_ = Assert.ThrowsExactly<GitParseException>(
			() => _ = GitSubmoduleParser.ApplyStatus(gitlinks, "nospaceanywhere\n"));
	}

	public TestContext TestContext { get; set; } = null!;
}

[TestClass]
public class GitSubmoduleUpdateBuilderTests
{
	[TestMethod]
	public void BuildsTheDefaultUpdateVector()
	{
		RecordingGitProcessRunner runner = new();
		GitSubmoduleUpdateBuilder builder = new(runner, TestPaths.Root);

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"submodule",
			"update",
		];
		CollectionAssert.AreEqual(expectedArguments, builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void MapsEveryOptionToItsFlag()
	{
		RecordingGitProcessRunner runner = new();
		GitSubmoduleUpdateBuilder builder = new(runner, TestPaths.Root);

		_ = builder.Initialise().Recursive().FromRemote().Force().WithDepth(1);

		string[] arguments = [.. builder.BuildArguments()];

		CollectionAssert.Contains(arguments, "--init");
		CollectionAssert.Contains(arguments, "--recursive");
		CollectionAssert.Contains(arguments, "--remote");
		CollectionAssert.Contains(arguments, "--force");
		CollectionAssert.Contains(arguments, "--depth");
		CollectionAssert.Contains(arguments, "1");
	}

	[TestMethod]
	public void RejectsANonPositiveDepth()
	{
		RecordingGitProcessRunner runner = new();
		GitSubmoduleUpdateBuilder builder = new(runner, TestPaths.Root);

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = builder.WithDepth(0));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = builder.WithDepth(-1));
	}

	[TestMethod]
	public void RejectsANullProgressSink()
	{
		RecordingGitProcessRunner runner = new();
		GitSubmoduleUpdateBuilder builder = new(runner, TestPaths.Root);

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.ReportingProgress(null!));
	}

	[TestMethod]
	public void ConfigurationMethodsReturnTheSameBuilderForChaining()
	{
		RecordingGitProcessRunner runner = new();
		GitSubmoduleUpdateBuilder builder = new(runner, TestPaths.Root);

		Assert.AreSame(builder, builder.Initialise().Recursive().FromRemote().Force().WithDepth(1));
	}
}

[TestClass]
public class GitSubmoduleRecursionOptionTests
{
	[TestMethod]
	public void CloneAndCheckoutEmitAPlainFlag()
	{
		// Neither takes a value: a fresh clone has no prior state for an on-demand decision, and
		// git's own clone/checkout flags take none either.
		RecordingGitProcessRunner runner = new();

		GitCloneBuilder clone = new(
			runner,
			new FakeFileSystemProvider(new MockFileSystem()),
			"https://example.com/x.git".As<GitRepositoryRemotePath>(),
			TestPaths.Root);
		_ = clone.RecursingSubmodules();
		CollectionAssert.Contains(clone.BuildArguments().ToArray(), "--recurse-submodules");

		GitCheckoutBuilder checkout = new(runner, TestPaths.Root, "main".As<GitRefName>());
		_ = checkout.RecursingSubmodules();
		CollectionAssert.Contains(checkout.BuildArguments().ToArray(), "--recurse-submodules");
	}

	[TestMethod]
	public void FetchAndPullEmitTheValuedForm()
	{
		RecordingGitProcessRunner runner = new();

		foreach ((GitSubmoduleRecursion recursion, string expected) in new[]
		{
			(GitSubmoduleRecursion.No, "no"),
			(GitSubmoduleRecursion.Yes, "yes"),
			(GitSubmoduleRecursion.OnDemand, "on-demand"),
		})
		{
			GitFetchBuilder fetch = new(runner, TestPaths.Root);
			_ = fetch.RecursingSubmodules(recursion);
			CollectionAssert.Contains(fetch.BuildArguments().ToArray(), "--recurse-submodules=" + expected);

			GitPullBuilder pull = new(runner, TestPaths.Root);
			_ = pull.RecursingSubmodules(recursion);
			CollectionAssert.Contains(pull.BuildArguments().ToArray(), "--recurse-submodules=" + expected);
		}
	}

	[TestMethod]
	public void PushEmitsItsOwnValueSet()
	{
		// push spells the same flag name but means an unrelated thing, so it has its own enum whose
		// values git accepts and the other verbs' do not.
		RecordingGitProcessRunner runner = new();

		foreach ((GitSubmodulePushCheck check, string expected) in new[]
		{
			(GitSubmodulePushCheck.No, "no"),
			(GitSubmodulePushCheck.Check, "check"),
			(GitSubmodulePushCheck.OnDemand, "on-demand"),
			(GitSubmodulePushCheck.Only, "only"),
		})
		{
			GitPushBuilder push = new(runner, TestPaths.Root);
			_ = push.CheckingSubmodules(check);
			CollectionAssert.Contains(push.BuildArguments().ToArray(), "--recurse-submodules=" + expected);
		}
	}

	[TestMethod]
	public void RejectsAnUnrecognisedEnumValue()
	{
		RecordingGitProcessRunner runner = new();

		Assert.ThrowsExactly<InvalidEnumArgumentException>(
			() => _ = new GitFetchBuilder(runner, TestPaths.Root).RecursingSubmodules((GitSubmoduleRecursion)99));
		Assert.ThrowsExactly<InvalidEnumArgumentException>(
			() => _ = new GitPullBuilder(runner, TestPaths.Root).RecursingSubmodules((GitSubmoduleRecursion)99));
		Assert.ThrowsExactly<InvalidEnumArgumentException>(
			() => _ = new GitPushBuilder(runner, TestPaths.Root).CheckingSubmodules((GitSubmodulePushCheck)99));
	}

	[TestMethod]
	public void FetchDropsPorcelainWhenRecursingBecauseGitRefusesBothTogether()
	{
		// "fatal: options '--porcelain' and '--recurse-submodules' cannot be used together",
		// verified against git 2.43. Emitting both would turn every recursing fetch into a failure,
		// so the caller's request wins and the itemisation is given up.
		RecordingGitProcessRunner runner = new();
		GitFetchBuilder builder = new(runner, TestPaths.Root);

		_ = builder.RecursingSubmodules(GitSubmoduleRecursion.Yes);

		string[] arguments = [.. builder.BuildArguments()];

		CollectionAssert.DoesNotContain(arguments, "--porcelain");
		CollectionAssert.Contains(arguments, "--recurse-submodules=yes");
	}

	[TestMethod]
	public void FetchKeepsPorcelainWhenNotRecursing()
	{
		RecordingGitProcessRunner runner = new();
		GitFetchBuilder builder = new(runner, TestPaths.Root);

		CollectionAssert.Contains(builder.BuildArguments().ToArray(), "--porcelain");
	}

	[TestMethod]
	public async Task FetchReportsDetailUnavailableWhenRecursingAsync()
	{
		// The itemisation is genuinely gone, and DetailAvailable is what says so — an empty Updates
		// list must never be read as "nothing changed".
		ScriptedGitProcessRunner runner = new ScriptedGitProcessRunner()
			.Then(standardOutput: "git version 2.43.0\n")
			.Then(standardOutput: string.Empty);
		GitFetchBuilder builder = new(runner, TestPaths.Root);

		_ = builder.RecursingSubmodules(GitSubmoduleRecursion.OnDemand);

		GitFetchResult result = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsFalse(result.DetailAvailable);
		Assert.AreEqual(0, result.Updates.Count);
		Assert.IsFalse(result.IsUpToDate);
	}

	public TestContext TestContext { get; set; } = null!;
}
