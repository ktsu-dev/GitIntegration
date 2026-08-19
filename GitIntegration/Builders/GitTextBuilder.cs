// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Runs a fixed argument vector and returns git's trimmed standard output.
/// </summary>
/// <remarks>
/// Used by <c>GitClient</c> for the single-value probes — <c>rev-parse --show-toplevel</c>,
/// <c>rev-parse --is-inside-work-tree</c>, and <c>remote get-url origin</c> — that need no options
/// and produce one line. Internal, and deliberately so: it accepts an arbitrary vector, which is
/// safe only because every caller of it is inside this assembly and passes literals.
/// </remarks>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">
/// The repository to scope the command to, or <see langword="null"/> for a command that is not
/// repository-scoped.
/// </param>
/// <param name="verbArguments">The verb and its options, in order.</param>
internal sealed class GitTextBuilder(
	IGitProcessRunner runner,
	AbsoluteDirectoryPath? repositoryPath,
	params string[] verbArguments)
	: GitCommandBuilder<string>(runner, repositoryPath)
{
	private readonly string[] _verbArguments = Ensure.NotNull(verbArguments);

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		foreach (string argument in _verbArguments)
		{
			arguments.Add(argument);
		}
	}

	/// <inheritdoc />
	protected override string ParseResult(GitProcessResult result) =>
		Ensure.NotNull(result).StandardOutput.Trim();
}
