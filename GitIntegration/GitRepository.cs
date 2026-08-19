// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Diagnostics;

using ktsu.Semantics.Paths;

/// <summary>
/// Represents a git repository: where its working copy is, and what is known about the host it
/// came from.
/// </summary>
public class GitRepository
{
	/// <summary>
	/// Gets the local filesystem path where the repository is, or is intended to be, cloned.
	/// </summary>
	public required AbsoluteDirectoryPath LocalPath { get; init; }

	/// <summary>
	/// Gets the repository name, or <see langword="null"/> when it is not known.
	/// </summary>
	public GitRepositoryName? Name { get; init; }

	/// <summary>
	/// Gets the browser-facing URI, or <see langword="null"/> when it is not known.
	/// </summary>
	public GitRepositoryWebURI? WebURI { get; init; }

	/// <summary>
	/// Gets the remote path, or <see langword="null"/> when it is not known.
	/// </summary>
	public GitRepositoryRemotePath? RemotePath { get; init; }

	/// <summary>
	/// Opens <see cref="WebURI"/> in the default browser. Does nothing when it is
	/// <see langword="null"/>.
	/// </summary>
	public void OpenWebClient()
	{
		if (WebURI is null)
		{
			return;
		}

		// UseShellExecute with the URI as FileName is the portable form. The previous
		// implementation hardcoded "explorer", which does not exist on Linux or macOS.
		_ = Process.Start(new ProcessStartInfo
		{
			FileName = WebURI.WeakString,
			UseShellExecute = true,
		});
	}
}
