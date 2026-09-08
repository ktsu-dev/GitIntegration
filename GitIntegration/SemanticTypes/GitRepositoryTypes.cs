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

/// <summary>
/// A strongly-typed identifier a host assigns to a repository, as distinct from its human-facing
/// name.
/// </summary>
/// <remarks>
/// A string rather than a <see cref="System.Guid"/>, because the two hosts do not agree on its
/// shape: Azure DevOps types its <c>{repositoryId}</c> path parameter as <c>string (uuid)</c>,
/// while GitHub's repository id is a decimal number. The value is opaque to this library — it is
/// obtained from a host and handed back to that same host, never parsed — so a type that insisted
/// on one host's shape would simply exclude the other.
/// <para>
/// Validated as a single path segment for the same reason <see cref="GitRepositoryName"/> is: it is
/// substituted directly into a request path.
/// </para>
/// </remarks>
[HasNonWhitespaceContent]
[IsSinglePathSegment]
public sealed record GitHostRepositoryId : SemanticString<GitHostRepositoryId> { }
