// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

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
	/// Opens <see cref="WebURI"/> in the default browser.
	/// </summary>
	/// <remarks>
	/// Only an absolute <c>http</c> or <c>https</c> URI is launched. Anything else — a
	/// <see langword="null"/> <see cref="WebURI"/>, a <c>file:</c> URI, a bare filesystem path, a
	/// relative string, or any other registered handler scheme — is silently ignored. The check is
	/// deliberate rather than defensive: the launch runs through <c>UseShellExecute</c>, so without
	/// it any non-blank string would be handed to the shell as something to execute, and
	/// <see cref="GitRepositoryWebURI"/> only guarantees that the value is not blank. The value is
	/// expected to be populated from a hosting provider's API response, which is remote data.
	/// </remarks>
	public void OpenWebClient()
	{
		if (!IsBrowsableUri(WebURI?.WeakString, out Uri? uri))
		{
			return;
		}

		// UseShellExecute with the URI as FileName is the portable form. The previous
		// implementation hardcoded "explorer", which does not exist on Linux or macOS.
		_ = Process.Start(new ProcessStartInfo
		{
			FileName = uri.AbsoluteUri,
			UseShellExecute = true,
		});
	}

	/// <summary>
	/// Decides whether a value is safe to hand to the shell as something to open.
	/// </summary>
	/// <remarks>
	/// Separate from <see cref="OpenWebClient"/> so the decision can be asserted in tests without
	/// the side effect of actually launching a browser.
	/// </remarks>
	/// <param name="value">The candidate value, which may be <see langword="null"/>.</param>
	/// <param name="uri">The parsed absolute http or https URI, when the value is accepted.</param>
	/// <returns>
	/// <see langword="true"/> when <paramref name="value"/> is an absolute <c>http</c> or
	/// <c>https</c> URI; otherwise, <see langword="false"/>.
	/// </returns>
	internal static bool IsBrowsableUri(string? value, [NotNullWhen(true)] out Uri? uri)
	{
		if (value is null ||
			!Uri.TryCreate(value, UriKind.Absolute, out uri) ||
			(uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
		{
			uri = null;
			return false;
		}

		return true;
	}
}
