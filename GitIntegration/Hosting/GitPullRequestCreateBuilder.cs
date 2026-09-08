// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Collects a pull request's details, then submits them through a provider-supplied delegate.
/// </summary>
/// <remarks>
/// Holds a delegate rather than a <see cref="GitProvider"/> reference, so this builder stays
/// ignorant of providers entirely: its own tests need no provider, only a recording lambda. A
/// provider supplies <c>(specification, ct) =&gt; CreatePullRequestCoreAsync(repositoryName,
/// specification, ct)</c>, closing over the repository it was asked to build a pull request for.
/// An instance is single-use and not thread-safe, matching every builder in the local layer.
/// </remarks>
/// <param name="execute">Submits the finished specification to the host.</param>
internal sealed class GitPullRequestCreateBuilder(Func<GitPullRequestSpecification, CancellationToken, Task<GitPullRequest>> execute) : IGitPullRequestCreateBuilder
{
	private readonly Func<GitPullRequestSpecification, CancellationToken, Task<GitPullRequest>> _execute = Ensure.NotNull(execute);

	private GitBranchName? _source;
	private GitBranchName? _target;
	private GitPullRequestTitle? _title;
	private string? _description;
	private bool _isDraft;

	/// <inheritdoc/>
	public IGitPullRequestCreateBuilder From(GitBranchName source)
	{
		_source = Ensure.NotNull(source);
		return this;
	}

	/// <inheritdoc/>
	public IGitPullRequestCreateBuilder Into(GitBranchName target)
	{
		_target = Ensure.NotNull(target);
		return this;
	}

	/// <inheritdoc/>
	public IGitPullRequestCreateBuilder Titled(GitPullRequestTitle title)
	{
		_title = Ensure.NotNull(title);
		return this;
	}

	/// <inheritdoc/>
	public IGitPullRequestCreateBuilder Describing(string description)
	{
		_description = Ensure.NotNull(description);
		return this;
	}

	/// <inheritdoc/>
	public IGitPullRequestCreateBuilder AsDraft()
	{
		_isDraft = true;
		return this;
	}

	/// <inheritdoc/>
	public async Task<GitPullRequest> ExecuteAsync(CancellationToken cancellationToken = default)
	{
		// Three near-identical blocks rather than one loop or a shared helper, deliberately. Each
		// message names the member that is missing and the method that sets it, and the tests pin
		// them individually — collapsing them would either lose that specificity or reintroduce it as
		// a table, which is the same three facts with a layer of indirection over them.
		if (_source is null)
		{
			throw new InvalidOperationException(
				"The pull request has no source branch. Call From to set one.");
		}

		if (_target is null)
		{
			throw new InvalidOperationException(
				"The pull request has no target branch. Call Into to set one.");
		}

		if (_title is null)
		{
			throw new InvalidOperationException(
				"The pull request has no title. Call Titled to set one.");
		}

		GitPullRequestSpecification specification = new(_source, _target, _title, _description, _isDraft);
		return await _execute(specification, cancellationToken).ConfigureAwait(false);
	}
}
