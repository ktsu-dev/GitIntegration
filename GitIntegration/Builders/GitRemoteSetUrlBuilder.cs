// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

using ktsu.Semantics.Paths;

/// <summary>
/// Changes the URL a remote points at.
/// </summary>
public interface IGitRemoteSetUrlBuilder : IGitCommandBuilder<GitCompleted>
{
	/// <summary>
	/// Sets only the push URL, leaving the fetch URL as it was.
	/// </summary>
	/// <remarks>
	/// This is what makes <see cref="GitRemote.FetchUrl"/> and <see cref="GitRemote.PushUrl"/>
	/// differ — a repository that fetches over HTTPS but pushes over SSH, for instance.
	/// </remarks>
	/// <returns>The same builder, to allow chaining.</returns>
	public IGitRemoteSetUrlBuilder ForPushOnly();
}

/// <summary>
/// Builds <c>git remote set-url &lt;name&gt; &lt;url&gt;</c>.
/// </summary>
/// <param name="runner">Runs the assembled command.</param>
/// <param name="repositoryPath">The repository to scope the command to.</param>
/// <param name="name">The remote to change.</param>
/// <param name="url">The URL to set.</param>
internal sealed class GitRemoteSetUrlBuilder(
	IGitProcessRunner runner,
	AbsoluteDirectoryPath repositoryPath,
	GitRemoteName name,
	GitRepositoryRemotePath url)
	: GitCommandBuilder<GitCompleted>(runner, repositoryPath), IGitRemoteSetUrlBuilder
{
	private readonly GitRemoteName _name = Ensure.NotNull(name);
	private readonly GitRepositoryRemotePath _url = Ensure.NotNull(url);
	private bool _forPushOnly;

	/// <inheritdoc />
	public IGitRemoteSetUrlBuilder ForPushOnly()
	{
		_forPushOnly = true;
		return this;
	}

	/// <inheritdoc />
	protected override void AppendVerbArguments(ICollection<string> arguments)
	{
		Ensure.NotNull(arguments);

		arguments.Add("remote");
		arguments.Add("set-url");

		if (_forPushOnly)
		{
			arguments.Add("--push");
		}

		AppendOperands(arguments, _name.WeakString, _url.WeakString);
	}

	/// <inheritdoc />
	protected override GitCompleted ParseResult(GitProcessResult result) =>
		new() { Arguments = Ensure.NotNull(result).Arguments };
}
