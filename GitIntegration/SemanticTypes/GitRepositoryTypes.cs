// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using ktsu.Semantics.Strings;

/// <summary>
/// A strongly-typed repository name.
/// </summary>
/// <remarks>
/// One path segment, enforced by <see cref="IsSinglePathSegmentAttribute"/>. Both hosting providers
/// substitute this value straight into a request path, and they escape it differently — Azure DevOps
/// runs <see cref="System.Uri.EscapeDataString(string)"/> over it, GitHub hands it to Octokit
/// unescaped — so a name carrying a separator would reshape the request on one host and not the
/// other. Validating here fixes both at the source.
/// </remarks>
[HasNonWhitespaceContent]
[IsSinglePathSegment]
public sealed record GitRepositoryName : SemanticString<GitRepositoryName> { }

/// <summary>
/// A strongly-typed browser-facing URI for a repository.
/// </summary>
[HasNonWhitespaceContent]
public sealed record GitRepositoryWebURI : SemanticString<GitRepositoryWebURI> { }

/// <summary>
/// A strongly-typed remote path for a repository, which may be an HTTPS URL, an SSH URL, or a
/// local filesystem path.
/// </summary>
[HasNonWhitespaceContent]
[NotAnOption]
public sealed record GitRepositoryRemotePath : SemanticString<GitRepositoryRemotePath> { }
