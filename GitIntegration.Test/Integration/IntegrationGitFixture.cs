// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Essentials.FileSystemProviders.Native;

/// <summary>
/// Setup shared by the integration tier: skipping when git is absent, and giving each throwaway
/// repository a deterministic identity that neither depends on nor disturbs the host's own git
/// configuration.
/// </summary>
internal static class IntegrationGitFixture
{
	/// <summary>
	/// The environment variable that turns a missing git binary from a skip into a failure.
	/// </summary>
	/// <remarks>
	/// Set it in any environment where git is supposed to be present. Without it these tests skip
	/// when git is absent, which is right for a contributor who has not installed git but wrong for
	/// CI: a runner missing git would otherwise report a green suite while testing nothing at all.
	/// </remarks>
	internal const string RequiredEnvironmentVariable = "KTSU_GIT_INTEGRATION_TESTS_REQUIRED";

	/// <summary>
	/// Decides whether a value read from the environment means "git is required".
	/// </summary>
	/// <remarks>
	/// A pure function of the string rather than a reader of the environment, so it can be tested
	/// without mutating process-wide state that other tests running in parallel would see. Any
	/// non-empty value other than <c>0</c> or <c>false</c> counts as set, so the variable behaves
	/// the way a reader expects however a CI system happens to spell "yes".
	/// </remarks>
	/// <param name="value">The raw environment variable value, which may be absent.</param>
	/// <returns><see langword="true"/> when a missing git binary should fail rather than skip.</returns>
	internal static bool IsGitRequired(string? value) =>
		!string.IsNullOrWhiteSpace(value) &&
		!string.Equals(value, "0", StringComparison.Ordinal) &&
		!string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);

	/// <summary>Creates a client backed by the real git executable on <c>PATH</c>.</summary>
	/// <returns>A client with no repository scope yet.</returns>
	internal static GitClient CreateClient() =>
		new(new RunCommandGitProcessRunner(new GitOptions()), new NativeFileSystemProvider());

	/// <summary>
	/// Skips the calling test when no usable git binary is present — unless git is required.
	/// </summary>
	/// <param name="cancellationToken">Cancels the version probe.</param>
	/// <exception cref="GitExecutableNotFoundException">
	/// git is not on <c>PATH</c> and <see cref="RequiredEnvironmentVariable"/> is set.
	/// </exception>
	internal static async Task RequireGitAsync(CancellationToken cancellationToken)
	{
		try
		{
			_ = await CreateClient().GetVersionAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (GitExecutableNotFoundException) when (
			!IsGitRequired(Environment.GetEnvironmentVariable(RequiredEnvironmentVariable)))
		{
			Assert.Inconclusive(
				$"git is not on PATH, so the integration tests were skipped. Set {RequiredEnvironmentVariable} " +
				"to make this a failure instead, which is what CI does.");
		}

		// When the variable is set the exception filter above declines to catch, so
		// GitExecutableNotFoundException propagates and the test fails loudly. That is deliberate:
		// a CI runner without git must not report a green suite having tested nothing.
	}

	/// <summary>
	/// Pins the repository identity a commit needs, plus the reconciliation strategy a pull needs,
	/// entirely inside the repository's own config.
	/// </summary>
	/// <remarks>
	/// Every key here is pinned locally rather than left to the host, so a developer's global
	/// identity, signing key, or reconciliation preference cannot change whether these tests pass,
	/// and the tests cannot disturb whatever the host has configured globally.
	///
	/// <c>user.name</c> / <c>user.email</c> — a commit needs an identity, and the tests must not
	/// depend on the host having one configured.
	///
	/// <c>commit.gpgsign</c> — a developer with signing enabled globally would otherwise have every
	/// commit here blocked waiting on a key or a prompt.
	///
	/// <c>pull.rebase</c> — git refuses to pull divergent branches at all unless a reconciliation
	/// strategy is configured, and Git for Windows ships <c>pull.rebase=false</c> in its system
	/// config while stock Linux and macOS git ship no default at all. Left unpinned, a conflicting
	/// pull merges quietly on Windows and dies with "Need to specify how to reconcile divergent
	/// branches" everywhere else — the gap that broke CI once already.
	/// </remarks>
	/// <param name="repository">The repository to configure.</param>
	/// <param name="authorName">The identity to write as <c>user.name</c>.</param>
	/// <param name="authorEmail">The identity to write as <c>user.email</c>.</param>
	/// <param name="cancellationToken">Cancels the configuration writes.</param>
	internal static async Task ConfigureIdentityAsync(
		GitRepository repository,
		GitAuthorName authorName,
		GitAuthorEmail authorEmail,
		CancellationToken cancellationToken)
	{
		IGitProcessRunner runner = repository.ProcessRunner!;

		foreach ((string key, string value) in new[]
		{
			("user.name", authorName.WeakString),
			("user.email", authorEmail.WeakString),
			("commit.gpgsign", "false"),
			("pull.rebase", "false"),
		})
		{
			_ = await new GitTextBuilder(runner, repository.LocalPath, "config", key, value)
				.ExecuteAsync(cancellationToken).ConfigureAwait(false);
		}
	}
}
