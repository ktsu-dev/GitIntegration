// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Strings;

[TestClass]
public sealed class GitPullRequestCreateBuilderTests
{
	private static GitPullRequest CannedPullRequest => new()
	{
		Number = "1".As<GitPullRequestNumber>(),
		Title = "t".As<GitPullRequestTitle>(),
		SourceBranch = "a".As<GitBranchName>(),
		TargetBranch = "b".As<GitBranchName>(),
		State = GitPullRequestState.Open,
	};

	private static (GitPullRequestCreateBuilder Builder, Func<GitPullRequestSpecification?> Captured) Recording()
	{
		GitPullRequestSpecification? captured = null;
		GitPullRequestCreateBuilder builder = new((specification, _) =>
		{
			captured = specification;
			return Task.FromResult(CannedPullRequest);
		});

		return (builder, () => captured);
	}

	[TestMethod]
	public async Task PassesEveryConfiguredValueToTheExecuteDelegateAsync()
	{
		(GitPullRequestCreateBuilder builder, Func<GitPullRequestSpecification?> captured) = Recording();

		_ = await builder
			.From("feature/x".As<GitBranchName>())
			.Into("main".As<GitBranchName>())
			.Titled("Add x".As<GitPullRequestTitle>())
			.Describing("because")
			.AsDraft()
			.ExecuteAsync(TestContext.CancellationTokenSource.Token)
			.ConfigureAwait(false);

		GitPullRequestSpecification specification = captured()!;
		Assert.AreEqual("feature/x".As<GitBranchName>(), specification.Source);
		Assert.AreEqual("main".As<GitBranchName>(), specification.Target);
		Assert.AreEqual("Add x".As<GitPullRequestTitle>(), specification.Title);
		Assert.AreEqual("because", specification.Description);
		Assert.IsTrue(specification.IsDraft);
	}

	[TestMethod]
	public async Task DefaultsDescriptionToNullAndDraftToFalseAsync()
	{
		(GitPullRequestCreateBuilder builder, Func<GitPullRequestSpecification?> captured) = Recording();

		_ = await builder
			.From("a".As<GitBranchName>()).Into("b".As<GitBranchName>()).Titled("t".As<GitPullRequestTitle>())
			.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.IsNull(captured()!.Description);
		Assert.IsFalse(captured()!.IsDraft);
	}

	[TestMethod]
	public async Task ReturnsWhatTheExecuteDelegateReturnedAsync()
	{
		(GitPullRequestCreateBuilder builder, _) = Recording();

		GitPullRequest result = await builder
			.From("a".As<GitBranchName>()).Into("b".As<GitBranchName>()).Titled("t".As<GitPullRequestTitle>())
			.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual(CannedPullRequest, result);
	}

	[TestMethod]
	public async Task ProviderRoutesTheBuilderToItsCoreMethodAsync()
	{
		RecordingProvider provider = new() { Owner = "contoso".As<GitProviderOwner>() };

		_ = await provider.CreatePullRequest("repo".As<GitRepositoryName>())
			.From("a".As<GitBranchName>()).Into("b".As<GitBranchName>()).Titled("t".As<GitPullRequestTitle>())
			.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false);

		Assert.AreEqual("repo".As<GitRepositoryName>(), provider.Repository);
		Assert.AreEqual("a".As<GitBranchName>(), provider.Specification!.Source);
		Assert.AreEqual("b".As<GitBranchName>(), provider.Specification!.Target);
		Assert.AreEqual("t".As<GitPullRequestTitle>(), provider.Specification!.Title);
	}

	[TestMethod]
	public async Task ThrowsWhenTheSourceBranchIsMissingAsync()
	{
		(GitPullRequestCreateBuilder builder, _) = Recording();

		InvalidOperationException exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
			async () => await builder
				.Into("b".As<GitBranchName>()).Titled("t".As<GitPullRequestTitle>())
				.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		StringAssert.Contains(exception.Message, "From");
	}

	[TestMethod]
	public async Task ThrowsWhenTheTargetBranchIsMissingAsync()
	{
		(GitPullRequestCreateBuilder builder, _) = Recording();

		InvalidOperationException exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
			async () => await builder
				.From("a".As<GitBranchName>()).Titled("t".As<GitPullRequestTitle>())
				.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		StringAssert.Contains(exception.Message, "Into");
	}

	[TestMethod]
	public async Task ThrowsWhenTheTitleIsMissingAsync()
	{
		(GitPullRequestCreateBuilder builder, _) = Recording();

		InvalidOperationException exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
			async () => await builder
				.From("a".As<GitBranchName>()).Into("b".As<GitBranchName>())
				.ExecuteAsync(TestContext.CancellationTokenSource.Token).ConfigureAwait(false))
			.ConfigureAwait(false);

		StringAssert.Contains(exception.Message, "Titled");
	}

	[TestMethod]
	public void RejectsANullSourceBranch()
	{
		(GitPullRequestCreateBuilder builder, _) = Recording();

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.From(null!));
	}

	[TestMethod]
	public void RejectsANullTargetBranch()
	{
		(GitPullRequestCreateBuilder builder, _) = Recording();

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.Into(null!));
	}

	[TestMethod]
	public void RejectsANullTitle()
	{
		(GitPullRequestCreateBuilder builder, _) = Recording();

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.Titled(null!));
	}

	[TestMethod]
	public void RejectsANullDescription()
	{
		(GitPullRequestCreateBuilder builder, _) = Recording();

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = builder.Describing(null!));
	}

	/// <summary>
	/// Records the repository and specification handed to <c>CreatePullRequestCoreAsync</c>, so the
	/// routing test can assert on what <see cref="GitProvider.CreatePullRequest(GitRepositoryName)"/> actually passed
	/// through, not merely that it was called.
	/// </summary>
	private sealed class RecordingProvider : GitProvider
	{
		public string? Repository { get; private set; }

		public GitPullRequestSpecification? Specification { get; private set; }

		public override GitProviderName Name => "RecordingProvider".As<GitProviderName>();

		// Never reached: these tests either inject a Handler or never issue a request at all. The
		// member is abstract so that each real provider has to name its own shared transport rather
		// than inherit one, which is the point of it existing.
		private protected override HttpMessageHandler DefaultHandler =>
			throw new NotSupportedException("Not exercised by these tests.");

		public override Task<IReadOnlyList<GitRepository>> GetRepositoriesAsync(CancellationToken cancellationToken = default) =>
			throw new NotSupportedException("Not exercised by the routing test.");

		internal override Task<IReadOnlyList<GitPullRequest>> GetPullRequestsCoreAsync(string repositoryIdentifier, CancellationToken cancellationToken) =>
			throw new NotSupportedException("Not exercised by the routing test.");

		internal override Task<GitPullRequest> CreatePullRequestCoreAsync(string repositoryIdentifier, GitPullRequestSpecification specification, CancellationToken cancellationToken)
		{
			Repository = repositoryIdentifier;
			Specification = specification;
			return Task.FromResult(CannedPullRequest);
		}
	}

	public TestContext TestContext { get; set; } = null!;
}
