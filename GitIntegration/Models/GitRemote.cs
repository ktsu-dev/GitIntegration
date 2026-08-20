// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

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
	/// is configured.
	/// </summary>
	public required GitRepositoryRemotePath PushUrl { get; init; }
}
