// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Removes a whole file's staged changes, leaving the working tree alone.
/// </summary>
/// <remarks>
/// Unstaging one hunk of a file's patch is <c>Apply(text).ToIndex().Reversed()</c>. This builder is
/// the file-level verb, and the only option for a binary file, which has no hunks to apply in
/// reverse.
/// </remarks>
public interface IGitRestoreBuilder : IGitCommandBuilder<GitCompleted>
{
}

/// <summary>
/// Builds <c>git reset -q -- &lt;path&gt;</c>, which unstages the path.
/// </summary>
/// <remarks>
/// <c>git restore --staged</c> would read as the modern spelling, but with no <c>--source</c> it
/// restores the index from <c>HEAD</c>, and in a repository with no commits yet it dies with "could
/// not resolve HEAD". That is exactly when a user is most likely to have staged too much.
/// <c>git reset -- &lt;path&gt;</c> copies the path's entry from <c>HEAD</c> into the index, and
/// on an unborn branch it removes the entry, leaving the file untracked. It behaves that way on
/// every supported git, so there is no version to probe for.
/// </remarks>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
/// <param name="path">The path, relative to the repository root, to unstage.</param>
internal sealed class GitRestoreBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath, RelativeFilePath path)
	: GitCommandBuilder<GitCompleted>(runner, repositoryPath), IGitRestoreBuilder
{
	private readonly RelativeFilePath _path = Ensure.NotNull(path);

	/// <inheritdoc />
	protected override bool PassesPathsLiterally => true;

	/// <summary>
	/// Appends the verb and the path, separating the two with a bare <c>--</c> rather than through
	/// <c>AppendOperands</c>.
	/// </summary>
	/// <remarks>
	/// A bare <c>--</c> has separated options from pathspecs for git's whole history, so it gives
	/// the same protection against a dash-leading path as the <c>--end-of-options</c> that
	/// <c>AppendOperands</c> writes, without needing git 2.24. <c>-q</c> suppresses the list of
	/// paths that still have unstaged changes, which <c>reset</c> otherwise prints.
	/// </remarks>
	/// <param name="arguments">The vector being assembled.</param>
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("reset");
		arguments.Add("-q");
		arguments.Add("--");
		arguments.Add(_path.WeakString);
	}

	/// <inheritdoc />
	protected override GitCompleted ParseResult(GitProcessResult result) =>
		new() { Arguments = Ensure.NotNull(result).Arguments };
}
