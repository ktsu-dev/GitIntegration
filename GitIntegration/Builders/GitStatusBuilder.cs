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
/// Builds <c>git status --porcelain=v2 --branch -z</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
internal sealed class GitStatusBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)
	: GitCommandBuilder<GitStatus>(runner, repositoryPath), IGitStatusBuilder
{
	private GitUntrackedFilesMode? _untrackedFiles;
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

		if (_untrackedFiles is GitUntrackedFilesMode mode)
		{
			arguments.Add("--untracked-files=" + ToOptionValue(mode));
		}

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
