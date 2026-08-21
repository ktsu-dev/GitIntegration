// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;
using System.Text.Json.Serialization;

/// <summary>
/// The envelope Azure DevOps wraps a repository list in.
/// </summary>
/// <remarks>
/// Shape confirmed against the Repositories - List endpoint's <c>api-version=7.1</c> sample
/// response: <c>{ "count": &lt;int&gt;, "value": [ GitRepository, ... ] }</c>. See
/// <c>docs/superpowers/research/2026-08-21-azure-devops-rest-findings.md</c>, section 2.
/// </remarks>
internal sealed class AzureDevOpsRepositoryListResponse
{
	/// <summary>Gets the number of repositories in <see cref="Value"/>.</summary>
	public int Count { get; init; }

	/// <summary>Gets the repositories the request returned.</summary>
	public IReadOnlyList<AzureDevOpsRepository> Value { get; init; } = [];
}

/// <summary>
/// One repository, as Azure DevOps's <c>GitRepository</c> schema reports it.
/// </summary>
/// <remarks>
/// Only the fields <see cref="AzureDevOpsProvider"/> maps onto <see cref="GitRepository"/> are
/// declared here; the rest of the documented schema (<c>id</c>, <c>sshUrl</c>,
/// <c>defaultBranch</c>, <c>project</c>, and so on) is left unread because nothing in this
/// library's model needs it. Field names come from findings section 2. <see cref="WebUrl"/> is
/// documented-by-schema-only there: no example response the findings could check populates it, so
/// treat a populated value as unconfirmed shape even though the field name itself is confirmed.
/// </remarks>
internal sealed class AzureDevOpsRepository
{
	/// <summary>Gets the repository's name, or <see langword="null"/> when the host omitted it.</summary>
	public string? Name { get; init; }

	/// <summary>
	/// Gets the repository's browser-facing URL, or <see langword="null"/> when the host did not
	/// report one.
	/// </summary>
	public string? WebUrl { get; init; }

	/// <summary>Gets the repository's clone URL, or <see langword="null"/> when the host omitted it.</summary>
	public string? RemoteUrl { get; init; }
}

/// <summary>
/// The error body shape Azure DevOps returns for a failed request.
/// </summary>
/// <remarks>
/// Field name confirmed against the published <c>WrappedException</c> interface (findings section
/// 6); only <see cref="Message"/> is read, since that is the only piece of the error body this
/// library's exception hierarchy carries as a message.
/// </remarks>
internal sealed class AzureDevOpsErrorResponse
{
	/// <summary>Gets the human-readable failure message, or <see langword="null"/> when absent.</summary>
	public string? Message { get; init; }
}

/// <summary>
/// Source-generated serialization metadata for the Azure DevOps DTOs.
/// </summary>
/// <remarks>
/// Source generation rather than reflection-based <c>JsonSerializer</c> calls: the reflection path
/// warns under this library's trimming analyzers, and warnings are errors here. Azure DevOps's
/// JSON uses camelCase field names throughout every fixture and documentation example the findings
/// checked, hence the camelCase naming policy applied to every type this context serializes.
/// </remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AzureDevOpsRepositoryListResponse))]
[JsonSerializable(typeof(AzureDevOpsErrorResponse))]
internal sealed partial class AzureDevOpsJsonContext : JsonSerializerContext
{
}
