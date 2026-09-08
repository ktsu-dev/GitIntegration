// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.ComponentModel;

using ktsu.Semantics.Paths;

/// <summary>
/// Fetches from a remote and integrates the result into the current branch.
/// </summary>
/// <remarks>
/// Returns only success or failure. Everything <c>pull</c> prints is human prose with no porcelain
/// alternative, so rather than invent a parser for it this verb leaves the caller to ask
/// <c>Status()</c> and <c>Log()</c> what changed — both of which are precise. The one outcome worth
/// its own type is a conflict, because it leaves the repository mid-merge.
/// </remarks>
public interface IGitPullBuilder : IGitCommandBuilder<GitCompleted>
{
	/// <summary>Pulls from this remote instead of the branch's configured upstream.</summary>
	/// <param name="name">The remote to pull from.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	public IGitPullBuilder FromRemote(GitRemoteName name);

	/// <summary>Pulls this branch. Requires a remote, which git reads as the first operand.</summary>
	/// <remarks>
	/// The requirement is checked when the argument vector is built, not here: a caller may set the
	/// branch before the remote, so only the finished configuration knows whether the pair is
	/// complete. <c>BuildArguments</c> therefore throws <see cref="InvalidOperationException"/> for a
	/// branch with no remote — a configuration error, not I/O, so its purity is intact.
	/// </remarks>
	/// <param name="name">The branch to pull.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	public IGitPullBuilder WithBranch(GitBranchName name);

	/// <summary>
	/// Refuses to pull at all when the result would need a merge commit.
	/// </summary>
	/// <remarks>
	/// Cannot be combined with <see cref="Rebase"/>: the two mean opposite things about history —
	/// refuse to merge, versus rewrite to avoid needing one. A caller may set both in either order,
	/// so only the finished configuration can detect the contradiction; <c>BuildArguments</c> throws
	/// <see cref="InvalidOperationException"/> when both are set. Combining with <see cref="Merge"/>
	/// is fine: reconcile by merging, but only accept a fast-forward.
	/// </remarks>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitPullBuilder FastForwardOnly();

	/// <summary>
	/// Replays local commits on top of the fetched ones instead of merging.
	/// </summary>
	/// <remarks>
	/// Cannot be combined with <see cref="FastForwardOnly"/> or <see cref="Merge"/>, for the same
	/// reason in each case: <c>BuildArguments</c> throws <see cref="InvalidOperationException"/> when
	/// either pair is set together, since only the finished configuration can detect the
	/// contradiction.
	/// </remarks>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitPullBuilder Rebase();

	/// <summary>
	/// Reconciles by creating a merge commit when needed, regardless of any <c>pull.rebase</c> the
	/// host has configured.
	/// </summary>
	/// <remarks>
	/// Emits <c>--no-rebase</c>, git's own counterpart to <see cref="Rebase"/>. Without either flag,
	/// a host with no <c>pull.rebase</c> configured refuses to pull divergent branches at all; this
	/// is the way to express merge semantics through the library instead of reaching around it to
	/// configure git directly. Cannot be combined with <see cref="Rebase"/>: <c>BuildArguments</c>
	/// throws <see cref="InvalidOperationException"/> when both are set, since only the finished
	/// configuration can detect the contradiction. Combining with <see cref="FastForwardOnly"/> is
	/// fine — <c>git pull --no-rebase --ff-only</c> reconciles by merging but only accepts a
	/// fast-forward, so the pair is coherent rather than contradictory.
	/// </remarks>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitPullBuilder Merge();

	/// <summary>Deletes remote-tracking branches whose counterparts are gone, as part of the fetch.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitPullBuilder Prune();

	/// <summary>
	/// Also updates the repository's submodules as part of the pull.
	/// </summary>
	/// <remarks>
	/// Emits <c>--recurse-submodules=&lt;value&gt;</c>, so one invocation moves the superproject and
	/// its submodules together rather than leaving the submodules stale until something else notices.
	/// <para>
	/// Overlaps with <c>UpdateSubmodules()</c> without replacing it: this flag updates submodules
	/// that are already registered, while that verb's <c>Initialise()</c> also checks out a submodule
	/// added upstream since this working copy was cloned. Not to be confused with
	/// <c>IGitPushBuilder.CheckingSubmodules</c>, which git spells with the same flag name but which
	/// governs an unrelated question.
	/// </para>
	/// </remarks>
	/// <param name="recursion">Whether, and when, to recurse.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="InvalidEnumArgumentException"><paramref name="recursion"/> is not a recognised value.</exception>
	public IGitPullBuilder RecursingSubmodules(GitSubmoduleRecursion recursion);

	/// <summary>Reports git's progress output as it arrives.</summary>
	/// <param name="progress">The sink to report to. Must be thread-safe.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="progress"/> is <see langword="null"/>.</exception>
	public IGitPullBuilder ReportingProgress(IProgress<string> progress);
}

/// <summary>
/// Builds <c>git pull</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
internal sealed class GitPullBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)
	: GitCommandBuilder<GitCompleted>(runner, repositoryPath), IGitPullBuilder
{
	private GitRemoteName? _remote;
	private GitBranchName? _branch;
	private bool _fastForwardOnly;
	private bool _rebase;
	private bool _merge;
	private bool _prune;
	private GitSubmoduleRecursion? _submoduleRecursion;

	/// <inheritdoc />
	public IGitPullBuilder FromRemote(GitRemoteName name)
	{
		_remote = Ensure.NotNull(name);
		return this;
	}

	/// <inheritdoc />
	public IGitPullBuilder WithBranch(GitBranchName name)
	{
		_branch = Ensure.NotNull(name);
		return this;
	}

	/// <inheritdoc />
	public IGitPullBuilder FastForwardOnly()
	{
		_fastForwardOnly = true;
		return this;
	}

	/// <inheritdoc />
	public IGitPullBuilder Rebase()
	{
		_rebase = true;
		return this;
	}

	/// <inheritdoc />
	public IGitPullBuilder Merge()
	{
		_merge = true;
		return this;
	}

	/// <inheritdoc />
	public IGitPullBuilder Prune()
	{
		_prune = true;
		return this;
	}

	/// <inheritdoc />
	public IGitPullBuilder RecursingSubmodules(GitSubmoduleRecursion recursion)
	{
		// Validated at the fluent call rather than deferred into AppendVerbArguments, matching
		// IGitStatusBuilder.WithUntrackedFiles: BuildArguments is documented as a pure computation
		// with no exceptions of its own beyond the contradiction guards.
		if (!Enum.IsDefined(recursion))
		{
			throw new InvalidEnumArgumentException(nameof(recursion), (int)recursion, typeof(GitSubmoduleRecursion));
		}

		_submoduleRecursion = recursion;
		return this;
	}

	/// <inheritdoc />
	public IGitPullBuilder ReportingProgress(IProgress<string> progress)
	{
		Progress = Ensure.NotNull(progress);
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		// git accepts both and lets one quietly win. They mean opposite things about history —
		// refuse to merge, versus rewrite so no merge is needed — so a caller who asked for both
		// has a bug worth reporting rather than a preference worth guessing.
		if (_fastForwardOnly && _rebase)
		{
			throw new InvalidOperationException(
				"FastForwardOnly and Rebase cannot both be requested: one refuses to create a merge, " +
				"the other rewrites history to avoid needing one.");
		}

		// Same trap, same fix: git accepts --rebase --no-rebase too and lets one quietly win. They
		// are opposite values of the same reconciliation switch, so asking for both is a bug worth
		// reporting rather than a preference worth guessing.
		if (_rebase && _merge)
		{
			throw new InvalidOperationException(
				"Rebase and Merge cannot both be requested: one rewrites history to avoid a merge, " +
				"the other insists on making one.");
		}

		arguments.Add("pull");

		if (_fastForwardOnly)
		{
			arguments.Add("--ff-only");
		}

		// --no-rebase sits next to --rebase rather than in declaration order because the two are
		// the same switch: git takes the last one it is given, so emitting both would silently
		// pick a strategy rather than fail. The guard above makes that unreachable, and keeping
		// the pair adjacent is what makes it obvious at a glance that nothing between them can
		// separate the check from what it protects.
		if (_merge)
		{
			arguments.Add("--no-rebase");
		}

		if (_rebase)
		{
			arguments.Add("--rebase");
		}

		if (_prune)
		{
			arguments.Add("--prune");
		}

		if (_submoduleRecursion is GitSubmoduleRecursion recursion)
		{
			arguments.Add("--recurse-submodules=" + ToOptionValue(recursion));
		}

		if (_remote is null)
		{
			// git pull <refspec> with no remote reads the first operand as the remote, so a branch
			// on its own would be silently misinterpreted rather than rejected.
			if (_branch is not null)
			{
				throw new InvalidOperationException(
					"A branch was given without a remote. git reads the first operand as the remote name, " +
					"so call FromRemote as well.");
			}

			return;
		}

		if (_branch is null)
		{
			AppendOperands(arguments, _remote.WeakString);
			return;
		}

		AppendOperands(arguments, _remote.WeakString, _branch.WeakString);
	}

	/// <inheritdoc />
	protected override GitCompleted ParseResult(GitProcessResult result) =>
		new() { Arguments = Ensure.NotNull(result).Arguments };

	/// <summary>
	/// Classifies a failed pull, recognising a conflict as its own outcome.
	/// </summary>
	/// <remarks>
	/// Overridden because the base class inspects standard error while git announces a conflict on
	/// standard <em>output</em> — the same trap <c>commit</c> sets with "nothing to commit". Both a
	/// merge conflict and a rebase conflict carry the word CONFLICT, so one match covers both, and
	/// the <c>LC_ALL=C</c> that every invocation runs under is what makes it dependable.
	/// </remarks>
	/// <param name="result">The failed invocation outcome.</param>
	/// <returns>The exception to throw.</returns>
	protected override GitCommandException CreateException(GitProcessResult result)
	{
		Ensure.NotNull(result);

		return result.StandardOutput.Contains("CONFLICT", StringComparison.Ordinal)
			? new GitPullConflictException(
				"The pull left conflicts in the working tree. Use Status() to see which paths " +
				$"are unmerged: {result.StandardOutput.Trim()}",
				result.ExitCode,
				result.Arguments,
				result.StandardError)
			: base.CreateException(result);
	}

	/// <summary>
	/// Joins standard error and standard output into one diagnostic.
	/// </summary>
	/// <remarks>
	/// Pull is the second verb whose diagnostic lands on standard output, so an error built only
	/// from standard error would carry the fetch progress and say nothing about the conflict. The
	/// two are joined on a single newline, with the trailing newline trimmed from standard error
	/// first, so the result reads as two legible lines rather than a run-on string. Only the parts
	/// that are actually present are joined: a stderr-only failure (the common plain "fatal: ..."
	/// case) must not gain a trailing blank line from an empty standard output.
	/// <para>
	/// Overriding the base class's seam rather than either entry point is what makes the two report
	/// the same text. <see cref="CreateException"/> recognises only a conflict and hands everything
	/// else to the base implementation, whose message is built from this method — so a non-conflict
	/// failure explained on standard output now reaches a caller of
	/// <see cref="IGitCommandBuilder{TResult}.ExecuteAsync"/> as well as one of
	/// <see cref="IGitCommandBuilder{TResult}.TryExecuteAsync"/>, instead of arriving as
	/// "git exited with code N: " with nothing after the colon.
	/// </para>
	/// </remarks>
	/// <param name="result">The failed invocation outcome.</param>
	/// <returns>The joined diagnostic text.</returns>
	protected override string GetDiagnostic(GitProcessResult result)
	{
		Ensure.NotNull(result);

		string standardError = result.StandardError.TrimEnd('\n', '\r');

		return standardError.Length == 0
			? result.StandardOutput
			: result.StandardOutput.Length == 0
				? standardError
				: standardError + "\n" + result.StandardOutput;
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
