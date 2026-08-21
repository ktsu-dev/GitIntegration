# GitIntegration Phase 5b — the hosting layer

Status: approved design, not yet implemented.
Supersedes: the **Hosting layer** section of `2026-08-19-gitintegration-v2-design.md` (see
[Relationship to the v2 spec](#relationship-to-the-v2-spec)).

## Why this phase exists

The hosting layer that ships today is a stub. `GitHubProvider.RefreshRemoteRepositories()` sets
credentials on an Octokit client and returns — it refreshes nothing. `ConcurrentBag<GitRepository>
Repositories` is never populated by anyone. Nothing in the library consumes either member, neither
is registered in `AddGitIntegration`, and the whole layer has no tests.

The abstract method is also `void` and synchronous while any real implementation must perform
network I/O, so as written neither Octokit nor a REST client can implement it honestly.

This phase makes the layer real, and uses two providers to prove the abstraction generalises rather
than merely accommodating whichever client happened to be first.

## Scope

In scope:

- Repository enumeration for GitHub and Azure DevOps.
- Pull request **listing** and **creation** for both.
- A testable transport seam, and the tests it enables.

Out of scope, deliberately: PR merge and completion (the two hosts diverge most there — Azure DevOps
completes with completion options, GitHub merges with a merge method — and the blast radius is
largest), PR comments and reviews, repository creation, and webhooks.

## Versioning

This removes two public members, which normally forces a major bump. It ships as **`[minor]` —
2.4.0** by explicit decision: the members never worked, so no consumer can depend on their
behaviour, and the removal produces a compile error rather than a silent behavioural change. Burning
a major version on withdrawing a no-op is not worth the signal it sends.

## The abstraction

```csharp
public interface IGitHostingProvider
{
    GitProviderName Name { get; }
    GitProviderOwner Owner { get; }
    PersonaGUID PersonaGUID { get; }
    bool IsAuthenticated { get; }

    Task<IReadOnlyList<GitRepository>> GetRepositoriesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<GitPullRequest>> GetPullRequestsAsync(GitRepositoryName repo, CancellationToken ct = default);

    IGitPullRequestCreateBuilder CreatePullRequest(GitRepositoryName repo);
}

public abstract class GitProvider : IGitHostingProvider { … }
public sealed class GitHubProvider : GitProvider { … }        // Octokit
public sealed class AzureDevOpsProvider : GitProvider { … }   // raw HttpClient
```

### Why two idioms

The reads take no options, so a builder would be ceremony. Creating a pull request has two required
inputs and three optional ones, which is where a builder earns its keep.

This is not a new rule: `GitClient` already exposes plain async methods (`GetVersionAsync`,
`DiscoverAsync`) while everything with options gets a builder. A caller moving between layers meets
the same rule rather than a second convention.

### Removed

- `RefreshRemoteRepositories()` — replaced by `GetRepositoriesAsync`.
- `ConcurrentBag<GitRepository> Repositories` — replaced by the returned list. The bag was unordered,
  never cleared between refreshes, and would have accumulated duplicates had anything populated it.

Neither is kept as an `[Obsolete]` shim. A shim would preserve a member that does nothing, inviting
a consumer to newly believe in it.

### Which pull requests `GetPullRequestsAsync` returns

**Open pull requests only, requested explicitly on both hosts.**

Both hosts happen to default to open/active when unfiltered, but relying on that would make this
library's contract a restatement of two vendors' defaults, either of which could change without
notice. The filter is sent explicitly so the result is defined here.

Listing merged or abandoned pull requests is a filtering feature; it is out of scope, and adding
options later is additive.

### The create builder

```csharp
public interface IGitPullRequestCreateBuilder
{
    IGitPullRequestCreateBuilder From(GitBranchName source);
    IGitPullRequestCreateBuilder Into(GitBranchName target);
    IGitPullRequestCreateBuilder Titled(GitPullRequestTitle title);
    IGitPullRequestCreateBuilder Describing(string description);
    IGitPullRequestCreateBuilder AsDraft();

    Task<GitPullRequest> ExecuteAsync(CancellationToken ct = default);
}
```

`From`, `Into`, and `Titled` are required; `Describing` and `AsDraft` are optional. A missing
required value throws `InvalidOperationException` from `ExecuteAsync` rather than from the setter,
because a caller may legally supply the parts in any order and only the finished configuration knows
whether it is complete — the same reasoning, and the same documentation requirement, as
`GitPullBuilder`. The exception is documented on every member that can leave the builder incomplete.

Like the local layer's builders, an instance is single-use and not thread-safe.

### New semantic types

`GitPullRequestNumber`, `GitPullRequestTitle`, `GitPullRequestAuthor`, and `GitPullRequestWebURI`
join the existing types in `SemanticTypes/`, each with the validation attribute its shape warrants.
`GitPullRequestAuthor` carries the host's user identifier — GitHub's `login`, Azure DevOps's
`uniqueName` — and the difference is documented on the type rather than smoothed over.

### `AzureDevOpsProvider.Project`

```csharp
public AzureDevOpsProjectName? Project { get; init; }   // null enumerates all projects
```

Azure DevOps nests repositories under a project; GitHub has no equivalent level. The type already
exists in `SemanticTypes/GitProviderTypes.cs` with nothing behind it.

## Credentials

`ktsu.CredentialCache` resolves a credential by `PersonaGUID`. Handling, in full:

| Credential | Behaviour |
|---|---|
| `CredentialWithToken` | Used. GitHub: token auth. Azure DevOps: Basic, empty username, token as password. |
| `CredentialWithUsernamePassword` | Used as supplied. |
| `CredentialWithNothing`, or none resolved | Proceed **unauthenticated**, documented. |
| Any other subtype | Throw. |

Today's `GitHubProvider` recognises only `CredentialWithUsernamePassword` and silently does nothing
for anything else, so a `CredentialWithToken` — the natural fit for a PAT on both hosts — is ignored
without a word. That is the bug this table fixes.

Proceeding unauthenticated is deliberate and is not the same bug: enumerating public repositories
without credentials is legitimate, and refusing it would break a real use. An *unrecognised* subtype
is different — it means the caller configured something we do not understand, and continuing as
though nothing were configured would hide that.

## Models

```csharp
public sealed record GitPullRequest
{
    public required GitPullRequestNumber Number { get; init; }
    public required GitPullRequestTitle Title { get; init; }
    public string? Description { get; init; }
    public required GitBranchName SourceBranch { get; init; }
    public required GitBranchName TargetBranch { get; init; }
    public GitPullRequestAuthor? Author { get; init; }
    public required GitPullRequestState State { get; init; }
    public bool IsDraft { get; init; }
    public GitPullRequestWebURI? WebURI { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
}

public enum GitPullRequestState { Open, Merged, Closed }
```

Only fields both hosts genuinely have. Divergent concepts — Azure DevOps's −10..+10 reviewer vote
scale, GitHub's approve/request-changes reviews, work item links, completion options — are not
exposed. Adding a field later is additive; removing one is breaking.

Nullability follows the discipline already established in this library: **null means "not known",
which is distinct from "known to be empty"** — the same reasoning behind `GitFetchResult
.DetailAvailable`.

### State mapping

The one place a faithful-looking mapping could quietly lie, so it is written down:

| Host | Source | `GitPullRequestState` |
|---|---|---|
| GitHub | `state: open` | `Open` |
| GitHub | `state: closed`, `merged: true` | `Merged` |
| GitHub | `state: closed`, `merged: false` | `Closed` |
| Azure DevOps | `status: active` | `Open` |
| Azure DevOps | `status: completed` | `Merged` |
| Azure DevOps | `status: abandoned` | `Closed` |

### Two normalisations

- **Branch refs.** Azure DevOps returns `refs/heads/main`; GitHub returns `main`. Both normalise to a
  bare `GitBranchName`, so callers never branch on host.
- **Web URI.** Azure DevOps's `url` field is the **API** URL, not the browser one; the web link is at
  `_links.web.href`. Populating `WebURI` from `url` would put something plausible and wrong in a
  public field. When the web link is absent, `WebURI` stays null rather than being guessed.

## Errors

```
GitHostingException
├─ GitHostingAuthenticationException   401, and 403 without rate-limit headers
├─ GitHostingNotFoundException         404
├─ GitHostingRateLimitException        429, or 403 with rate-limit headers; carries reset time
└─ GitHostingRequestException          any other non-success status
```

Each carries the provider name, the HTTP status, and the response body — mirroring how
`GitCommandException` carries the argument vector, so a failure can be reproduced by hand.

`GitHostingException` deliberately does **not** derive from `GitException`. Every subtype of
`GitException` carries process concepts — exit code, argument vector — that an HTTP failure does not
have, and forcing the inheritance would put meaningless members on both sides of the tree.

## Transport and testability

Providers take an `HttpMessageHandler` through an **`internal`** init property, defaulting to a real
one. The test project already has `InternalsVisibleTo`, so tests inject a fake without transport
types appearing anywhere in the public API. Octokit receives the same handler via its
`HttpClientAdapter` (verified present in Octokit 14.0.0), so **one seam covers both providers**.

This mirrors `IGitProcessRunner` — one interface the library owns, one real implementation, fakes for
tests — which is the pattern the local layer already proved across five phases. It needs no new
package. DI registration can be layered on later without breaking anything.

Rejected alternatives: `Microsoft.Extensions.Http` with `AddHttpClient<T>()` adds a package to a
library carrying only DI *Abstractions* today and fits awkwardly with providers built from `required`
init properties; a full DI-first redesign moving `Owner` and `PersonaGUID` into options rewrites the
construction model for ergonomics this phase does not need.

### The fake

`FakeHttpMessageHandler` scripts responses **and** records complete requests — both capabilities from
the start, not one added later.

This is a direct lesson from Phase 5a: `ScriptedGitProcessRunner` recorded only argument vectors, so
a test asserting progress-sink forwarding could observe nothing and passed while asserting nothing at
all. A fake that cannot see what the code under test did produces tests that read as coverage and are
not.

### What the tests assert

- Request URI, HTTP method, and authorization header.
- Request body JSON for pull request creation.
- Parsing of canned responses into models, asserting on the parsed **fields** — not on counts.
- Status-to-exception mapping across the hierarchy above.

**Fixtures are captured from real responses, redacted — never hand-written.** Phase 5a shipped three
tests that passed for the wrong reason, and hand-built fixtures caused all three.

### The limitation, stated plainly

There is no integration tier for this layer. Hitting real GitHub and Azure DevOps requires
credentials and network access that CI cannot have. These tests therefore verify the client against
*our understanding* of each API, not against the APIs themselves. Captured fixtures narrow that gap;
they do not close it. Handler-level tests here must not be read as end-to-end assurance.

## Research the plan must do first

Exact `api-version` values, JSON field names, and pagination semantics for the Azure DevOps REST API
are **not settled by this document** and must not be written from memory. Building a client against
assumed field names produces one that works against its author's beliefs rather than the service.

The plan's first task verifies against Microsoft's published REST reference and captures real
response shapes for:

- `GET /_apis/git/repositories` — org-wide and project-scoped.
- `GET /{project}/_apis/git/repositories/{repo}/pullrequests`.
- `POST /{project}/_apis/git/repositories/{repo}/pullrequests`.
- Pagination: `$top`/`$skip` versus continuation-token headers, and which endpoints use which.
- The error body shape, so the exception hierarchy carries something useful.

## Design decision: pull requests require a project on Azure DevOps

PR operations need a project, but enumeration with `Project` null spans all projects and
`GitRepository` carries no project field.

**PR operations throw `InvalidOperationException` when `Project` is null**, with a message naming
what to set. Enumeration continues to work project-less.

Resolving the project by re-enumerating and matching names costs an extra call and is ambiguous when
two projects hold a repository of the same name. Adding a project field to `GitRepository` would push
a host-specific concept into a type the local layer owns.

This follows the precedent set by `GitPullBuilder`: reject an invalid *combination* at the point the
full configuration is known, and document the exception on the members that can create the invalid
state.

## Relationship to the v2 spec

`2026-08-19-gitintegration-v2-design.md` anticipated most of this — it already diagnosed
`RefreshRemoteRepositories()` as a synchronous no-op and proposed replacing it and the bag.

Its `AzureDevOpsProvider` section is **superseded**. That section prescribes `VssConnection` and
`GitHttpClient` from `Microsoft.TeamFoundationServer.Client`, following the pattern in
`ktsu.BuildMonitor`. That package was rejected by measurement: version 19.225.1 pulls 21 transitive
packages including `System.Data.SqlClient` 4.8.5, which NuGet flags as NU1903 for the high-severity
advisory GHSA-98g6-xh36-x2p7, plus native SQL Server SNI libraries for win-x64, x86, and arm64.
Pinning to 4.9.0 clears the advisory but not the package count or the native libraries. The REST
approach needs no package at all.

The v2 spec's `IGitHostingProvider` also carried only `GetRepositoriesAsync`; pull request listing
and creation are added here.
