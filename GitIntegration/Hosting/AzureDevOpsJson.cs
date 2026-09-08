// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;
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
/// The envelope Azure DevOps wraps a pull request list in.
/// </summary>
/// <remarks>
/// Shape confirmed against the Get Pull Requests endpoint's three sample responses:
/// <c>{ "value": [ GitPullRequest, ... ], "count": &lt;int&gt; }</c>. See findings section 3.
/// </remarks>
internal sealed class AzureDevOpsPullRequestListResponse
{
	/// <summary>Gets the number of pull requests in <see cref="Value"/>.</summary>
	public int Count { get; init; }

	/// <summary>Gets the pull requests the request returned.</summary>
	public IReadOnlyList<AzureDevOpsPullRequest> Value { get; init; } = [];
}

/// <summary>
/// One pull request, as Azure DevOps's <c>GitPullRequest</c> schema reports it.
/// </summary>
/// <remarks>
/// Only the fields <see cref="AzureDevOpsProvider"/> maps onto <see cref="GitPullRequest"/> are
/// declared here. Field names come from findings section 3 (list) and section 4 (create — the same
/// schema, confirmed identical at both endpoints). <see cref="IsDraft"/> and <see cref="Links"/> are
/// documented-by-schema-only for the list endpoint: no sample response the findings could check
/// populates <c>isDraft</c>, and none of the list samples populate <c>_links</c> at all. The create
/// endpoint's own sample response is the only one that populates <see cref="Links"/>, and even there
/// carries no <c>web</c> key — see <see cref="AzureDevOpsReferenceLinks.Web"/>.
/// </remarks>
internal sealed class AzureDevOpsPullRequest
{
	/// <summary>Gets the pull request's host-assigned number.</summary>
	public int PullRequestId { get; init; }

	/// <summary>Gets the pull request's title, or <see langword="null"/> when the host omitted it.</summary>
	public string? Title { get; init; }

	/// <summary>Gets the pull request's description, or <see langword="null"/> when the host omitted it.</summary>
	public string? Description { get; init; }

	/// <summary>
	/// Gets the fully-qualified source ref (e.g. <c>refs/heads/my-branch</c>), or <see langword="null"/>
	/// when the host omitted it.
	/// </summary>
	public string? SourceRefName { get; init; }

	/// <summary>
	/// Gets the fully-qualified target ref (e.g. <c>refs/heads/main</c>), or <see langword="null"/>
	/// when the host omitted it.
	/// </summary>
	public string? TargetRefName { get; init; }

	/// <summary>Gets the identity that created the pull request, or <see langword="null"/> when the host omitted it.</summary>
	public AzureDevOpsIdentityRef? CreatedBy { get; init; }

	/// <summary>Gets the pull request's status (<c>active</c>, <c>completed</c>, or <c>abandoned</c>).</summary>
	public string? Status { get; init; }

	/// <summary>Gets a value indicating whether the pull request is a draft.</summary>
	public bool IsDraft { get; init; }

	/// <summary>Gets when the pull request was created, or <see langword="null"/> when the host omitted it.</summary>
	public DateTimeOffset? CreationDate { get; init; }

	/// <summary>
	/// Gets the pull request's related links, or <see langword="null"/> when the host omitted them.
	/// </summary>
	/// <remarks>
	/// JSON key is <c>_links</c>, which the naming policy this context applies would otherwise render
	/// as <c>links</c> — the explicit <see cref="JsonPropertyNameAttribute"/> is required, not
	/// decorative.
	/// </remarks>
	[JsonPropertyName("_links")]
	public AzureDevOpsReferenceLinks? Links { get; init; }
}

/// <summary>
/// Azure DevOps's <c>IdentityRef</c> shape, as it appears on a pull request's <c>createdBy</c>.
/// </summary>
/// <remarks>Field names from findings section 3.</remarks>
internal sealed class AzureDevOpsIdentityRef
{
	/// <summary>Gets the identity's display name, or <see langword="null"/> when the host omitted it.</summary>
	public string? DisplayName { get; init; }

	/// <summary>
	/// Gets the identity's unique name (usually an email address), or <see langword="null"/> when the
	/// host omitted it.
	/// </summary>
	public string? UniqueName { get; init; }
}

/// <summary>
/// Azure DevOps's <c>ReferenceLinks</c> shape, as it appears on a pull request's <c>_links</c>.
/// </summary>
/// <remarks>
/// Only <see cref="Web"/> is declared. Findings section "Contradictions and gaps" entry 1 settles
/// that no official Microsoft source — the REST schema, any sample response at either API version,
/// or the official Node SDK's type definitions — documents or demonstrates a <c>web</c> key on a
/// pull request's <c>_links</c>; every real example found instead carries <c>self</c>,
/// <c>repository</c>, <c>workItems</c>, <c>sourceBranch</c>, <c>targetBranch</c>,
/// <c>sourceCommit</c>, <c>targetCommit</c>, <c>createdBy</c>, and <c>iterations</c>. Those keys are
/// left undeclared here because nothing in this library's model reads them — <see cref="Web"/> is
/// read when present and stays <see langword="null"/> otherwise, never composed from a constructed
/// URL.
/// </remarks>
internal sealed class AzureDevOpsReferenceLinks
{
	/// <summary>
	/// Gets the pull request's browser-facing link, or <see langword="null"/> when the host did not
	/// report one.
	/// </summary>
	public AzureDevOpsLink? Web { get; init; }
}

/// <summary>One entry in a <c>ReferenceLinks</c> map: a single <c>href</c>.</summary>
internal sealed class AzureDevOpsLink
{
	/// <summary>Gets the link's target, or <see langword="null"/> when the host omitted it.</summary>
	public string? Href { get; init; }
}

/// <summary>
/// The request body <c>POST .../pullrequests</c> accepts, as documented in findings section 4.
/// </summary>
/// <remarks>
/// <see cref="IsDraft"/> is documented in the request body schema table but does not appear in the
/// page's own sample request — schema-confirmed as a settable field, not example-confirmed. It is
/// still sent, since <see cref="GitPullRequestSpecification.IsDraft"/> is part of this library's own
/// contract regardless of whether the documentation's worked example happens to exercise it.
/// </remarks>
internal sealed class AzureDevOpsPullRequestCreateRequest
{
	/// <summary>Gets the fully-qualified source ref (e.g. <c>refs/heads/my-branch</c>).</summary>
	public required string SourceRefName { get; init; }

	/// <summary>Gets the fully-qualified target ref (e.g. <c>refs/heads/main</c>).</summary>
	public required string TargetRefName { get; init; }

	/// <summary>Gets the pull request's title.</summary>
	public required string Title { get; init; }

	/// <summary>Gets the pull request's description, or <see langword="null"/> to omit one.</summary>
	/// <remarks>
	/// Omitted from the payload entirely when unset, rather than sent as <c>"description": null</c>.
	/// Microsoft's own documented sample request simply leaves the field out when there is no
	/// description, and this attribute is what makes "or <see langword="null"/> to omit one" literally
	/// true of the bytes on the wire rather than merely of this property's meaning. Applied here
	/// rather than as a context-wide default: the response DTOs deserialize rather than serialize, so
	/// a global setting would say nothing about them while quietly changing how any future request
	/// type behaves.
	/// </remarks>
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Description { get; init; }

	/// <summary>Gets a value indicating whether the pull request should be created as a draft.</summary>
	public bool IsDraft { get; init; }
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
[JsonSerializable(typeof(AzureDevOpsPullRequestListResponse))]
[JsonSerializable(typeof(AzureDevOpsPullRequest))]
[JsonSerializable(typeof(AzureDevOpsPullRequestCreateRequest))]
internal sealed partial class AzureDevOpsJsonContext : JsonSerializerContext
{
}
