// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Runs a fixed argument vector and returns git's trimmed standard output, or, for a path, the output
/// without its line terminator.
/// </summary>
/// <remarks>
/// Used by <see cref="GitClient"/> for the single-value probes — <c>rev-parse --show-toplevel</c>,
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

	/// <summary>
	/// Gets a value indicating whether only the one line terminator git appends is removed, rather
	/// than all surrounding whitespace.
	/// </summary>
	/// <remarks>
	/// For output that is a path. Linux and macOS allow a directory name to start or end with a
	/// space, and a trimmed path names a different directory, possibly a sibling repository (#277).
	/// </remarks>
	internal bool PreservesWhitespace { get; init; }

	/// <inheritdoc />
	protected override string ParseResult(GitProcessResult result)
	{
		string output = Ensure.NotNull(result).StandardOutput;

		return PreservesWhitespace ? StripLineTerminator(output) : output.Trim();
	}

	/// <summary>
	/// Removes one trailing <c>\n</c> or <c>\r\n</c>, and nothing else.
	/// </summary>
	/// <param name="output">git's standard output.</param>
	/// <returns>The output without its final line terminator.</returns>
	internal static string StripLineTerminator(string output)
	{
		Ensure.NotNull(output);

		if (output.EndsWith('\n'))
		{
			output = output[..^1];

			if (output.EndsWith('\r'))
			{
				output = output[..^1];
			}
		}

		return output;
	}
}
