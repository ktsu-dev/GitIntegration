// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using ktsu.Semantics.Strings;

/// <summary>
/// A strongly-typed git commit message.
/// </summary>
[HasNonWhitespaceContent]
public sealed record GitCommitMessage : SemanticString<GitCommitMessage> { }

/// <summary>
/// A strongly-typed name recorded in a commit's author or committer signature.
/// </summary>
[HasNonWhitespaceContent]
public sealed record GitAuthorName : SemanticString<GitAuthorName> { }

/// <summary>
/// A strongly-typed email address recorded in a commit's author or committer signature.
/// </summary>
/// <remarks>
/// This deliberately does not validate as an email address. Git records whatever
/// <c>user.email</c> is set to, including values that are not valid addresses, and a commit that
/// already exists in history must remain readable.
/// </remarks>
[HasNonWhitespaceContent]
public sealed record GitAuthorEmail : SemanticString<GitAuthorEmail> { }
