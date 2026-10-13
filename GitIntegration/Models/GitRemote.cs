// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

/// <summary>
/// One configured remote.
/// </summary>
public sealed record GitRemote
{
	/// <summary>Gets the remote's name, such as <c>origin</c>.</summary>
	public required GitRemoteName Name { get; init; }

	/// <summary>Gets the URL git fetches from.</summary>
	public required GitRepositoryRemotePath FetchUrl { get; init; }

	/// <summary>
	/// Gets the URL git pushes to. Equal to <see cref="FetchUrl"/> unless <c>remote.*.pushurl</c>
	/// is configured. When several push URLs are configured this is the first, in git's order;
	/// <see cref="PushUrls"/> carries them all.
	/// </summary>
	public required GitRepositoryRemotePath PushUrl { get; init; }

	/// <summary>
	/// Gets every URL git pushes to, in git's order. <c>git push</c> sends to all of them when
	/// <c>remote.*.pushurl</c> is configured more than once. Defaults to <see cref="PushUrl"/> alone
	/// when not set.
	/// </summary>
	public IReadOnlyList<GitRepositoryRemotePath> PushUrls
	{
		get => field ?? [PushUrl];
		init;
	}
}
