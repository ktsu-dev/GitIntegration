// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using ktsu.Semantics.Strings;

[TestClass]
public class GitTagListBuilderTests
{
	private const string Us = "\u001f";

	[TestMethod]
	public void BuildsTheForEachRefVectorOverTheTagNamespace()
	{
		RecordingGitProcessRunner runner = new();
		GitTagListBuilder builder = new(runner, TestPaths.Root);

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"for-each-ref",
			"--format=" + GitOutputFormats.ForEachTagFormat,
			"refs/tags",
		];
		CollectionAssert.AreEqual(expectedArguments, builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void DoesNotGuardALibraryConstantWithTheEndOfOptionsMarker()
	{
		// refs/tags is a library constant, not a caller-supplied operand, so no caller value can
		// reach that position and the marker would only add noise. GitBranchListBuilder makes the
		// same call for the same reason.
		RecordingGitProcessRunner runner = new();
		GitTagListBuilder builder = new(runner, TestPaths.Root);

		CollectionAssert.DoesNotContain(builder.BuildArguments().ToArray(), "--end-of-options");
	}

	[TestMethod]
	public async Task ParsesBothKindsOfTagAsync()
	{
		// Captured from git 2.43 with this library's own format. The annotated record's objecttype is
		// "tag" and its *objectname dereferences to the commit; the lightweight record's objecttype
		// is "commit" and its *objectname is empty.
		string output =
			"ann1" + Us + "d829cd4fe5eccc7b6fa171efdba141ff41f00a71" + Us + "tag" + Us +
			"3d61d00b68091ebb86034533f0267778e0addcf3" + Us + "annotated msg\n" +
			"light1" + Us + "3d61d00b68091ebb86034533f0267778e0addcf3" + Us + "commit" + Us + Us + "c1\n";

		RecordingGitProcessRunner runner = new() { StandardOutput = output };
		GitTagListBuilder builder = new(runner, TestPaths.Root);

		IReadOnlyList<GitTag> tags = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(2, tags.Count);

		Assert.AreEqual("ann1".As<GitTagName>(), tags[0].Name);
		Assert.IsTrue(tags[0].IsAnnotated);
		Assert.AreEqual("annotated msg", tags[0].Message);

		// Sha is the commit and ObjectSha is the tag object, so the two differ for an annotated tag.
		Assert.AreEqual("3d61d00b68091ebb86034533f0267778e0addcf3".As<GitCommitSha>(), tags[0].Sha);
		Assert.AreEqual("d829cd4fe5eccc7b6fa171efdba141ff41f00a71".As<GitCommitSha>(), tags[0].ObjectSha);

		Assert.AreEqual("light1".As<GitTagName>(), tags[1].Name);
		Assert.IsFalse(tags[1].IsAnnotated);
		Assert.AreEqual(tags[1].Sha, tags[1].ObjectSha);
	}

	[TestMethod]
	public async Task ReportsNoMessageForALightweightTagEvenThoughGitPrintsOneAsync()
	{
		// The trap this parser exists to close. git's %(contents:subject) falls through to the
		// commit's own subject when the reference names no tag object, so the field arrives populated
		// with text that reads like a tag message and is not one. "c1" below is the commit's subject.
		string output =
			"light1" + Us + "3d61d00b68091ebb86034533f0267778e0addcf3" + Us + "commit" + Us + Us + "c1\n";

		RecordingGitProcessRunner runner = new() { StandardOutput = output };
		GitTagListBuilder builder = new(runner, TestPaths.Root);

		IReadOnlyList<GitTag> tags = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsNull(tags[0].Message);
	}

	[TestMethod]
	public async Task KeepsASeparatorEmbeddedInAMessageInThatFieldAsync()
	{
		// The subject is the only field not covered by git's ban on control characters in a reference
		// name, and it is last, so a bounded split absorbs an embedded separator rather than letting
		// the field count shift and the record be rejected.
		string output =
			"ann1" + Us + "d829cd4fe5eccc7b6fa171efdba141ff41f00a71" + Us + "tag" + Us +
			"3d61d00b68091ebb86034533f0267778e0addcf3" + Us + "before" + Us + "after\n";

		RecordingGitProcessRunner runner = new() { StandardOutput = output };
		GitTagListBuilder builder = new(runner, TestPaths.Root);

		IReadOnlyList<GitTag> tags = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual("before" + Us + "after", tags[0].Message);
	}

	[TestMethod]
	public async Task ReportsAnEmptyListForARepositoryWithNoTagsAsync()
	{
		RecordingGitProcessRunner runner = new() { StandardOutput = string.Empty };
		GitTagListBuilder builder = new(runner, TestPaths.Root);

		IReadOnlyList<GitTag> tags = await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(0, tags.Count);
	}

	[TestMethod]
	public async Task ThrowsForAMalformedRecordAsync()
	{
		RecordingGitProcessRunner runner = new() { StandardOutput = "ann1" + Us + "deadbeef\n" };
		GitTagListBuilder builder = new(runner, TestPaths.Root);

		await Assert.ThrowsExactlyAsync<GitParseException>(
			async () => await builder.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	public TestContext TestContext { get; set; } = null!;
}

[TestClass]
public class GitTagWriteBuilderTests
{
	private static GitTagName Version => "v1.2.3".As<GitTagName>();

	[TestMethod]
	public void BuildsALightweightTagVector()
	{
		RecordingGitProcessRunner runner = new();
		GitTagCreateBuilder builder = new(runner, TestPaths.Root, Version);

		string[] arguments = [.. builder.BuildArguments()];

		CollectionAssert.Contains(arguments, "tag");
		CollectionAssert.DoesNotContain(arguments, "--annotate");
		Assert.AreEqual("v1.2.3", arguments[^1]);
	}

	[TestMethod]
	public void EmitsAnnotateAndMessageTogether()
	{
		// Either flag alone is a different command: --annotate with no message opens an editor that
		// output redirection makes impossible to answer, and --message alone relies on a side effect
		// git documents rather than promises.
		RecordingGitProcessRunner runner = new();
		GitTagCreateBuilder builder = new(runner, TestPaths.Root, Version);

		_ = builder.Annotating("release 1.2.3".As<GitCommitMessage>());

		string[] arguments = [.. builder.BuildArguments()];
		int annotate = Array.IndexOf(arguments, "--annotate");

		Assert.AreNotEqual(-1, annotate);
		Assert.AreEqual("--message", arguments[annotate + 1]);
		Assert.AreEqual("release 1.2.3", arguments[annotate + 2]);
	}

	[TestMethod]
	public void PutsTheNameBeforeTheTarget()
	{
		// git tag takes <name> then an optional <commit> positionally. Swapping them tags the wrong
		// object under the wrong name without complaint, so the order is pinned rather than assumed.
		RecordingGitProcessRunner runner = new();
		GitTagCreateBuilder builder = new(runner, TestPaths.Root, Version);

		_ = builder.At("main".As<GitRefName>());

		string[] arguments = [.. builder.BuildArguments()];
		int marker = Array.IndexOf(arguments, "--end-of-options");

		Assert.AreEqual("v1.2.3", arguments[marker + 1]);
		Assert.AreEqual("main", arguments[marker + 2]);
	}

	[TestMethod]
	public void KeepsEveryFlagBeforeTheEndOfOptionsMarker()
	{
		RecordingGitProcessRunner runner = new();
		GitTagCreateBuilder builder = new(runner, TestPaths.Root, Version);

		_ = builder.Force().Annotating("release".As<GitCommitMessage>());

		string[] arguments = [.. builder.BuildArguments()];
		int marker = Array.IndexOf(arguments, "--end-of-options");

		Assert.IsTrue(Array.IndexOf(arguments, "--force") < marker);
		Assert.IsTrue(Array.IndexOf(arguments, "--annotate") < marker);
	}

	[TestMethod]
	public void BuildsTheDeleteVector()
	{
		RecordingGitProcessRunner runner = new();
		GitTagDeleteBuilder builder = new(runner, TestPaths.Root, Version);

		string[] expectedArguments =
		[
			"-C", TestPaths.Root.WeakString,
			"--no-pager",
			"-c", "core.quotepath=false",
			"-c", "color.ui=false",
			"tag",
			"--delete",
			"--end-of-options",
			"v1.2.3",
		];
		CollectionAssert.AreEqual(expectedArguments, builder.BuildArguments().ToArray());
	}

	[TestMethod]
	public void ConfigurationMethodsReturnTheSameBuilderForChaining()
	{
		RecordingGitProcessRunner runner = new();
		GitTagCreateBuilder builder = new(runner, TestPaths.Root, Version);

		Assert.AreSame(builder, builder.At("main".As<GitRefName>()).Annotating("m".As<GitCommitMessage>()).Force());
	}

	[TestMethod]
	public void RejectsNullArguments()
	{
		RecordingGitProcessRunner runner = new();
		GitTagCreateBuilder builder = new(runner, TestPaths.Root, Version);

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new GitTagCreateBuilder(runner, TestPaths.Root, null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new GitTagDeleteBuilder(runner, TestPaths.Root, null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.At(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.Annotating(null!));
	}
}
