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
	/// <para>
	/// <see cref="GitUntrackedFilesMode.No"/> cannot be combined with <see cref="IncludeIgnored"/>:
	/// git refuses the pair with "Unsupported combination of ignored and untracked-files arguments".
	/// A caller may set both in either order, so only the finished configuration can detect the
	/// contradiction; <c>BuildArguments</c> throws <see cref="InvalidOperationException"/> when both
	/// are set, before a process is even spawned.
	/// </para>
	/// </remarks>
	/// <param name="mode">The reporting mode.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="InvalidEnumArgumentException"><paramref name="mode"/> is not a recognized value.</exception>
	public IGitStatusBuilder WithUntrackedFiles(GitUntrackedFilesMode mode);

	/// <summary>
	/// Includes ignored files in the reported entries.
	/// </summary>
	/// <remarks>
	/// Emits <c>--ignored=matching</c>, which reports a wholly ignored directory once, as a single
	/// entry for the directory itself, rather than one entry per file inside it. That holds under
	/// <see cref="GitUntrackedFilesMode.All"/> too: <c>.gitignore</c> naming <c>build/</c> yields a
	/// single <c>build</c> entry however many files <c>build/</c> holds. A file ignored by a pattern that
	/// names files rather than its directory is still reported on its own.
	/// <para>
	/// Cannot be combined with <see cref="WithUntrackedFiles"/> set to
	/// <see cref="GitUntrackedFilesMode.No"/>: git refuses the pair, so <c>BuildArguments</c> throws
	/// <see cref="InvalidOperationException"/> when both are set.
	/// </para>
	/// </remarks>
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

		// git refuses this pair with "fatal: Unsupported combination of ignored and untracked-files
		// arguments", which reaches the caller as a GitCommandException that names neither builder
		// call. --ignored=traditional is no way out: under -uno git exits 0 and lists no ignored
		// entries, a silent wrong answer. So refuse it here, naming both calls, as the Add, Pull,
		// Checkout and Diff builders refuse their contradictory pairs (ktsu-dev/GitIntegration#295).
		if (_includeIgnored && _untrackedFiles == GitUntrackedFilesMode.No)
		{
			throw new InvalidOperationException(
				"IncludeIgnored and WithUntrackedFiles(GitUntrackedFilesMode.No) cannot both be requested: " +
				"git refuses to list ignored files while untracked files are switched off.");
		}

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

		// Same reasoning, for submodules. diff.ignoreSubmodules=all in a host's config (status reads
		// it too) hides a submodule's new commits and untracked files, so the superproject reports
		// IsClean == true over unpushed work. Pinning "none" reports the submodule however the host
		// is configured (ktsu-dev/GitIntegration#190).
		arguments.Add("--ignore-submodules=none");

		if (_includeIgnored)
		{
			// "matching" reports the paths an ignore pattern actually names, so an ignored directory is
			// one entry for the directory even under --untracked-files=all ("traditional" would list
			// every file inside it there). That keeps the result proportional to the ignore rules
			// rather than to the size of a build or package folder, and IncludeIgnored's docs say so.
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
