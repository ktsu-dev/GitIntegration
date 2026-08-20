// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using ktsu.Semantics.Strings;

[TestClass]
public class GitRemoteResultTests
{
	private static GitRefUpdate Update(GitRefUpdateKind kind) => new()
	{
		Kind = kind,
		Reference = "refs/heads/main".As<GitRefName>(),
		Summary = "[test]",
	};

	[TestMethod]
	public void AFetchWithNoUpdatesIsUpToDate()
	{
		// git prints nothing at all when a fetch changed nothing, so an empty list is the
		// ordinary success case rather than a sign that parsing failed.
		GitFetchResult result = new() { Updates = [], DetailAvailable = true };

		Assert.IsTrue(result.IsUpToDate);
	}

	[TestMethod]
	public void AFetchWithUpdatesIsNotUpToDate()
	{
		GitFetchResult result = new()
		{
			Updates = [Update(GitRefUpdateKind.FastForward)],
			DetailAvailable = true,
		};

		Assert.IsFalse(result.IsUpToDate);
	}

	[TestMethod]
	public void AFetchWithoutDetailIsNotReportedAsUpToDate()
	{
		// The dangerous confusion this guards against: on git older than 2.41 the update list is
		// empty because it could not be gathered, not because nothing happened. Reporting that as
		// "up to date" would be a silent lie.
		GitFetchResult result = new() { Updates = [], DetailAvailable = false };

		Assert.IsFalse(result.IsUpToDate);
	}

	[TestMethod]
	public void APushWithARejectedRefReportsRejections()
	{
		GitPushResult result = new()
		{
			Updates = [Update(GitRefUpdateKind.FastForward), Update(GitRefUpdateKind.Rejected)],
		};

		Assert.IsTrue(result.HasRejections);
	}

	[TestMethod]
	public void APushWithNoRejectedRefsReportsNone()
	{
		GitPushResult result = new()
		{
			Updates = [Update(GitRefUpdateKind.Created), Update(GitRefUpdateKind.UpToDate)],
		};

		Assert.IsFalse(result.HasRejections);
	}

	[TestMethod]
	public void OnlyARejectedUpdateIsRejected()
	{
		Assert.IsTrue(Update(GitRefUpdateKind.Rejected).IsRejected);
		Assert.IsFalse(Update(GitRefUpdateKind.FastForward).IsRejected);
		Assert.IsFalse(Update(GitRefUpdateKind.UpToDate).IsRejected);
		Assert.IsFalse(Update(GitRefUpdateKind.Removed).IsRejected);
	}

	[TestMethod]
	public void ARejectedPushExceptionCarriesTheParsedResult()
	{
		// The whole point of the type: a rejected push exits non-zero while printing exactly what
		// the caller wanted to know, so the detail must survive the throw.
		GitPushResult result = new() { Updates = [Update(GitRefUpdateKind.Rejected)] };

		GitPushRejectedException exception = new("rejected", 1, [], string.Empty, result);

		Assert.AreSame(result, exception.Result);
		Assert.AreEqual(1, exception.ExitCode);
	}

	[TestMethod]
	public void ARejectedPushExceptionHasNoResultWhenConstructedWithoutOne()
	{
		Assert.IsNull(new GitPushRejectedException("rejected").Result);
	}
}
