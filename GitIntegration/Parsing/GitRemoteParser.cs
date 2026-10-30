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
		Dictionary<string, List<string>> pushUrls = new(StringComparer.Ordinal);

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

			// A remote.<name>.* section with no url (a global remote.origin.prune=true in a
			// repository without origin, say) is listed as the name and a bare tab. git itself
			// treats such a remote as non-existent, so it is skipped rather than rejected.
			if (remainder.Length == 0)
			{
				continue;
			}

			if (!order.Contains(name))
			{
				order.Add(name);
			}

			remainder = WithoutPartialCloneFilter(remainder);

			// Anchored on the suffix rather than the last space: a local remote is a filesystem
			// path and may legitimately contain spaces.
			if (remainder.EndsWith(FetchSuffix, StringComparison.Ordinal))
			{
				fetchUrls[name] = remainder[..^FetchSuffix.Length];
			}
			else if (remainder.EndsWith(PushSuffix, StringComparison.Ordinal))
			{
				// git prints one push line per remote.<name>.pushurl, and pushes to every one of
				// them, so each is kept in git's order rather than the last overwriting the rest.
				if (!pushUrls.TryGetValue(name, out List<string>? urls))
				{
					urls = [];
					pushUrls[name] = urls;
				}

				urls.Add(remainder[..^PushSuffix.Length]);
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
			List<string> pushUrlList = pushUrls.TryGetValue(name, out List<string>? urls) ? urls : [];

			// git always prints both directions, but a remote configured with only one is still
			// better described by the URL that exists than rejected outright.
			fetchUrl ??= pushUrlList.Count > 0 ? pushUrlList[0] : null;

			if (fetchUrl is null)
			{
				throw new GitParseException($"git listed the remote '{name}' with no URL.");
			}

			if (pushUrlList.Count == 0)
			{
				pushUrlList.Add(fetchUrl);
			}

			List<GitRepositoryRemotePath> pushPaths = pushUrlList.ConvertAll(
				url => GitParseValues.ToSemantic<GitRepositoryRemotePath>(url, "remote push url"));

			remotes.Add(new GitRemote
			{
				Name = GitParseValues.ToSemantic<GitRemoteName>(name, "remote name"),
				FetchUrl = GitParseValues.ToSemantic<GitRepositoryRemotePath>(fetchUrl, "remote fetch url"),
				PushUrl = pushPaths[0],
				PushUrls = pushPaths,
			});
		}

		return remotes;
	}

	/// <summary>
	/// Drops the partial-clone filter git appends to a fetch line, as in
	/// <c>url (fetch) [blob:none]</c>, when <c>remote.&lt;name&gt;.partialclonefilter</c> is set.
	/// </summary>
	/// <param name="remainder">The line after the remote name and its tab.</param>
	/// <returns>The line ending in the direction marker, or <paramref name="remainder"/> unchanged.</returns>
	private static string WithoutPartialCloneFilter(string remainder)
	{
		if (!remainder.EndsWith(']'))
		{
			return remainder;
		}

		int open = remainder.LastIndexOf(" [", StringComparison.Ordinal);

		// Only a filter that follows the fetch marker is dropped, so a URL that happens to end in a
		// bracket is not cut short and a line with no marker is still rejected.
		return open >= 0 && remainder[..open].EndsWith(FetchSuffix, StringComparison.Ordinal)
			? remainder[..open]
			: remainder;
	}
}
