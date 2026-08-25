// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using ktsu.Semantics.Strings;

/// <summary>
/// A strongly-typed name for a git hosting provider, such as <c>GitHub</c> or <c>AzureDevOps</c>.
/// </summary>
[HasNonWhitespaceContent]
public sealed record GitProviderName : SemanticString<GitProviderName> { }

/// <summary>
/// A strongly-typed owner of repositories within a hosting provider: a GitHub user or
/// organisation, or an Azure DevOps organisation.
/// </summary>
[HasNonWhitespaceContent]
public sealed record GitProviderOwner : SemanticString<GitProviderOwner> { }

/// <summary>
/// A strongly-typed Azure DevOps project name. Azure DevOps nests repositories under a project,
/// which GitHub has no equivalent of.
/// </summary>
[HasNonWhitespaceContent]
public sealed record AzureDevOpsProjectName : SemanticString<AzureDevOpsProjectName> { }

/// <summary>A pull request's host-assigned number.</summary>
/// <remarks>
/// <see cref="RegexMatchAttribute"/> alone would let an empty value through — it validates shape,
/// not presence, so it short-circuits to success on an empty string. <c>Number</c> is
/// <see langword="required"/> and so cannot express "not known" by being null; empty is its only
/// failure mode, and <see cref="HasNonWhitespaceContentAttribute"/> is what catches it.
/// </remarks>
[HasNonWhitespaceContent]
[RegexMatch(@"^[0-9]+$")]
public sealed record GitPullRequestNumber : SemanticString<GitPullRequestNumber> { }

/// <summary>A pull request's title.</summary>
[HasNonWhitespaceContent]
public sealed record GitPullRequestTitle : SemanticString<GitPullRequestTitle> { }

/// <summary>
/// The host's identifier for the account that opened a pull request.
/// </summary>
/// <remarks>
/// The hosts do not agree on what identifies a user: GitHub supplies a login, Azure DevOps a
/// unique name that is usually an email address. This type carries whichever the host gave,
/// unaltered, rather than normalising two different concepts into one that matches neither.
/// </remarks>
[HasNonWhitespaceContent]
public sealed record GitPullRequestAuthor : SemanticString<GitPullRequestAuthor> { }

/// <summary>The browser address of a pull request.</summary>
/// <remarks>
/// No URI-shape validation: GitHub and Azure DevOps web addresses differ enough that guessing a
/// pattern would reject legitimate values from one host or the other. Non-empty is the only
/// invariant worth enforcing here.
/// </remarks>
[HasNonWhitespaceContent]
public sealed record GitPullRequestWebURI : SemanticString<GitPullRequestWebURI> { }
