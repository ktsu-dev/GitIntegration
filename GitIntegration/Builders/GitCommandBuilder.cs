// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;

/// <summary>
/// The shared behavior of every git command builder: global argument injection, execution, and
/// failure translation.
/// </summary>
/// <typeparam name="TResult">
/// The parsed result type. Constrained to <c>notnull</c> so that a derived builder whose
/// <see cref="ParseResult"/> returns null on some edge case cannot yield a successful-looking
/// <see cref="GitResult{T}"/> with a null value; <see cref="GitResult{T}.FromValue(T)"/> throws instead.
/// </typeparam>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">
/// The repository to scope the command to, or <see langword="null"/> for commands that are not
/// repository-scoped, such as <c>init</c>, <c>clone</c>, and <c>--version</c>.
/// </param>
public abstract class GitCommandBuilder<TResult>(IGitProcessRunner runner, AbsoluteDirectoryPath? repositoryPath)
	: IGitCommandBuilder<TResult>
	where TResult : notnull
{
	/// <summary>
	/// Gets the runner this builder executes through.
	/// </summary>
	/// <remarks>
	/// Exposed to derived builders because not every git operation is one invocation of one
	/// argument vector. <c>Commit</c> runs <c>commit</c> and then <c>log -1</c> to read back what
	/// was written, and <c>Fetch</c> assembles a vector that depends on a runtime probe of the
	/// installed git version. Neither is expressible through <see cref="BuildArguments"/> alone.
	/// </remarks>
	protected IGitProcessRunner Runner { get; } = Ensure.NotNull(runner);

	/// <summary>
	/// Gets the repository this command is scoped to, if any.
	/// </summary>
	protected AbsoluteDirectoryPath? RepositoryPath { get; } = repositoryPath;

	/// <summary>
	/// Gets or sets an optional sink for git's incremental output.
	/// </summary>
	/// <remarks>
	/// Forwarded into every <see cref="GitProcessRequest"/> this builder issues. Only the
	/// long-running verbs have any use for it — <c>clone</c>, <c>fetch</c>, <c>push</c>, and
	/// <c>pull</c> all write their progress stream to standard error while they run — so most
	/// builders leave it null and the request carries no sink at all. The sink may be invoked
	/// concurrently by the standard-output and standard-error readers and must be thread-safe.
	/// </remarks>
	protected IProgress<string>? Progress { get; set; }

	/// <summary>
	/// Appends the verb and its options to the argument vector, after the global arguments.
	/// </summary>
	/// <param name="arguments">The vector being assembled.</param>
	protected abstract void AppendVerbArguments(ICollection<string> arguments);

	/// <summary>
	/// Turns a successful invocation's output into the result type.
	/// </summary>
	/// <param name="result">The raw invocation outcome.</param>
	/// <returns>The parsed result.</returns>
	protected abstract TResult ParseResult(GitProcessResult result);

	/// <summary>
	/// Gets a value indicating whether the command passes caller-supplied file paths, which git
	/// must then match literally rather than as pathspec patterns.
	/// </summary>
	/// <remarks>
	/// Only builders that emit paths turn this on. git exports the setting to the processes it
	/// starts, so setting it on every command would also change how hooks read their pathspecs.
	/// </remarks>
	protected virtual bool PassesPathsLiterally => false;

	/// <summary>
	/// Appends <c>--progress</c> when a sink is attached, so git reports transfer progress even
	/// though its standard error is redirected rather than a terminal.
	/// </summary>
	/// <remarks>
	/// Without the flag git writes no counting, "Receiving objects" or "Resolving deltas" lines to a
	/// redirected standard error, which leaves a sink with nothing during the slow network phase. It
	/// is emitted only when someone is listening, so standard error stays small and the diagnostic in
	/// a thrown exception stays short otherwise (issue #154).
	/// </remarks>
	/// <param name="arguments">The vector being assembled.</param>
	/// <exception cref="ArgumentNullException"><paramref name="arguments"/> is <see langword="null"/>.</exception>
	protected void AppendProgressFlag(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		if (Progress is not null)
		{
			arguments.Add("--progress");
		}
	}

	/// <summary>
	/// Appends caller-supplied operands after an end-of-options marker, so a value beginning with a
	/// dash cannot be reinterpreted by git as an option.
	/// </summary>
	/// <param name="arguments">The vector being assembled.</param>
	/// <param name="operands">The operands to append.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="arguments"/> or <paramref name="operands"/> is <see langword="null"/>.
	/// </exception>
	protected static void AppendOperands(ICollection<string> arguments, params string[] operands)
	{
		Ensure.NotNull(arguments);
		Ensure.NotNull(operands);

		// Everything after --end-of-options is an operand, whatever it starts with. This is the
		// second layer behind NotAnOptionAttribute: the attribute stops a dash-leading value being
		// constructed at all, and this stops any that reaches the vector another way from being
		// parsed as a flag.
		arguments.Add("--end-of-options");

		foreach (string operand in operands)
		{
			arguments.Add(operand);
		}
	}

	/// <summary>
	/// Appends <c>--</c> followed by each path, the pathspec tail of a verb that takes revisions and
	/// paths.
	/// </summary>
	/// <remarks>
	/// Any revision has to be appended first: git reads everything after <c>--</c> as a filename. The
	/// <c>--</c> is written even with no paths, so a revision before it is never read as a path
	/// (ktsu-dev/GitIntegration#168). Each caller decides whether the tail is written at all.
	/// </remarks>
	/// <param name="arguments">The vector being assembled.</param>
	/// <param name="paths">The paths to limit the command to, possibly none.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="arguments"/> or <paramref name="paths"/> is <see langword="null"/>.
	/// </exception>
	private protected static void AppendPathspecs(ICollection<string> arguments, IEnumerable<RelativeFilePath> paths)
	{
		Ensure.NotNull(arguments);
		Ensure.NotNull(paths);

		arguments.Add("--");

		foreach (RelativeFilePath path in paths)
		{
			arguments.Add(path.WeakString);
		}
	}

	/// <inheritdoc />
	public IReadOnlyList<string> BuildArguments()
	{
		List<string> arguments = [];

		if (RepositoryPath is not null)
		{
			// RunCommand cannot set a process working directory, so the repository is selected
			// with -C rather than by launching git inside it.
			arguments.Add("-C");
			arguments.Add(RepositoryPath.WeakString);
		}

		// Git must never block on a pager, must not octal-escape non-ASCII paths, and must not
		// emit ANSI color codes, or the output stops being parseable.
		arguments.Add("--no-pager");
		arguments.Add("-c");
		arguments.Add("core.quotepath=false");
		arguments.Add("-c");
		arguments.Add("color.ui=false");

		// Caller paths are RelativeFilePath values naming one file each, so git must not read
		// glob characters or a leading colon in them as pathspec syntax, or "file[1].txt" also
		// matches file1.txt (ktsu-dev/GitIntegration#125). This is a global option, so it has to
		// come before the verb.
		if (PassesPathsLiterally)
		{
			arguments.Add("--literal-pathspecs");
		}

		AppendVerbArguments(arguments);

		return arguments;
	}

	/// <inheritdoc />
	public virtual async Task<TResult> ExecuteAsync(CancellationToken cancellationToken = default)
	{
		GitProcessResult result = await Runner.RunAsync(
			new GitProcessRequest { Arguments = BuildArguments(), Progress = Progress },
			cancellationToken).ConfigureAwait(false);

		if (!result.Success)
		{
			throw CreateException(result);
		}

		return ParseResult(result);
	}

	/// <inheritdoc />
	public virtual async Task<GitResult<TResult>> TryExecuteAsync(CancellationToken cancellationToken = default)
	{
		GitProcessResult result = await Runner.RunAsync(
			new GitProcessRequest { Arguments = BuildArguments(), Progress = Progress },
			cancellationToken).ConfigureAwait(false);

		return result.Success
			? GitResult<TResult>.FromValue(ParseResult(result))
			: GitResult<TResult>.FromError(new GitCommandError
			{
				ExitCode = result.ExitCode,
				Arguments = result.Arguments,
				StandardError = GetDiagnostic(result),
			});
	}

	/// <summary>
	/// Extracts the text that explains why an invocation failed.
	/// </summary>
	/// <remarks>
	/// Standard error by default, which is where git puts a diagnostic for most verbs. Virtual
	/// because it is not where git puts one for every verb: <c>commit</c> announces "nothing to
	/// commit" on standard <em>output</em>, and <c>pull</c> announces a conflict there too, leaving
	/// standard error carrying only fetch progress or nothing at all.
	/// <para>
	/// The seam exists on the base class, used by both <see cref="ExecuteAsync"/> (through
	/// <see cref="CreateException"/>) and <see cref="TryExecuteAsync"/>, so that a verb which
	/// relocates its diagnostic states that once and both entry points follow. Overriding one entry
	/// point alone is what let the two report different text for the identical failure — the
	/// throwing path reporting an empty standard error while the result-based path reported the
	/// real explanation.
	/// </para>
	/// </remarks>
	/// <param name="result">The failed invocation outcome.</param>
	/// <returns>The diagnostic text.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
	protected virtual string GetDiagnostic(GitProcessResult result) => Ensure.NotNull(result).StandardError;

	/// <summary>
	/// Classifies a failed invocation into an exception type.
	/// </summary>
	/// <remarks>
	/// Virtual so a derived builder can recognize the failures specific to its own verb — a
	/// <c>rev-parse</c> builder seeing "unknown revision", for instance — and fall back to
	/// <c>base.CreateException</c> for everything else.
	/// </remarks>
	/// <param name="result">The failed invocation outcome.</param>
	/// <returns>The exception to throw.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
	protected virtual GitCommandException CreateException(GitProcessResult result)
	{
		Ensure.NotNull(result);

		// GetDiagnostic rather than StandardError directly, so a verb that puts its diagnostic on
		// standard output reports the same text here as it does through TryExecuteAsync.
		string diagnostic = GetDiagnostic(result);
		string message = $"git exited with code {result.ExitCode}: {diagnostic.Trim()}";

		// Git reports a missing working tree with a stable phrase and exit code 128. Surfacing it
		// as a distinct type lets callers distinguish "wrong directory" from "command failed".
		//
		// The phrase match relies on an English git, which RunCommandGitProcessRunner guarantees by
		// running every invocation with LC_ALL=C. That guarantee is the whole reason the match is
		// dependable: git translates its messages wherever the host has the catalogs installed, so
		// without a forced locale this would silently miss on a non-English machine and degrade to
		// a generic GitCommandException. Any alternative IGitProcessRunner implementation must
		// force the locale too, or accept that degradation.
		return IsRepositoryNotFound(diagnostic, RepositoryPath)
			? new GitRepositoryNotFoundException(message, result.ExitCode, result.Arguments, diagnostic)
			: new GitCommandException(message, result.ExitCode, result.Arguments, diagnostic);
	}

	private const string NotARepository = "not a git repository";

	/// <summary>
	/// Decides whether a diagnostic says the repository the command was scoped to is missing.
	/// </summary>
	/// <remarks>
	/// <para>
	/// git uses one phrase for two different failures. Discovery finding no repository says
	/// <c>not a git repository (or any of the parent directories): .git</c>, or names a mount point
	/// in place of the parent directories. A gitfile whose target is gone says
	/// <c>not a git repository: &lt;path&gt;</c>, and that gitfile need not be this repository's.
	/// A submodule whose <c>.git/modules/&lt;name&gt;</c> is missing makes <c>status</c> and
	/// <c>diff</c> in the superproject report <c>not a git repository: sub/../.git/modules/sub</c>,
	/// while the superproject itself is a perfectly good repository.
	/// </para>
	/// <para>
	/// So the second form counts only when its path is this repository's own git directory, the
	/// one discovery reaches from <paramref name="repositoryPath"/>: a linked worktree or a
	/// submodule checkout whose administrative directory has gone. Any other path is a failure
	/// further in, and stays a plain <see cref="GitCommandException"/> carrying git's message.
	/// </para>
	/// </remarks>
	/// <param name="diagnostic">The text git wrote to explain the failure.</param>
	/// <param name="repositoryPath">The directory the command ran in, if it was scoped to one.</param>
	/// <returns><see langword="true"/> when the scoped repository itself is missing.</returns>
	internal static bool IsRepositoryNotFound(string diagnostic, AbsoluteDirectoryPath? repositoryPath)
	{
		Ensure.NotNull(diagnostic);

		int index = diagnostic.IndexOf(NotARepository, StringComparison.OrdinalIgnoreCase);

		if (index < 0)
		{
			return false;
		}

		string rest = diagnostic[(index + NotARepository.Length)..];

		if (!rest.StartsWith(": ", StringComparison.Ordinal) || repositoryPath is null)
		{
			return true;
		}

		int end = rest.IndexOfAny(['\r', '\n']);
		string reported = (end < 0 ? rest[2..] : rest[2..end]).Trim();

		if (reported.Length == 0)
		{
			return true;
		}

		(string GitDirectory, string Top)? own = FindOwnGitDirectory(repositoryPath.WeakString);

		// Nothing to compare against, so keep the classification git's phrase has always had.
		if (own is null)
		{
			return true;
		}

		// git prints the path as the gitfile spelt it: absolute, or relative to the directory it is
		// working in. That is the scoped directory, or the top of the working tree once git has
		// moved there.
		return SamePath(Resolve(repositoryPath.WeakString, reported), own.Value.GitDirectory)
			|| SamePath(Resolve(own.Value.Top, reported), own.Value.GitDirectory);
	}

	/// <summary>
	/// Finds the git directory discovery reaches from <paramref name="start"/>, following a gitfile.
	/// </summary>
	/// <param name="start">The directory to walk up from.</param>
	/// <returns>
	/// The full git directory path and the directory holding <c>.git</c>, or <see langword="null"/>
	/// when there is no <c>.git</c> above.
	/// </returns>
	private static (string GitDirectory, string Top)? FindOwnGitDirectory(string start)
	{
		try
		{
			for (DirectoryInfo? directory = new(start); directory is not null; directory = directory.Parent)
			{
				string dotGit = Path.Join(directory.FullName, ".git");

				if (Directory.Exists(dotGit))
				{
					return (Path.GetFullPath(dotGit), directory.FullName);
				}

				if (File.Exists(dotGit))
				{
					foreach (string line in File.ReadLines(dotGit))
					{
						if (line.StartsWith("gitdir:", StringComparison.Ordinal))
						{
							return (Resolve(directory.FullName, line["gitdir:".Length..].Trim()), directory.FullName);
						}
					}

					return null;
				}
			}
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
		{
			// Classification is best effort. An unreadable directory falls back to git's phrase.
		}

		return null;
	}

	/// <summary>
	/// Hosts that fold letter case in paths, where git's spelling and ours may differ only in case.
	/// </summary>
	private static StringComparison PathComparison =>
		OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
			? StringComparison.OrdinalIgnoreCase
			: StringComparison.Ordinal;

	private static string Resolve(string baseDirectory, string path) =>
		Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Join(baseDirectory, path));

	private static bool SamePath(string left, string right) =>
		string.Equals(
			Path.TrimEndingDirectorySeparator(left),
			Path.TrimEndingDirectorySeparator(right),
			PathComparison);
}
