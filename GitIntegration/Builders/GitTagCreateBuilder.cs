// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Creates a tag.
/// </summary>
/// <remarks>
/// Both kinds of git tag are reachable from here. Without <see cref="Annotating"/> the result is a
/// lightweight tag: a reference pointing straight at a commit. With it, git writes a tag object
/// carrying the message, the tagger, and the date, and points the reference at that instead. This is
/// the one place the branch pattern does not map one-to-one, since a branch has no equivalent of an
/// annotation.
/// </remarks>
public interface IGitTagCreateBuilder : IGitCommandBuilder<GitCompleted>
{
	/// <summary>
	/// Points the new tag at this revision instead of at HEAD.
	/// </summary>
	/// <param name="target">The revision the tag should name.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
	public IGitTagCreateBuilder At(GitRefName target);

	/// <summary>
	/// Writes an annotated tag carrying this message, rather than a lightweight one.
	/// </summary>
	/// <remarks>
	/// The message is what makes the tag annotated, so the two are one method rather than a flag and
	/// a separate setter. Git has no annotated tag without a message: <c>git tag -a</c> with none
	/// supplied opens an editor, which no invocation this library makes could ever answer.
	/// </remarks>
	/// <param name="message">The tag message.</param>
	/// <returns>The same builder, to allow chaining.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
	public IGitTagCreateBuilder Annotating(GitCommitMessage message);

	/// <summary>
	/// Replaces the tag if it already exists, instead of failing.
	/// </summary>
	/// <remarks>
	/// Git refuses to overwrite an existing tag and exits with code 128 without this. Note that a tag
	/// already fetched by someone else is not moved by this — it only changes the local reference —
	/// which is why moving a published tag is a thing git makes deliberately awkward.
	/// </remarks>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitTagCreateBuilder Force();
}

/// <summary>
/// Builds <c>git tag [-a -m &lt;message&gt;] &lt;name&gt; [&lt;target&gt;]</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
/// <param name="name">The tag to create.</param>
internal sealed class GitTagCreateBuilder(
	IGitProcessRunner runner,
	AbsoluteDirectoryPath repositoryPath,
	GitTagName name)
	: GitCommandBuilder<GitCompleted>(runner, repositoryPath), IGitTagCreateBuilder
{
	private readonly GitTagName _name = Ensure.NotNull(name);
	private GitRefName? _target;
	private GitCommitMessage? _message;
	private bool _force;

	/// <inheritdoc />
	public IGitTagCreateBuilder At(GitRefName target)
	{
		_target = Ensure.NotNull(target);
		return this;
	}

	/// <inheritdoc />
	public IGitTagCreateBuilder Annotating(GitCommitMessage message)
	{
		_message = Ensure.NotNull(message);
		return this;
	}

	/// <inheritdoc />
	public IGitTagCreateBuilder Force()
	{
		_force = true;
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("tag");

		if (_force)
		{
			arguments.Add("--force");
		}

		// --annotate and --message travel together and are emitted adjacently, because either alone
		// is a different command: --annotate without a message opens an editor, which output
		// redirection makes impossible to answer, and --message without --annotate makes an
		// annotated tag anyway but only as a side effect git documents rather than promises.
		if (_message is not null)
		{
			arguments.Add("--annotate");
			arguments.Add("--message");
			arguments.Add(_message.WeakString);
		}

		// Order is load-bearing, exactly as it is for branch creation: git tag takes <name> then an
		// optional <commit> positionally, and swapping them tags the wrong object under the wrong
		// name without complaint.
		if (_target is null)
		{
			AppendOperands(arguments, _name.WeakString);
		}
		else
		{
			AppendOperands(arguments, _name.WeakString, _target.WeakString);
		}
	}

	/// <inheritdoc />
	protected override GitCompleted ParseResult(GitProcessResult result) =>
		new() { Arguments = Ensure.NotNull(result).Arguments };
}
