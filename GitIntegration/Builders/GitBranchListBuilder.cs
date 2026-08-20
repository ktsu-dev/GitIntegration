// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Lists branch references.
/// </summary>
public interface IGitBranchListBuilder : IGitCommandBuilder<IReadOnlyList<GitBranch>>
{
	/// <summary>Lists only local branches. Replaces any previous selection.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitBranchListBuilder LocalOnly();

	/// <summary>Lists only remote-tracking branches. Replaces any previous selection.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitBranchListBuilder RemoteOnly();
}

/// <summary>
/// Builds <c>git for-each-ref</c> over the branch namespaces.
/// </summary>
/// <remarks>
/// <c>for-each-ref</c> rather than <c>branch --list</c>: it takes an explicit format string, so the
/// output is machine-readable by construction rather than by hoping a human-facing listing keeps its
/// shape.
/// </remarks>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
internal sealed class GitBranchListBuilder(IGitProcessRunner runner, AbsoluteDirectoryPath repositoryPath)
	: GitCommandBuilder<IReadOnlyList<GitBranch>>(runner, repositoryPath), IGitBranchListBuilder
{
	private const string LocalPrefix = "refs/heads";
	private const string RemotePrefix = "refs/remotes";

	private string[] _prefixes = [LocalPrefix, RemotePrefix];

	/// <inheritdoc />
	public IGitBranchListBuilder LocalOnly()
	{
		_prefixes = [LocalPrefix];
		return this;
	}

	/// <inheritdoc />
	public IGitBranchListBuilder RemoteOnly()
	{
		_prefixes = [RemotePrefix];
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("for-each-ref");
		arguments.Add("--format=" + GitOutputFormats.ForEachRefFormat);

		// These prefixes are library constants, not caller-supplied operands, so they need no
		// --end-of-options guard: no caller value can reach this position.
		foreach (string prefix in _prefixes)
		{
			arguments.Add(prefix);
		}
	}

	/// <inheritdoc />
	protected override IReadOnlyList<GitBranch> ParseResult(GitProcessResult result) =>
		GitBranchParser.Parse(Ensure.NotNull(result).StandardOutput);
}
