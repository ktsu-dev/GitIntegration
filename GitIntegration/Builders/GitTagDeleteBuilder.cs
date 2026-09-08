// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Deletes a tag.
/// </summary>
/// <remarks>
/// Deletes the local reference only. A tag already pushed to a remote stays there; removing it
/// needs a delete refspec through <c>Push()</c>, which this verb deliberately does not do on a
/// caller's behalf.
/// <para>
/// There is no <c>Force()</c> here, unlike <see cref="IGitBranchDeleteBuilder"/>. Git has no
/// unmerged-tag concept to refuse over — a tag is not a line of development — so <c>git tag
/// --delete</c> takes no force flag at all.
/// </para>
/// </remarks>
public interface IGitTagDeleteBuilder : IGitCommandBuilder<GitCompleted>
{
}

/// <summary>
/// Builds <c>git tag --delete &lt;name&gt;</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
/// <param name="name">The tag to delete.</param>
internal sealed class GitTagDeleteBuilder(
	IGitProcessRunner runner,
	AbsoluteDirectoryPath repositoryPath,
	GitTagName name)
	: GitCommandBuilder<GitCompleted>(runner, repositoryPath), IGitTagDeleteBuilder
{
	private readonly GitTagName _name = Ensure.NotNull(name);

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("tag");

		// Long form throughout, matching GitBranchDeleteBuilder: --delete is -d, and spelling it out
		// keeps the vector readable when it is copied out of a GitCommandException and rerun by hand.
		arguments.Add("--delete");

		AppendOperands(arguments, _name.WeakString);
	}

	/// <inheritdoc />
	protected override GitCompleted ParseResult(GitProcessResult result) =>
		new() { Arguments = Ensure.NotNull(result).Arguments };
}
