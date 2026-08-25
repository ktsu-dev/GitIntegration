// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using ktsu.Semantics.Strings;

[TestClass]
public class GitPullRequestTests
{
	[TestMethod]
	public void RejectsANonNumericPullRequestNumber() =>
		Assert.IsFalse(GitPullRequestNumber.TryCreate("not-a-number", out _));

	[TestMethod]
	public void AcceptsAPositivePullRequestNumber() =>
		Assert.IsTrue(GitPullRequestNumber.TryCreate("42", out _));

	[TestMethod]
	public void RejectsAnEmptyPullRequestNumber() =>
		// RegexMatch alone would let this through: it short-circuits to success on an empty
		// string, so HasNonWhitespaceContent is what actually catches an empty, required Number.
		Assert.IsFalse(GitPullRequestNumber.TryCreate(string.Empty, out _));

	[TestMethod]
	public void RejectsAnEmptyTitle() =>
		Assert.IsFalse(GitPullRequestTitle.TryCreate(string.Empty, out _));

	[TestMethod]
	public void RejectsAnEmptyAuthor() =>
		Assert.IsFalse(GitPullRequestAuthor.TryCreate(string.Empty, out _));

	[TestMethod]
	public void RejectsAnEmptyWebURI() =>
		Assert.IsFalse(GitPullRequestWebURI.TryCreate(string.Empty, out _));

	[TestMethod]
	public void CarriesEveryFieldItWasGiven()
	{
		GitPullRequest pullRequest = new()
		{
			Number = "42".As<GitPullRequestNumber>(),
			Title = "Add the thing".As<GitPullRequestTitle>(),
			Description = "Because the thing was missing.",
			SourceBranch = "feature/thing".As<GitBranchName>(),
			TargetBranch = "main".As<GitBranchName>(),
			Author = "example-user".As<GitPullRequestAuthor>(),
			State = GitPullRequestState.Open,
			IsDraft = true,
			WebURI = "https://example.invalid/pr/42".As<GitPullRequestWebURI>(),
			CreatedAt = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero),
		};

		Assert.AreEqual("42".As<GitPullRequestNumber>(), pullRequest.Number);
		Assert.AreEqual("Add the thing".As<GitPullRequestTitle>(), pullRequest.Title);
		Assert.AreEqual("Because the thing was missing.", pullRequest.Description);
		Assert.AreEqual("feature/thing".As<GitBranchName>(), pullRequest.SourceBranch);
		Assert.AreEqual("main".As<GitBranchName>(), pullRequest.TargetBranch);
		Assert.AreEqual("example-user".As<GitPullRequestAuthor>(), pullRequest.Author);
		Assert.AreEqual(GitPullRequestState.Open, pullRequest.State);
		Assert.IsTrue(pullRequest.IsDraft);
		Assert.AreEqual("https://example.invalid/pr/42".As<GitPullRequestWebURI>(), pullRequest.WebURI);
		Assert.AreEqual(new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero), pullRequest.CreatedAt);
	}

	[TestMethod]
	public void LeavesTheOptionalFieldsNullWhenNotSupplied()
	{
		GitPullRequest pullRequest = new()
		{
			Number = "1".As<GitPullRequestNumber>(),
			Title = "t".As<GitPullRequestTitle>(),
			SourceBranch = "a".As<GitBranchName>(),
			TargetBranch = "b".As<GitBranchName>(),
			State = GitPullRequestState.Closed,
		};

		// null is "not known", never "known to be empty". That distinction is the whole point:
		// a field a host genuinely lacks and a field a provider failed to read both read as null
		// here, so a provider must never substitute an empty value for one it could not obtain and
		// make the two indistinguishable from a real, populated answer.
		Assert.IsNull(pullRequest.Description);
		Assert.IsNull(pullRequest.Author);
		Assert.IsNull(pullRequest.WebURI);
		Assert.IsNull(pullRequest.CreatedAt);
		Assert.IsFalse(pullRequest.IsDraft);
	}
}
