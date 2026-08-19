// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using ktsu.Semantics.Strings;

/// <summary>
/// A strongly-typed repository name.
/// </summary>
[HasNonWhitespaceContent]
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
public sealed record GitRepositoryRemotePath : SemanticString<GitRepositoryRemotePath> { }
