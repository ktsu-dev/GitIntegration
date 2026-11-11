// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.IO;

/// <summary>
/// Holds the README to the single sign-on behaviour <see cref="GitHubProvider"/> ships, which callers
/// could otherwise find only by reading the source.
/// </summary>
[TestClass]
public class ReadmeSingleSignOnTests
{
	private static string Readme() =>
		File.ReadAllText(Path.Join(AppContext.BaseDirectory, "README.md"));

	private static string GitHubProviderSection()
	{
		string readme = Readme();
		int start = readme.IndexOf("### `GitHubProvider`", StringComparison.Ordinal);
		Assert.AreNotEqual(-1, start, "The README's GitHubProvider section was not found.");

		int end = readme.IndexOf("\n### ", start + 1, StringComparison.Ordinal);
		return end == -1 ? readme[start..] : readme[start..end];
	}

	[TestMethod]
	public void GitHubProviderSectionDocumentsTheSingleSignOnException()
	{
		string section = GitHubProviderSection();

		StringAssert.Contains(section, "#### Single sign-on");
		StringAssert.Contains(section, "GitHostingAuthenticationException");
		StringAssert.Contains(section, "X-GitHub-SSO");
		StringAssert.Contains(section, "URL to authorise it at");
	}
}
