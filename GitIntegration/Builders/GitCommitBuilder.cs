// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;

/// <summary>
/// Records the staged changes as a new commit.
/// </summary>
public interface IGitCommitBuilder : IGitCommandBuilder<GitCommit>
{
	/// <summary>
	/// Adds a body below the subject, separated from it by a blank line.
	/// </summary>
	/// <param name="body">The body text.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="body"/> is <see langword="null"/>.</exception>
	public IGitCommitBuilder WithBody(string body);

	/// <summary>
	/// Records a commit even when nothing is staged.
	/// </summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitCommitBuilder AllowEmpty();

	/// <summary>
	/// Stages every modified and deleted tracked file first, leaving untracked files alone.
	/// </summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitCommitBuilder StageTrackedFiles();

	/// <summary>
	/// Records a different author than the committer.
	/// </summary>
	/// <remarks>
	/// This changes the author only. The committer still comes from git's configuration, which is
	/// how git distinguishes who wrote a change from who applied it.
	/// </remarks>
	/// <param name="name">The author's name.</param>
	/// <param name="email">The author's email address.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="name"/> or <paramref name="email"/> is <see langword="null"/>.
	/// </exception>
	public IGitCommitBuilder WithAuthor(GitAuthorName name, GitAuthorEmail email);
}

/// <summary>
/// Builds <c>git commit</c>, then reads the resulting commit back.
/// </summary>
/// <remarks>
/// One of two verbs in this library that needs two invocations. <c>git commit</c> prints a human
/// summary — <c>[main (root-commit) 6b93c10] first commit</c> — carrying an abbreviated object id
/// and nothing else, and offers no machine-readable alternative. So the commit is followed by a
/// <c>log -1</c> using the same pinned format every other commit in this library is parsed from.
/// </remarks>
/// <param name="runner">Runs the assembled commands.</param>
/// <param name="repositoryPath">The repository to scope the commands to.</param>
/// <param name="message">The commit subject.</param>
internal sealed class GitCommitBuilder(
	IGitProcessRunner runner,
	AbsoluteDirectoryPath repositoryPath,
	GitCommitMessage message)
	: GitCommandBuilder<GitCommit>(runner, repositoryPath), IGitCommitBuilder
{
	// Held separately from the base's nullable RepositoryPath because the readback constructs a
	// GitLogBuilder, which requires a non-null path. Commit is always repository-scoped.
	private readonly AbsoluteDirectoryPath _repositoryPath = Ensure.NotNull(repositoryPath);
	private readonly GitCommitMessage _message = Ensure.NotNull(message);
	private string? _body;
	private string? _author;
	private bool _allowEmpty;
	private bool _stageTrackedFiles;

	/// <inheritdoc />
	public IGitCommitBuilder WithBody(string body)
	{
		_body = Ensure.NotNull(body);
		return this;
	}

	/// <inheritdoc />
	public IGitCommitBuilder AllowEmpty()
	{
		_allowEmpty = true;
		return this;
	}

	/// <inheritdoc />
	public IGitCommitBuilder StageTrackedFiles()
	{
		_stageTrackedFiles = true;
		return this;
	}

	/// <inheritdoc />
	public IGitCommitBuilder WithAuthor(GitAuthorName name, GitAuthorEmail email)
	{
		// git parses a single "Name <email>" string here; passing two arguments would make it read
		// the second as a pathspec.
		_author = $"{Ensure.NotNull(name).WeakString} <{Ensure.NotNull(email).WeakString}>";
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("commit");

		if (_stageTrackedFiles)
		{
			arguments.Add("--all");
		}

		if (_allowEmpty)
		{
			arguments.Add("--allow-empty");
		}

		if (_author is not null)
		{
			arguments.Add("--author=" + _author);
		}

		// --message rather than -m, and repeated for the body: git joins repeated values with a
		// blank line between them, which is precisely the subject-then-body convention.
		arguments.Add("--message");
		arguments.Add(_message.WeakString);

		if (_body is not null)
		{
			arguments.Add("--message");
			arguments.Add(_body);
		}
	}

	/// <summary>
	/// Turns the readback invocation's output into the committed <see cref="GitCommit"/>.
	/// </summary>
	/// <remarks>
	/// Called with the output of the <c>log -1</c> readback, not of the commit itself — the base
	/// class never invokes it here, because <see cref="ExecuteAsync"/> is overridden.
	/// </remarks>
	/// <param name="result">The readback invocation's outcome.</param>
	/// <returns>The commit that was just recorded.</returns>
	/// <exception cref="GitParseException">The readback returned no commit.</exception>
	protected override GitCommit ParseResult(GitProcessResult result)
	{
		IReadOnlyList<GitCommit> commits = GitLogParser.Parse(Ensure.NotNull(result).StandardOutput);

		return commits.Count > 0
			? commits[0]
			: throw new GitParseException(
				"git reported a successful commit but reading it back returned no commit.");
	}

	/// <inheritdoc />
	public override async Task<GitCommit> ExecuteAsync(CancellationToken cancellationToken = default)
	{
		GitProcessResult result = await Runner.RunAsync(
			new GitProcessRequest { Arguments = BuildArguments(), Progress = Progress },
			cancellationToken).ConfigureAwait(false);

		if (!result.Success)
		{
			throw CreateException(result);
		}

		return await ReadBackAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public override async Task<GitResult<GitCommit>> TryExecuteAsync(CancellationToken cancellationToken = default)
	{
		GitProcessResult result = await Runner.RunAsync(
			new GitProcessRequest { Arguments = BuildArguments(), Progress = Progress },
			cancellationToken).ConfigureAwait(false);

		// The readback is skipped entirely when the commit failed — there is nothing to read back,
		// and running it would report the previous commit as though it were this one.
		return result.Success
			? GitResult<GitCommit>.FromValue(await ReadBackAsync(cancellationToken).ConfigureAwait(false))
			: GitResult<GitCommit>.FromError(new GitCommandError
			{
				ExitCode = result.ExitCode,
				Arguments = result.Arguments,
				StandardError = result.StandardError,
			});
	}

	/// <summary>
	/// Classifies a failed commit, recognising the one failure that is an ordinary program state.
	/// </summary>
	/// <remarks>
	/// Overridden because the base class inspects standard error and git reports "nothing to
	/// commit" on standard <em>output</em>, leaving standard error empty. Both of git's phrasings
	/// are matched: the tree may be clean, or it may hold only untracked files, and neither message
	/// contains the other. The match depends on the <c>LC_ALL=C</c> that
	/// <c>RunCommandGitProcessRunner</c> forces on every invocation.
	/// </remarks>
	/// <param name="result">The failed invocation outcome.</param>
	/// <returns>The exception to throw.</returns>
	protected override GitCommandException CreateException(GitProcessResult result)
	{
		Ensure.NotNull(result);

		if (result.StandardOutput.Contains("nothing to commit", StringComparison.Ordinal) ||
			result.StandardOutput.Contains("nothing added to commit", StringComparison.Ordinal))
		{
			return new GitNothingToCommitException(
				$"There is nothing staged to commit: {result.StandardOutput.Trim()}",
				result.ExitCode,
				result.Arguments,
				result.StandardError);
		}

		return base.CreateException(result);
	}

	private async Task<GitCommit> ReadBackAsync(CancellationToken cancellationToken)
	{
		// GitLogBuilder is used for its argument vector only, so the pinned format string stays in
		// exactly one place. Running it here rather than calling its ExecuteAsync keeps the "no
		// commit came back" failure attributable to the commit, not to a stray log call.
		GitLogBuilder log = new(Runner, _repositoryPath);
		_ = log.Take(1);

		GitProcessResult result = await Runner.RunAsync(
			new GitProcessRequest { Arguments = log.BuildArguments() },
			cancellationToken).ConfigureAwait(false);

		if (!result.Success)
		{
			throw base.CreateException(result);
		}

		return ParseResult(result);
	}
}
