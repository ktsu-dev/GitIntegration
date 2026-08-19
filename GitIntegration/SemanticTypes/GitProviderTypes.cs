// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using ktsu.Semantics.Strings;

/// <summary>
/// A strongly-typed identifier for a git hosting provider.
/// </summary>
[HasNonWhitespaceContent]
public sealed record GitProviderGUID : SemanticString<GitProviderGUID> { }

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
