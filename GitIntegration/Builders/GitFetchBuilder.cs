// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;

/// <summary>
/// Downloads objects and refs from a remote without touching the working tree.
/// </summary>
public interface IGitFetchBuilder : IGitCommandBuilder<GitFetchResult>
{
	/// <summary>Fetches from this remote instead of the branch's configured upstream.</summary>
	/// <param name="name">The remote to fetch from.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	public IGitFetchBuilder FromRemote(GitRemoteName name);

	/// <summary>Fetches from every configured remote.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitFetchBuilder AllRemotes();

	/// <summary>Deletes remote-tracking branches whose counterparts no longer exist on the remote.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitFetchBuilder Prune();

	/// <summary>Fetches tags as well as branches.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitFetchBuilder WithTags();

	/// <summary>Limits history to this many commits per branch.</summary>
	/// <param name="depth">How many commits of history to fetch. Must be positive.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="depth"/> is not positive.</exception>
	public IGitFetchBuilder WithDepth(int depth);

	/// <summary>Reports git's progress output as it arrives.</summary>
	/// <param name="progress">The sink to report to. Must be thread-safe.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="progress"/> is <see langword="null"/>.</exception>
	public IGitFetchBuilder ReportingProgress(IProgress<string> progress);
}

/// <summary>
/// Builds <c>git fetch</c>, with <c>--porcelain</c> where the installed git supports it.
/// </summary>
/// <remarks>
/// <c>fetch --porcelain</c> arrived in git 2.41. Below that this builder still fetches, but returns
/// a result whose <see cref="GitFetchResult.DetailAvailable"/> is false: the work happened and only
/// the itemised account is missing. It deliberately does not fall back to parsing git's human
/// output, which the design forbids for every other verb and which git has changed before.
/// </remarks>
/// <param name="runner">Runs the assembled commands.</param>
/// <param name="repositoryPath">The repository to scope the commands to.</param>
internal sealed class GitFetchBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)
	: GitCommandBuilder<GitFetchResult>(runner, repositoryPath), IGitFetchBuilder
{
	/// <summary>The first git release whose <c>fetch</c> understands <c>--porcelain</c>.</summary>
	private const int PorcelainMajor = 2;
	private const int PorcelainMinor = 41;

	private GitRemoteName? _remote;
	private int? _depth;
	private bool _allRemotes;
	private bool _prune;
	private bool _tags;

	// Defaults true so BuildArguments — which is pure and cannot probe — emits the modern form.
	// The execution paths set it from an actual version probe before the vector is built.
	private bool _porcelainSupported = true;

	/// <inheritdoc />
	public IGitFetchBuilder FromRemote(GitRemoteName name)
	{
		_remote = Ensure.NotNull(name);
		return this;
	}

	/// <inheritdoc />
	public IGitFetchBuilder AllRemotes()
	{
		_allRemotes = true;
		return this;
	}

	/// <inheritdoc />
	public IGitFetchBuilder Prune()
	{
		_prune = true;
		return this;
	}

	/// <inheritdoc />
	public IGitFetchBuilder WithTags()
	{
		_tags = true;
		return this;
	}

	/// <inheritdoc />
	public IGitFetchBuilder WithDepth(int depth)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);
		_depth = depth;
		return this;
	}

	/// <inheritdoc />
	public IGitFetchBuilder ReportingProgress(IProgress<string> progress)
	{
		Progress = Ensure.NotNull(progress);
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("fetch");

		if (_porcelainSupported)
		{
			arguments.Add("--porcelain");
		}

		if (_allRemotes)
		{
			arguments.Add("--all");
		}

		if (_prune)
		{
			arguments.Add("--prune");
		}

		if (_tags)
		{
			arguments.Add("--tags");
		}

		if (_depth is int depth)
		{
			arguments.Add("--depth=" + depth.ToString(CultureInfo.InvariantCulture));
		}

		if (_remote is not null)
		{
			AppendOperands(arguments, _remote.WeakString);
		}
	}

	/// <inheritdoc />
	protected override GitFetchResult ParseResult(GitProcessResult result)
	{
		Ensure.NotNull(result);

		// Without --porcelain there is no machine-readable account to parse, and this library does
		// not read the human alternative. The fetch still happened; only the itemisation is absent,
		// which DetailAvailable records so an empty list is not mistaken for "nothing changed".
		return _porcelainSupported
			? GitFetchParser.Parse(result.StandardOutput)
			: new GitFetchResult { Updates = [], DetailAvailable = false };
	}

	/// <inheritdoc />
	public override async Task<GitFetchResult> ExecuteAsync(CancellationToken cancellationToken = default)
	{
		await ProbeVersionAsync(cancellationToken).ConfigureAwait(false);

		return await base.ExecuteAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public override async Task<GitResult<GitFetchResult>> TryExecuteAsync(CancellationToken cancellationToken = default)
	{
		await ProbeVersionAsync(cancellationToken).ConfigureAwait(false);

		return await base.TryExecuteAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Asks the installed git what version it is, so the vector can be built to suit.
	/// </summary>
	/// <remarks>
	/// A separate invocation rather than something <c>BuildArguments</c> could do, because that
	/// method is documented as a pure computation with no I/O — it is what makes the emitted
	/// command inspectable in a test without running anything.
	/// </remarks>
	private async Task ProbeVersionAsync(CancellationToken cancellationToken)
	{
		GitVersion version = await new GitVersionBuilder(Runner)
			.ExecuteAsync(cancellationToken).ConfigureAwait(false);

		_porcelainSupported = version.AtLeast(PorcelainMajor, PorcelainMinor);
	}
}
