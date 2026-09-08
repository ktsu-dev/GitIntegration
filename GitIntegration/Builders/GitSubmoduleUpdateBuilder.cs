// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Globalization;

using ktsu.Semantics.Paths;

/// <summary>
/// Checks out the commits the superproject's gitlinks record, in each submodule's working directory.
/// </summary>
/// <remarks>
/// <para>
/// Returns only success or failure. Everything <c>git submodule update</c> prints is human prose
/// with no porcelain alternative, and this design forbids parsing that for every other verb — so
/// this one gets no exemption either, exactly as <c>pull</c> does not. A caller who needs to know
/// what moved asks <c>Submodules()</c> before and after, which is precise.
/// </para>
/// <para>
/// <b>A failure here does not mean the repository is unchanged.</b> This verb walks the submodules
/// in turn, so one failing after others have already been checked out leaves the repository in a
/// state that is neither the old one nor the intended new one. That is worth stating plainly,
/// because the natural reading of an exception is "nothing happened", and here that reading is
/// false. <see cref="GitCommandException"/> carries the exit code, the argument vector, and the
/// diagnostic, which is enough for a caller to act; what it cannot carry is how far the operation
/// got, and <c>Submodules()</c> is how to find that out.
/// </para>
/// <para>
/// Overlaps with <c>Pull().RecursingSubmodules(...)</c> without being equivalent to it. The flag
/// updates submodules as part of a pull; this verb also <em>initialises</em> newly added ones
/// through <see cref="Initialise"/>, which the flag does only for submodules already registered.
/// Both are worth having.
/// </para>
/// </remarks>
public interface IGitSubmoduleUpdateBuilder : IGitCommandBuilder<GitCompleted>
{
	/// <summary>
	/// Initialises any submodule that is registered but has never been checked out.
	/// </summary>
	/// <remarks>
	/// Emits <c>--init</c>. Without it, a submodule added upstream since this working copy was
	/// cloned is skipped silently rather than checked out, because <c>update</c> alone only touches
	/// submodules that are already initialised.
	/// </remarks>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitSubmoduleUpdateBuilder Initialise();

	/// <summary>Updates submodules nested inside submodules, to any depth.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitSubmoduleUpdateBuilder Recursive();

	/// <summary>
	/// Checks out the branch <c>.gitmodules</c> configures for each submodule, instead of the commit
	/// the superproject records.
	/// </summary>
	/// <remarks>
	/// Emits <c>--remote</c>, and it changes what this verb is for. The default is to make the
	/// working directories agree with the gitlinks the superproject already committed; this instead
	/// moves each submodule to the tip of its configured branch, which will normally leave the
	/// superproject's gitlinks out of date until someone commits the new ones.
	/// </remarks>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitSubmoduleUpdateBuilder FromRemote();

	/// <summary>
	/// Checks out the recorded commit even when it would discard changes in a submodule's working
	/// directory.
	/// </summary>
	/// <remarks>Local modifications inside the submodule are lost.</remarks>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitSubmoduleUpdateBuilder Force();

	/// <summary>Fetches only this many commits of history for a submodule being cloned.</summary>
	/// <param name="depth">The number of commits to fetch. Must be positive.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="depth"/> is not positive.</exception>
	public IGitSubmoduleUpdateBuilder WithDepth(int depth);

	/// <summary>Reports git's progress output as it arrives.</summary>
	/// <remarks>
	/// Offered because this is a network operation whenever a submodule has to be cloned or fetched,
	/// the same reason <c>Fetch</c>, <c>Clone</c>, and <c>Push</c> offer it.
	/// </remarks>
	/// <param name="progress">The sink to report to. Must be thread-safe.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="progress"/> is <see langword="null"/>.</exception>
	public IGitSubmoduleUpdateBuilder ReportingProgress(IProgress<string> progress);
}

/// <summary>
/// Builds <c>git submodule update</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
internal sealed class GitSubmoduleUpdateBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)
	: GitCommandBuilder<GitCompleted>(runner, repositoryPath), IGitSubmoduleUpdateBuilder
{
	private bool _initialise;
	private bool _recursive;
	private bool _fromRemote;
	private bool _force;
	private int? _depth;

	/// <inheritdoc />
	public IGitSubmoduleUpdateBuilder Initialise()
	{
		_initialise = true;
		return this;
	}

	/// <inheritdoc />
	public IGitSubmoduleUpdateBuilder Recursive()
	{
		_recursive = true;
		return this;
	}

	/// <inheritdoc />
	public IGitSubmoduleUpdateBuilder FromRemote()
	{
		_fromRemote = true;
		return this;
	}

	/// <inheritdoc />
	public IGitSubmoduleUpdateBuilder Force()
	{
		_force = true;
		return this;
	}

	/// <inheritdoc />
	public IGitSubmoduleUpdateBuilder WithDepth(int depth)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);
		_depth = depth;
		return this;
	}

	/// <inheritdoc />
	public IGitSubmoduleUpdateBuilder ReportingProgress(IProgress<string> progress)
	{
		Progress = Ensure.NotNull(progress);
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("submodule");
		arguments.Add("update");

		if (_initialise)
		{
			arguments.Add("--init");
		}

		if (_recursive)
		{
			arguments.Add("--recursive");
		}

		if (_fromRemote)
		{
			arguments.Add("--remote");
		}

		if (_force)
		{
			arguments.Add("--force");
		}

		if (_depth is int depth)
		{
			arguments.Add("--depth");
			arguments.Add(depth.ToString(CultureInfo.InvariantCulture));
		}

		// No operands: this verb updates every submodule. A pathspec restricting it to some of them
		// would be a caller-supplied value and would need the end-of-options guard; nothing asks for
		// that yet, and adding it later changes no existing vector.
	}

	/// <inheritdoc />
	protected override GitCompleted ParseResult(GitProcessResult result) =>
		new() { Arguments = Ensure.NotNull(result).Arguments };
}
