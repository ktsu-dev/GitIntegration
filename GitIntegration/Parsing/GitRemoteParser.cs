// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
using System.Collections.Generic;

/// <summary>
/// Reads <c>git remote -v</c>.
/// </summary>
/// <remarks>
/// The only verb in this library parsed from a format git does not let us specify. It is
/// nonetheless machine-stable: each line is the remote name, a tab, the URL, and a space-prefixed
/// direction marker. There is no porcelain alternative — <c>remote get-url</c> reports one remote
/// at a time and would need a listing invocation first.
/// </remarks>
internal static class GitRemoteParser
{
	private const string FetchSuffix = " (fetch)";
	private const string PushSuffix = " (push)";

	/// <summary>
	/// Parses verbose remote listing output.
	/// </summary>
	/// <param name="output">Everything git wrote to standard output.</param>
	/// <returns>The remotes, in the order git listed them.</returns>
	/// <exception cref="GitParseException">A line did not have the expected shape.</exception>
	internal static IReadOnlyList<GitRemote> Parse(string output)
	{
		Ensure.NotNull(output);

		// git prints each remote twice, once per direction. The order list preserves git's own
		// ordering, which a dictionary alone would not.
		List<string> order = [];
		Dictionary<string, string> fetchUrls = new(StringComparer.Ordinal);
		Dictionary<string, string> pushUrls = new(StringComparer.Ordinal);

		foreach (string line in output.Split('\n'))
		{
			string record = line.TrimEnd('\r');

			if (record.Length == 0)
			{
				continue;
			}

			int tab = record.IndexOf('\t');

			if (tab <= 0)
			{
				throw new GitParseException($"Malformed 'remote -v' line: '{record}'.");
			}

			string name = record[..tab];
			string remainder = record[(tab + 1)..];

			if (!order.Contains(name))
			{
				order.Add(name);
			}

			// Anchored on the suffix rather than the last space: a local remote is a filesystem
			// path and may legitimately contain spaces.
			if (remainder.EndsWith(FetchSuffix, StringComparison.Ordinal))
			{
				fetchUrls[name] = remainder[..^FetchSuffix.Length];
			}
			else if (remainder.EndsWith(PushSuffix, StringComparison.Ordinal))
			{
				pushUrls[name] = remainder[..^PushSuffix.Length];
			}
			else
			{
				throw new GitParseException($"Malformed 'remote -v' line: '{record}'.");
			}
		}

		List<GitRemote> remotes = [];

		foreach (string name in order)
		{
			_ = fetchUrls.TryGetValue(name, out string? fetchUrl);
			_ = pushUrls.TryGetValue(name, out string? pushUrl);

			// git always prints both directions, but a remote configured with only one is still
			// better described by the URL that exists than rejected outright.
			fetchUrl ??= pushUrl;
			pushUrl ??= fetchUrl;

			if (fetchUrl is null || pushUrl is null)
			{
				throw new GitParseException($"git listed the remote '{name}' with no URL.");
			}

			remotes.Add(new GitRemote
			{
				Name = GitParseValues.ToSemantic<GitRemoteName>(name, "remote name"),
				FetchUrl = GitParseValues.ToSemantic<GitRepositoryRemotePath>(fetchUrl, "remote fetch url"),
				PushUrl = GitParseValues.ToSemantic<GitRepositoryRemotePath>(pushUrl, "remote push url"),
			});
		}

		return remotes;
	}
}
