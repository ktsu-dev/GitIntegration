// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;

/// <summary>
/// Downloads objects and refs from a remote without touching the working tree.
/// </summary>
/// <remarks>
/// Like push, its two entry points differ in more than exception-versus-result, because a fetch
/// that refuses one reference (a tag the remote moved, say) exits non-zero while still updating
/// the others and listing every one. <see cref="IGitCommandBuilder{TResult}.ExecuteAsync"/> stays
/// strict and throws <see cref="GitFetchRejectedException"/>, which carries that account.
/// <see cref="IGitCommandBuilder{TResult}.TryExecuteAsync"/> returns the account as a value and
/// leaves the caller to check <see cref="GitFetchResult.HasRejections"/>. Either way the account
/// exists only where <c>--porcelain</c> does, from git 2.41.
/// </remarks>
public interface IGitFetchBuilder : IGitCommandBuilder<GitFetchResult>
{
	/// <summary>Fetches from this remote instead of the branch's configured upstream.</summary>
	/// <remarks>
	/// Cannot be combined with <see cref="AllRemotes"/>: git rejects a repository argument alongside
	/// <c>--all</c> outright. A caller may set both in either order, so only the finished
	/// configuration can detect the contradiction; <c>BuildArguments</c> throws
	/// <see cref="InvalidOperationException"/> when both are set, before a process is even spawned.
	/// </remarks>
	/// <param name="name">The remote to fetch from.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	public IGitFetchBuilder FromRemote(GitRemoteName name);

	/// <summary>Fetches from every configured remote.</summary>
	/// <remarks>
	/// Cannot be combined with <see cref="FromRemote"/>, for the same reason:
	/// <c>BuildArguments</c> throws <see cref="InvalidOperationException"/> when both are set, since
	/// only the finished configuration can detect the contradiction.
	/// </remarks>
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

	/// <summary>
	/// Also fetches the objects the repository's submodules need.
	/// </summary>
	/// <remarks>
	/// Emits <c>--recurse-submodules=&lt;value&gt;</c>. Not to be confused with
	/// <c>IGitPushBuilder.CheckingSubmodules</c>: git spells that flag with the same name but it
	/// governs an unrelated question, which is why this library gives the two different method names
	/// and different enums.
	/// </remarks>
	/// <param name="recursion">Whether, and when, to recurse.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="InvalidEnumArgumentException"><paramref name="recursion"/> is not a recognized value.</exception>
	public IGitFetchBuilder RecursingSubmodules(GitSubmoduleRecursion recursion);

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
	private GitSubmoduleRecursion? _submoduleRecursion;

	/// <summary>
	/// Gets or sets a value indicating whether the installed git is new enough for
	/// <c>fetch --porcelain</c>.
	/// </summary>
	/// <remarks>
	/// Defaults true so <c>BuildArguments</c> — which is pure and cannot probe — emits the modern
	/// form. The execution paths set it from an actual version probe before the vector is built.
	/// </remarks>
	private bool PorcelainSupportedByVersion { get; set; } = true;

	/// <summary>
	/// Gets a value indicating whether this invocation can ask for, and therefore parse, the
	/// porcelain account of what was fetched.
	/// </summary>
	/// <remarks>
	/// Two independent reasons it may not be, and both end in the same honest report:
	/// <see cref="GitFetchResult.DetailAvailable"/> false with an empty
	/// <see cref="GitFetchResult.Updates"/>, so an empty list is never mistaken for "nothing
	/// changed".
	/// <para>
	/// The first is the installed git's version: <c>fetch --porcelain</c> exists only from 2.41, and
	/// <see cref="ProbeVersionAsync"/> establishes that before the vector is built.
	/// </para>
	/// <para>
	/// The second is <see cref="RecursingSubmodules"/> with a value that actually recurses. Git
	/// refuses <c>--porcelain</c> together with <c>--recurse-submodules=yes</c> or
	/// <c>=on-demand</c> — "fatal: options '--porcelain' and '--recurse-submodules' cannot be used
	/// together", verified against git 2.43 — so emitting both would turn every recursing fetch into a
	/// failure. Dropping <c>--porcelain</c> is the only way to honour the caller's request, and it is
	/// the caller's request that wins: they asked to fetch submodules, not to be itemised. This is a
	/// second reason detail can be unavailable, alongside the version threshold, and it is not
	/// something the caller did wrong — so it degrades rather than throwing, unlike the
	/// AllRemotes/FromRemote pair above, which is a genuine contradiction between two things the
	/// caller asked for.
	/// </para>
	/// <para>
	/// <see cref="GitSubmoduleRecursion.No"/> is not recursing, and git accepts it alongside
	/// <c>--porcelain</c> (its <c>builtin/fetch.c</c> rejects the pair only for a mode other than
	/// off or default). A caller passing it explicitly, to override a host's
	/// <c>submodule.recurse=true</c>, keeps the itemised account.
	/// </para>
	/// </remarks>
	private bool IsPorcelainAvailable =>
		PorcelainSupportedByVersion && _submoduleRecursion is null or GitSubmoduleRecursion.No;

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
	public IGitFetchBuilder RecursingSubmodules(GitSubmoduleRecursion recursion)
	{
		// Validated at the fluent call rather than deferred to ToOptionValue inside
		// AppendVerbArguments, matching IGitStatusBuilder.WithUntrackedFiles: BuildArguments is
		// documented as a pure computation with no exceptions of its own.
		if (!Enum.IsDefined(recursion))
		{
			throw new InvalidEnumArgumentException(nameof(recursion), (int)recursion, typeof(GitSubmoduleRecursion));
		}

		_submoduleRecursion = recursion;
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

		// Real git dies at the remote with "fatal: fetch --all does not take a repository argument"
		// (exit 128) rather than rejecting this at parse time, so the contradiction would otherwise
		// go undetected until a process was already spawned. Pull rejects the equivalent contradiction
		// before spawning a process, and fetch should be no less consistent.
		if (_allRemotes && _remote is not null)
		{
			throw new InvalidOperationException(
				"AllRemotes and FromRemote cannot both be requested: fetch --all does not take a " +
				"repository argument.");
		}

		arguments.Add("fetch");

		if (IsPorcelainAvailable)
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

		if (_submoduleRecursion is GitSubmoduleRecursion recursion)
		{
			arguments.Add("--recurse-submodules=" + ToOptionValue(recursion));
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
		return IsPorcelainAvailable
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
	/// <remarks>
	/// A refused reference comes back as <see cref="GitFetchRejectedException"/> carrying the
	/// parsed account, so the references that did update are not lost with the exit code.
	/// </remarks>
	protected override GitCommandException CreateException(GitProcessResult result)
	{
		Ensure.NotNull(result);

		GitFetchResult? parsed = ParseRejections(result);

		return parsed is null
			? base.CreateException(result)
			: new GitFetchRejectedException(
				$"git refused at least one reference: {result.StandardOutput.Trim()}",
				result.ExitCode,
				result.Arguments,
				result.StandardError,
				parsed);
	}

	/// <inheritdoc />
	/// <remarks>
	/// Standard output when standard error is empty. Under <c>--porcelain</c> git reports a refused
	/// reference only as a <c>!</c> record on standard output and writes nothing to standard error,
	/// which would otherwise leave a failure with an empty diagnostic.
	/// </remarks>
	protected override string GetDiagnostic(GitProcessResult result)
	{
		Ensure.NotNull(result);

		return string.IsNullOrWhiteSpace(result.StandardError)
			? result.StandardOutput
			: result.StandardError;
	}

	/// <inheritdoc />
	public override async Task<GitResult<GitFetchResult>> TryExecuteAsync(CancellationToken cancellationToken = default)
	{
		await ProbeVersionAsync(cancellationToken).ConfigureAwait(false);

		GitProcessResult result = await Runner.RunAsync(
			new GitProcessRequest { Arguments = BuildArguments(), Progress = Progress },
			cancellationToken).ConfigureAwait(false);

		if (result.Success)
		{
			return GitResult<GitFetchResult>.FromValue(ParseResult(result));
		}

		// Deliberately a success carrying rejections rather than an error, as push does: git ran,
		// updated what it could, and reported precisely what happened to every reference. Callers
		// distinguish the two with GitFetchResult.HasRejections.
		GitFetchResult? parsed = ParseRejections(result);

		return parsed is not null
			? GitResult<GitFetchResult>.FromValue(parsed)
			: GitResult<GitFetchResult>.FromError(new GitCommandError
			{
				ExitCode = result.ExitCode,
				Arguments = result.Arguments,
				StandardError = GetDiagnostic(result),
			});
	}

	/// <summary>
	/// Reads the porcelain account of a failed fetch, when that failure was git refusing references.
	/// </summary>
	/// <remarks>
	/// Only a result that records a rejection counts. A fetch can also fail after listing some
	/// updates for an unrelated reason, such as one of several remotes being unreachable under
	/// <c>--all</c>, and returning those updates as a value would hide that failure.
	/// </remarks>
	/// <param name="result">The failed invocation outcome.</param>
	/// <returns>The parsed account, or <see langword="null"/> when it records no rejection.</returns>
	private GitFetchResult? ParseRejections(GitProcessResult result)
	{
		if (!IsPorcelainAvailable)
		{
			return null;
		}

		GitFetchResult parsed;

		try
		{
			parsed = GitFetchParser.Parse(result.StandardOutput);
		}
		catch (GitParseException)
		{
			// The failure is the thing to report, not the parser's view of whatever came with it.
			return null;
		}

		return parsed.HasRejections ? parsed : null;
	}

	/// <summary>
	/// Asks the installed git what version it is, so the vector can be built to suit.
	/// </summary>
	/// <remarks>
	/// A separate invocation rather than something <c>BuildArguments</c> could do, because that
	/// method is documented as a pure computation with no I/O — it is what makes the emitted
	/// command inspectable in a test without running anything.
	///
	/// Goes through <see cref="IGitCommandBuilder{TResult}.TryExecuteAsync"/> rather than
	/// <see cref="IGitCommandBuilder{TResult}.ExecuteAsync"/>, and the same way regardless of which
	/// of this builder's own two entry points is running: a failed probe means the version genuinely
	/// could not be established, and degrading to unsupported is the truthful answer, not a guess —
	/// <see cref="GitFetchResult.DetailAvailable"/> already exists to say exactly that. Mirroring
	/// each caller's own strictness would make <see cref="ExecuteAsync"/> throw a version exception
	/// for what is really a fetch problem; letting the probe degrade instead means a genuinely broken
	/// git still fails loudly, moments later, at the fetch itself and in each entry point's own idiom.
	/// </remarks>
	private async Task ProbeVersionAsync(CancellationToken cancellationToken)
	{
		GitResult<GitVersion> probe = await new GitVersionBuilder(Runner)
			.TryExecuteAsync(cancellationToken).ConfigureAwait(false);

		PorcelainSupportedByVersion = probe.Success && probe.Value!.AtLeast(PorcelainMajor, PorcelainMinor);
	}

	/// <summary>Maps the recursion mode onto the value git's flag takes.</summary>
	/// <param name="recursion">The mode to map.</param>
	/// <returns>The flag value.</returns>
	private static string ToOptionValue(GitSubmoduleRecursion recursion) => recursion switch
	{
		GitSubmoduleRecursion.No => "no",
		GitSubmoduleRecursion.Yes => "yes",
		GitSubmoduleRecursion.OnDemand => "on-demand",

		// Unreachable once RecursingSubmodules validates: this arm only exists to satisfy the
		// compiler's exhaustiveness check over the switch.
		_ => throw new InvalidEnumArgumentException(nameof(recursion), (int)recursion, typeof(GitSubmoduleRecursion)),
	};
}
