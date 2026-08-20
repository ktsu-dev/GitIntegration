// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Adds a remote.
/// </summary>
public interface IGitRemoteAddBuilder : IGitCommandBuilder<GitCompleted>
{
	/// <summary>Fetches from the remote immediately after adding it.</summary>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitRemoteAddBuilder WithFetch();
}

/// <summary>
/// Builds <c>git remote add &lt;name&gt; &lt;url&gt;</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
/// <param name="name">The remote to add.</param>
/// <param name="url">The URL the remote points at.</param>
internal sealed class GitRemoteAddBuilder(
	IGitProcessRunner runner,
	AbsoluteDirectoryPath repositoryPath,
	GitRemoteName name,
	GitRepositoryRemotePath url)
	: GitCommandBuilder<GitCompleted>(runner, repositoryPath), IGitRemoteAddBuilder
{
	private readonly GitRemoteName _name = Ensure.NotNull(name);
	private readonly GitRepositoryRemotePath _url = Ensure.NotNull(url);
	private bool _withFetch;

	/// <inheritdoc />
	public IGitRemoteAddBuilder WithFetch()
	{
		_withFetch = true;
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("remote");
		arguments.Add("add");

		// -f has no long form on this subcommand.
		if (_withFetch)
		{
			arguments.Add("-f");
		}

		// Both operands are caller-supplied, and the URL especially so: a remote path beginning
		// with a dash is the option-injection case NotAnOptionAttribute and this marker both guard.
		AppendOperands(arguments, _name.WeakString, _url.WeakString);
	}

	/// <inheritdoc />
	protected override GitCompleted ParseResult(GitProcessResult result) =>
		new() { Arguments = Ensure.NotNull(result).Arguments };
}
