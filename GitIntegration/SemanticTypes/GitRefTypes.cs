// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Diagnostics.CodeAnalysis;

using ktsu.Semantics.Strings;

/// <summary>
/// A strongly-typed git branch name, such as <c>main</c> or <c>feature/git-v2</c>.
/// </summary>
[HasNonWhitespaceContent]
[NotAnOption]
public sealed record GitBranchName : SemanticString<GitBranchName> { }

/// <summary>
/// A strongly-typed git remote name, such as <c>origin</c>.
/// </summary>
[HasNonWhitespaceContent]
[NotAnOption]
public sealed record GitRemoteName : SemanticString<GitRemoteName> { }

/// <summary>
/// A strongly-typed git reference, which may be a branch, tag, SHA, or revision expression.
/// </summary>
[HasNonWhitespaceContent]
[NotAnOption]
public sealed record GitRefName : SemanticString<GitRefName> { }

/// <summary>
/// A strongly-typed git object identifier, either abbreviated or full length.
/// </summary>
/// <remarks>
/// Values are canonicalised to lowercase, because git emits lowercase but accepts either case as
/// input, and callers should be able to compare two SHAs for equality without normalising first.
/// </remarks>
[RegexMatch("^[0-9a-fA-F]{4,40}$")]
public sealed record GitCommitSha : SemanticString<GitCommitSha>
{
	/// <inheritdoc />
	[SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "The SemanticString<T> base class guarantees input is non-null before invoking MakeCanonical.")]
	protected override string MakeCanonical(string input) => input.Trim().ToLowerInvariant();
}
