// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Switches the working tree to a different branch, tag, or commit.
/// </summary>
/// <remarks>
/// <c>checkout</c> rather than the newer <c>switch</c>, because the target is a
/// <see cref="GitRefName"/> — which may be a branch, a tag, or an object id — and <c>switch</c>
/// handles only branches.
/// </remarks>
public interface IGitCheckoutBuilder : IGitCommandBuilder<GitCompleted>
{
	/// <summary>Creates the target as a new branch at the current HEAD and switches to it.</summary>
	/// <remarks>
	/// Cannot be combined with <see cref="Detach"/>: git refuses <c>-b</c> alongside
	/// <c>--detach</c> outright. A caller may set both in either order, so only the finished
	/// configuration can detect the contradiction; <c>BuildArguments</c> throws
	/// <see cref="InvalidOperationException"/> when both are set, before a process is even spawned.
	/// </remarks>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitCheckoutBuilder CreatingBranch();

	/// <summary>
	/// Switches even when it would discard uncommitted changes in the working tree.
	/// </summary>
	/// <remarks>Local modifications to files that differ between the two trees are lost.</remarks>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitCheckoutBuilder Force();

	/// <summary>Checks the target out as a detached HEAD rather than switching to a branch.</summary>
	/// <remarks>
	/// Cannot be combined with <see cref="CreatingBranch"/>, for the same reason:
	/// <c>BuildArguments</c> throws <see cref="InvalidOperationException"/> when both are set, since
	/// only the finished configuration can detect the contradiction.
	/// </remarks>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitCheckoutBuilder Detach();
}

/// <summary>
/// Builds <c>git checkout</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
/// <param name="target">The branch, tag, or commit to switch to.</param>
internal sealed class GitCheckoutBuilder(
	IGitProcessRunner runner,
	AbsoluteDirectoryPath repositoryPath,
	GitRefName target)
	: GitCommandBuilder<GitCompleted>(runner, repositoryPath), IGitCheckoutBuilder
{
	private readonly GitRefName _target = Ensure.NotNull(target);
	private bool _creatingBranch;
	private bool _force;
	private bool _detach;

	/// <inheritdoc />
	public IGitCheckoutBuilder CreatingBranch()
	{
		_creatingBranch = true;
		return this;
	}

	/// <inheritdoc />
	public IGitCheckoutBuilder Force()
	{
		_force = true;
		return this;
	}

	/// <inheritdoc />
	public IGitCheckoutBuilder Detach()
	{
		_detach = true;
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		// Real git rejects "-b <name> --detach <target>" at the command line with
		// "fatal: '--detach' cannot be used with '-b/-B/--orphan'" (verified against git 2.43), so
		// the contradiction would otherwise go undetected until a process was already spawned. Fetch
		// and pull reject their equivalent contradictions before spawning a process, and checkout
		// should be no less consistent.
		if (_creatingBranch && _detach)
		{
			throw new InvalidOperationException(
				"CreatingBranch and Detach cannot both be requested: git refuses -b alongside " +
				"--detach.");
		}

		arguments.Add("checkout");

		// -b has no long form, unlike every other flag this library emits.
		if (_creatingBranch)
		{
			arguments.Add("-b");
		}

		if (_force)
		{
			arguments.Add("--force");
		}

		if (_detach)
		{
			arguments.Add("--detach");
		}

		// Checkout is the one verb that cannot use AppendOperands, and the reason is a git bug rather
		// than a design choice here.
		//
		// git <= 2.43 only strips "--end-of-options" from the argument vector when
		// PARSE_OPT_KEEP_DASHDASH is unset (parse-options.c). checkout sets exactly that flag,
		// because it is one of the few verbs accepting both a revision and a pathspec, so the marker
		// survives into checkout's own operand list and is read as a path — "git checkout
		// --end-of-options main" dies with "error: pathspec '--end-of-options' did not match any
		// file(s) known to git". git 2.44 changed the condition to PARSE_OPT_KEEP_UNKNOWN_OPT, which
		// checkout does not set, so the marker works from that version onward. Emitting it here would
		// therefore make Checkout unusable on, among others, the stock git of Ubuntu 24.04 LTS.
		//
		// The trailing "--" is git's own documented disambiguator for this verb and works on every
		// version. It buys a guarantee the marker did not: the operand is read as a revision and
		// never as a path, so checking out a branch whose name also matches a file on disk resolves
		// to the branch rather than silently restoring the file.
		//
		// Option injection is still prevented, by the layer that was always the primary one:
		// GitRefName carries NotAnOptionAttribute, which refuses to construct a value beginning with
		// a dash at all, so no dash-leading target can reach this vector in the first place.
		arguments.Add(_target.WeakString);
		arguments.Add("--");
	}

	/// <inheritdoc />
	protected override GitCompleted ParseResult(GitProcessResult result) =>
		new() { Arguments = Ensure.NotNull(result).Arguments };
}
