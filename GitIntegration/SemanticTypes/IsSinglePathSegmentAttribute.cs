// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;

using ktsu.Semantics.Strings;

/// <summary>
/// Validates that a value is a single path segment, so it cannot reshape a request path it is
/// substituted into.
/// </summary>
/// <remarks>
/// <para>
/// The hosting counterpart to <see cref="NotAnOptionAttribute"/>. Where that attribute stops a
/// value being reinterpreted by <c>git</c> as a flag, this one stops a value being reinterpreted by
/// a URL or a filesystem as more than one segment. A repository name is substituted directly into
/// a request path by both providers, and the two escape it differently:
/// <c>AzureDevOpsProvider</c> runs <see cref="Uri.EscapeDataString(string)"/> over every path
/// component, while <c>GitHubProvider</c> hands the raw value to Octokit, which formats it into a
/// relative URI without escaping. Validating at construction fixes both sites at the source rather
/// than at each use, and removes the inconsistency between them.
/// </para>
/// <para>
/// A denylist of structural characters rather than an allowlist of permitted ones, because the two
/// hosts do not agree on what a name may contain: GitHub restricts names to
/// <c>A-Z a-z 0-9 . _ -</c>, while Azure DevOps permits spaces and a wider set besides. An
/// allowlist tight enough to describe GitHub would reject legitimate Azure DevOps repositories, and
/// one loose enough for Azure DevOps would not be an allowlist worth having. What both hosts agree
/// on is that a repository name is one segment, which is exactly what this rejects violations of.
/// </para>
/// <para>
/// Percent-encoded separators are deliberately not part of the check. Azure DevOps escapes the
/// value before it reaches a URL, so a <c>%2F</c> in a name arrives at the service as <c>%252F</c>
/// and addresses a repository literally named <c>%2F</c> rather than traversing anywhere; and
/// GitHub's own naming rules exclude <c>%</c> entirely, so no such name can name a real repository
/// there. Rejecting <c>%</c> here would therefore close no reachable path while risking a
/// legitimate name on a host this library has not surveyed.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class IsSinglePathSegmentAttribute : SemanticStringValidationAttribute
{
	/// <summary>
	/// The characters that separate one path segment from the next, or a path from what follows it.
	/// </summary>
	/// <remarks>
	/// <c>/</c> and <c>\</c> separate segments in a URL and on a filesystem respectively; <c>?</c>
	/// and <c>#</c> end the path entirely and begin a query string or a fragment, so a name carrying
	/// one could append query parameters to a request rather than merely redirect it.
	/// </remarks>
	private static readonly char[] SegmentSeparators = ['/', '\\', '?', '#'];

	/// <summary>
	/// Validates that the supplied value names one path segment and no more.
	/// </summary>
	/// <param name="semanticString">The semantic string to validate.</param>
	/// <returns>
	/// <see langword="true"/> when the value is non-empty, contains no path separator, and is
	/// neither of the relative segments <c>.</c> and <c>..</c>; otherwise, <see langword="false"/>.
	/// </returns>
	public override bool Validate(ISemanticString semanticString)
	{
		string? value = semanticString?.WeakString;

		if (string.IsNullOrEmpty(value))
		{
			return false;
		}

		// "." and ".." are rejected as whole values rather than as substrings. A name such as
		// "my..repo" carries no traversal — a relative segment only means anything when a separator
		// delimits it, and every separator is already rejected above.
		return value.IndexOfAny(SegmentSeparators) < 0 && value is not ("." or "..");
	}
}
