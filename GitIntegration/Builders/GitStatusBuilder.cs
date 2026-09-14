// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.ComponentModel;

using ktsu.Semantics.Paths;

/// <summary>
/// Reports the working tree and index state of a repository.
/// </summary>
public interface IGitStatusBuilder : IGitCommandBuilder<GitStatus>
{
	/// <summary>
	/// Sets how much untracked detail git should report.
	/// </summary>
	/// <remarks>
	/// Without this call the command reports untracked files the way
	/// <see cref="GitUntrackedFilesMode.Normal"/> describes, whatever the host's
	/// <c>status.showUntrackedFiles</c> is set to.
	/// </remarks>
	/// <param name="mode">The reporting mode.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="InvalidEnumArgumentException"><paramref name="mode"/> is not a recognised value.</exception>
	public IGitStatusBuilder WithUntrackedFiles(GitUntrackedFilesMode mode);

	/// <summary>
	/// Includes ignored files in the reported entries.
	/// </summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitStatusBuilder IncludeIgnored();
}

/// <summary>
/// Builds <c>git status --porcelain=v2 --branch -z --untracked-files=normal</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
internal sealed class GitStatusBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)
	: GitCommandBuilder<GitStatus>(runner, repositoryPath), IGitStatusBuilder
{
	/// <summary>
	/// The untracked reporting mode emitted when the caller never calls <see cref="WithUntrackedFiles"/>.
	/// </summary>
	/// <remarks>
	/// Git's own documented default for <c>status.showUntrackedFiles</c>, so pinning it changes nothing
	/// on a host that has not set the variable and everything on a host that has.
	/// </remarks>
	internal const GitUntrackedFilesMode DefaultUntrackedFiles = GitUntrackedFilesMode.Normal;

	private GitUntrackedFilesMode _untrackedFiles = DefaultUntrackedFiles;
	private bool _includeIgnored;

	/// <inheritdoc />
	public IGitStatusBuilder WithUntrackedFiles(GitUntrackedFilesMode mode)
	{
		// Validated here, at the fluent call, rather than deferred to ToOptionValue inside
		// AppendVerbArguments: BuildArguments() is documented as a pure computation with no
		// exceptions of its own, and every sibling builder (GitLogBuilder.Take/Skip, ForRevision,
		// ForPath) validates its argument at the setter for the same reason.
		if (!Enum.IsDefined(mode))
		{
			throw new InvalidEnumArgumentException(nameof(mode), (int)mode, typeof(GitUntrackedFilesMode));
		}

		_untrackedFiles = mode;
		return this;
	}

	/// <inheritdoc />
	public IGitStatusBuilder IncludeIgnored()
	{
		_includeIgnored = true;
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("status");

		// Porcelain v2 is the documented, version-stable machine format; --branch adds the header
		// records carrying the branch, upstream, and ahead/behind counts; -z NUL-terminates every
		// record so a path containing a space or a newline cannot be mistaken for a delimiter.
		arguments.Add("--porcelain=v2");
		arguments.Add("--branch");
		arguments.Add("-z");

		// Emitted whether or not the caller chose a mode. Omitting the flag hands the decision to
		// status.showUntrackedFiles, which a CI image or a developer's ~/.gitconfig can set to "no" to
		// speed up status in a large tree — and then the same working copy reports IsClean == true with
		// an untracked file sitting in it. A caller asking "is there work here I would destroy?"
		// deserves the same answer on every host, so the default is pinned in the argument vector
		// beside --no-pager, core.quotepath and color.ui rather than left to the host.
		arguments.Add("--untracked-files=" + ToOptionValue(_untrackedFiles));

		if (_includeIgnored)
		{
			// "matching" rather than "traditional": it lists the ignored paths themselves instead of
			// collapsing an ignored directory to a single entry, which is what a caller asking for
			// ignored files almost always wants.
			arguments.Add("--ignored=matching");
		}
	}

	/// <inheritdoc />
	protected override GitStatus ParseResult(GitProcessResult result) =>
		GitStatusParser.Parse(Ensure.NotNull(result).StandardOutput);

	private static string ToOptionValue(GitUntrackedFilesMode mode) => mode switch
	{
		GitUntrackedFilesMode.No => "no",
		GitUntrackedFilesMode.Normal => "normal",
		GitUntrackedFilesMode.All => "all",

		// Unreachable once WithUntrackedFiles validates: this arm only exists to satisfy the
		// compiler's exhaustiveness check over the switch.
		_ => throw new InvalidEnumArgumentException(nameof(mode), (int)mode, typeof(GitUntrackedFilesMode)),
	};
}
