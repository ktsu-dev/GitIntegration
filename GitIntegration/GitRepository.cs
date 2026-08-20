// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;

/// <summary>
/// Represents a git repository: where its working copy is, and what is known about the host it
/// came from.
/// </summary>
public class GitRepository
{
	/// <summary>
	/// Gets the local filesystem path where the repository is, or is intended to be, cloned.
	/// </summary>
	public required AbsoluteDirectoryPath LocalPath { get; init; }

	/// <summary>
	/// Gets the repository name, or <see langword="null"/> when it is not known.
	/// </summary>
	public GitRepositoryName? Name { get; init; }

	/// <summary>
	/// Gets the browser-facing URI, or <see langword="null"/> when it is not known.
	/// </summary>
	public GitRepositoryWebURI? WebURI { get; init; }

	/// <summary>
	/// Gets the remote path, or <see langword="null"/> when it is not known.
	/// </summary>
	public GitRepositoryRemotePath? RemotePath { get; init; }

	/// <summary>
	/// Gets the runner this repository's verbs execute through, or <see langword="null"/> when this
	/// value carries hosting metadata only.
	/// </summary>
	/// <remarks>
	/// Nullable for the same reason the metadata is. A repository produced by
	/// <see cref="IGitClient.OpenAsync"/> or <see cref="IGitClient.DiscoverAsync"/> has one; a
	/// repository produced by a hosting provider describes something that may not exist on disk
	/// yet and has none. Calling a verb without one throws
	/// <see cref="InvalidOperationException"/> rather than failing later inside git.
	/// </remarks>
	public IGitProcessRunner? ProcessRunner { get; init; }

	/// <summary>
	/// Decides whether <see cref="LocalPath"/> currently holds a git working tree.
	/// </summary>
	/// <param name="cancellationToken">Cancels the invocation.</param>
	/// <returns><see langword="true"/> when the path is inside a working tree.</returns>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public Task<bool> IsClonedAsync(CancellationToken cancellationToken = default)
	{
		// Validated here rather than in the async body, matching GitClient's wrapper/core split, so
		// a metadata-only repository throws from the call itself rather than only once the returned
		// task is awaited.
		IGitProcessRunner runner = RequireRunner();

		return IsClonedCoreAsync(runner, cancellationToken);
	}

	private Task<bool> IsClonedCoreAsync(IGitProcessRunner runner, CancellationToken cancellationToken) =>
		GitProbes.IsWorkTreeAsync(runner, LocalPath, cancellationToken);

	/// <summary>Reports the working tree and index state.</summary>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public IGitStatusBuilder Status() => new GitStatusBuilder(RequireRunner(), LocalPath);

	/// <summary>Lists commits.</summary>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public IGitLogBuilder Log() => new GitLogBuilder(RequireRunner(), LocalPath);

	/// <summary>Lists the paths that differ between two states of the repository.</summary>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public IGitDiffBuilder Diff() => new GitDiffBuilder(RequireRunner(), LocalPath);

	/// <summary>Resolves a revision to the object id it names.</summary>
	/// <param name="revision">The revision to resolve.</param>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="revision"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public IGitRevParseBuilder RevParse(GitRefName revision)
	{
		// Argument validation before RequireRunner(): left-to-right evaluation would otherwise
		// report the missing runner for a null revision on a metadata-only repository, which is the
		// wrong diagnostic for what the caller got wrong.
		Ensure.NotNull(revision);

		return new GitRevParseBuilder(RequireRunner(), LocalPath, revision);
	}

	/// <summary>Lists branch references.</summary>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public IGitBranchListBuilder Branches() => new GitBranchListBuilder(RequireRunner(), LocalPath);

	/// <summary>Lists the configured remotes.</summary>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public IGitRemoteListBuilder Remotes() => new GitRemoteListBuilder(RequireRunner(), LocalPath);

	/// <summary>Stages changes for the next commit.</summary>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public IGitAddBuilder Add() => new GitAddBuilder(RequireRunner(), LocalPath);

	/// <summary>Records the staged changes as a new commit.</summary>
	/// <param name="message">The commit subject.</param>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public IGitCommitBuilder Commit(GitCommitMessage message)
	{
		// Argument validation precedes the state check so a null argument is reported as such,
		// rather than as a missing runner.
		Ensure.NotNull(message);
		return new GitCommitBuilder(RequireRunner(), LocalPath, message);
	}

	/// <summary>Creates a branch.</summary>
	/// <param name="name">The branch to create.</param>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public IGitBranchCreateBuilder CreateBranch(GitBranchName name)
	{
		Ensure.NotNull(name);
		return new GitBranchCreateBuilder(RequireRunner(), LocalPath, name);
	}

	/// <summary>Deletes a branch.</summary>
	/// <param name="name">The branch to delete.</param>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public IGitBranchDeleteBuilder DeleteBranch(GitBranchName name)
	{
		Ensure.NotNull(name);
		return new GitBranchDeleteBuilder(RequireRunner(), LocalPath, name);
	}

	/// <summary>Switches the working tree to a different branch, tag, or commit.</summary>
	/// <param name="target">The branch, tag, or commit to switch to.</param>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public IGitCheckoutBuilder Checkout(GitRefName target)
	{
		Ensure.NotNull(target);
		return new GitCheckoutBuilder(RequireRunner(), LocalPath, target);
	}

	/// <summary>Adds a remote.</summary>
	/// <param name="name">The remote to add.</param>
	/// <param name="url">The URL the remote points at.</param>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="name"/> or <paramref name="url"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public IGitRemoteAddBuilder AddRemote(GitRemoteName name, GitRepositoryRemotePath url)
	{
		Ensure.NotNull(name);
		Ensure.NotNull(url);
		return new GitRemoteAddBuilder(RequireRunner(), LocalPath, name, url);
	}

	/// <summary>Removes a remote and every remote-tracking branch belonging to it.</summary>
	/// <param name="name">The remote to remove.</param>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public IGitRemoteRemoveBuilder RemoveRemote(GitRemoteName name)
	{
		Ensure.NotNull(name);
		return new GitRemoteRemoveBuilder(RequireRunner(), LocalPath, name);
	}

	/// <summary>Changes the URL a remote points at.</summary>
	/// <param name="name">The remote to change.</param>
	/// <param name="url">The URL to set.</param>
	/// <returns>A fresh builder.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="name"/> or <paramref name="url"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">This repository has no <see cref="ProcessRunner"/>.</exception>
	public IGitRemoteSetUrlBuilder SetRemoteUrl(GitRemoteName name, GitRepositoryRemotePath url)
	{
		Ensure.NotNull(name);
		Ensure.NotNull(url);
		return new GitRemoteSetUrlBuilder(RequireRunner(), LocalPath, name, url);
	}

	private IGitProcessRunner RequireRunner() =>
		ProcessRunner ?? throw new InvalidOperationException(
			"This GitRepository carries hosting metadata only and has no process runner. Obtain one " +
			$"from {nameof(IGitClient)}.{nameof(IGitClient.OpenAsync)} or " +
			$"{nameof(IGitClient)}.{nameof(IGitClient.DiscoverAsync)} before running git commands against it.");

	/// <summary>
	/// Opens <see cref="WebURI"/> in the default browser.
	/// </summary>
	/// <remarks>
	/// Only an absolute <c>http</c> or <c>https</c> URI is launched. Anything else — a
	/// <see langword="null"/> <see cref="WebURI"/>, a <c>file:</c> URI, a bare filesystem path, a
	/// relative string, or any other registered handler scheme — is silently ignored. The check is
	/// deliberate rather than defensive: the launch runs through <c>UseShellExecute</c>, so without
	/// it any non-blank string would be handed to the shell as something to execute, and
	/// <see cref="GitRepositoryWebURI"/> only guarantees that the value is not blank. The value is
	/// expected to be populated from a hosting provider's API response, which is remote data.
	/// </remarks>
	public void OpenWebClient()
	{
		if (!IsBrowsableUri(WebURI?.WeakString, out Uri? uri))
		{
			return;
		}

		// UseShellExecute with the URI as FileName is the portable form. The previous
		// implementation hardcoded "explorer", which does not exist on Linux or macOS.
		_ = Process.Start(new ProcessStartInfo
		{
			FileName = uri.AbsoluteUri,
			UseShellExecute = true,
		});
	}

	/// <summary>
	/// Decides whether a value is safe to hand to the shell as something to open.
	/// </summary>
	/// <remarks>
	/// Separate from <see cref="OpenWebClient"/> so the decision can be asserted in tests without
	/// the side effect of actually launching a browser.
	/// </remarks>
	/// <param name="value">The candidate value, which may be <see langword="null"/>.</param>
	/// <param name="uri">The parsed absolute http or https URI, when the value is accepted.</param>
	/// <returns>
	/// <see langword="true"/> when <paramref name="value"/> is an absolute <c>http</c> or
	/// <c>https</c> URI; otherwise, <see langword="false"/>.
	/// </returns>
	internal static bool IsBrowsableUri(string? value, [NotNullWhen(true)] out Uri? uri)
	{
		if (value is null ||
			!Uri.TryCreate(value, UriKind.Absolute, out uri) ||
			(uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
		{
			uri = null;
			return false;
		}

		return true;
	}
}
