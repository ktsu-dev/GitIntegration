// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;

/// <summary>
/// Changes the URL a remote points at.
/// </summary>
/// <remarks>
/// A remote can hold several values for the key this writes, such as a repository that pushes to
/// two mirrors and so reports two <see cref="GitRemote.PushUrls"/>. <c>git remote set-url</c>
/// refuses to change such a key, so this replaces every value with the one URL instead. Afterwards
/// the remote has exactly one URL of the kind written: one push URL with <see cref="ForPushOnly"/>,
/// one fetch URL without it.
/// </remarks>
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
/// Builds <c>git remote set-url &lt;name&gt; &lt;url&gt;</c>, or
/// <c>git config --local --replace-all remote.&lt;name&gt;.&lt;key&gt; &lt;url&gt;</c> when the key holds
/// several values.
/// </summary>
/// <remarks>
/// <c>set-url</c> stays the usual path because it is what reports a remote that does not exist;
/// <c>config</c> would quietly create one. A key with several values already proves the remote
/// exists, so falling back to <c>config</c> there loses nothing.
/// </remarks>
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
	private bool _replaceAll;

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

		if (_replaceAll)
		{
			arguments.Add("config");
			arguments.Add("--local");
			arguments.Add("--replace-all");
			arguments.Add("--end-of-options");
			arguments.Add(ConfigKey);
			arguments.Add(_url.WeakString);
			return;
		}

		arguments.Add("remote");
		arguments.Add("set-url");

		if (_forPushOnly)
		{
			arguments.Add("--push");
		}

		AppendOperands(arguments, _name.WeakString, _url.WeakString);
	}

	/// <inheritdoc />
	public override async Task<GitCompleted> ExecuteAsync(CancellationToken cancellationToken = default)
	{
		_replaceAll = await HasSeveralValuesAsync(cancellationToken).ConfigureAwait(false);

		return await base.ExecuteAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public override async Task<GitResult<GitCompleted>> TryExecuteAsync(CancellationToken cancellationToken = default)
	{
		_replaceAll = await HasSeveralValuesAsync(cancellationToken).ConfigureAwait(false);

		return await base.TryExecuteAsync(cancellationToken).ConfigureAwait(false);
	}

	private string ConfigKey => $"remote.{_name.WeakString}.{(_forPushOnly ? "pushurl" : "url")}";

	private async Task<bool> HasSeveralValuesAsync(CancellationToken cancellationToken)
	{
		// --local, because set-url writes the repository's own config, and it is several values
		// there that make it fail. A value inherited from the global config is not replaced either
		// way. TryExecuteAsync, because a key with no value exits 1, and that is an answer, not a
		// failure: set-url then adds the value or reports the missing remote.
		GitResult<string> probe = await new GitTextBuilder(
			Runner, RepositoryPath, "config", "--local", "--get-all", "--end-of-options", ConfigKey)
			.TryExecuteAsync(cancellationToken).ConfigureAwait(false);

		return probe.Success
			&& probe.Value is not null
			&& probe.Value.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length > 1;
	}

	/// <inheritdoc />
	protected override GitCompleted ParseResult(GitProcessResult result) =>
		new() { Arguments = Ensure.NotNull(result).Arguments };
}
