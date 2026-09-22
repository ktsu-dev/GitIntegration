# Worktree verbs, GitHub owner kinds, and device flow

Status: approved design, not yet implemented.

## Why this phase exists

A desktop launcher is being built against this library. It shows a tree of every repository its user
can reach across Azure DevOps and GitHub, and drives the ordinary git operations from that tree. Its
branch model is one worktree per branch rather than one checkout that switches.

Three things it needs do not exist here:

1. **Worktrees.** The library has no worktree verb at all. `GitProbes.IsWorkTreeAsync` asks whether a
   path is a working tree, and `GitStatusEntry.WorkTreeState` names one half of a status code. Neither
   creates, lists, or removes anything.
2. **GitHub repositories it can actually see.** `GitHubProvider.GetRepositoriesAsync` calls
   `GET /users/{login}/repos`, which returns public repositories only. The launcher's repositories are
   private and live under an organisation behind SAML single sign-on, so today it would enumerate an
   empty list and be right to.
3. **A way to sign in.** Credentials resolve from `ktsu.CredentialCache` or from a `CredentialSource`
   callback. Both assume a credential already exists. Nothing in the library obtains one, and a
   desktop application cannot ask its user to run `gh auth login` first.

Everything here is additive. No existing member changes shape, and no existing behaviour changes for
a caller that does not opt in.

## Versioning

**`[minor]` — 3.2.0.** New types and new members only. The one existing method whose behaviour is
touched, `GitHubProvider.GetRepositoriesAsync`, keeps its current route and its current documented
coverage under the new property's default.

## Scope

In scope:

- `worktree` list, add, remove and prune.
- An owner-kind selector on `GitHubProvider` routing repository enumeration to the endpoint that can
  answer for that kind of owner.
- The single sign-on authorisation URL surfaced on the failure that demands it.
- GitHub's OAuth device flow, as a type that obtains a credential rather than one that stores it.

Out of scope, deliberately: `worktree move`, `worktree lock` and `worktree unlock` (a launcher UI
exercises none of them, and `lock` protects against a failure mode — removable storage going away —
that this caller does not have); device flow for Azure DevOps (it authenticates through Entra ID,
which the consuming application already does for itself, and `CredentialSource` is the seam that was
built for exactly that); and token refresh (a GitHub OAuth App's device-flow token does not expire,
unlike a GitHub App's).

## Worktree verbs

### The model

```csharp
public sealed record GitWorktree
{
    public required AbsoluteDirectoryPath Path { get; init; }
    public GitCommitSha? Head { get; init; }
    public GitBranchName? Branch { get; init; }
    public bool IsMain { get; init; }
    public bool IsBare { get; init; }
    public bool IsDetached { get; init; }
    public bool IsLocked { get; init; }
    public string? LockReason { get; init; }
    public bool IsPrunable { get; init; }
    public string? PrunableReason { get; init; }
}
```

Inert data, like `GitBranch` and `GitSubmodule`. It carries no `GitRepository` and no process runner.
A caller that wants to operate inside a worktree passes its `Path` to `IGitClient.OpenAsync`, which is
the same journey a caller makes from any other path. Handing out a live `GitRepository` from a model
would make this the only model in the library that can execute anything, and would force a decision
about which runner it inherited.

`Head` and `Branch` are nullable because git's own output omits them. A bare entry reports neither. A
detached entry reports `HEAD` and no `branch`. Making either required would mean inventing a value for
a record git declined to describe.

`LockReason` and `PrunableReason` are separately nullable from their flags, because `locked` and
`prunable` each appear both bare and with a reason, and "locked for a reason nobody recorded" is a
different fact from "not locked".

`IsMain` is **positional**. Git's porcelain has no attribute for it: the main working tree is simply
the first record, always. The parser sets it on the first record and on no other. This is recorded
here because it is the one field in the record not read from a named attribute, and because a future
`--porcelain` change that reorders records would break it silently. It exists because a caller
managing worktrees needs to refuse to remove the one that owns the repository, and answering that from
positional knowledge inside the parser is better than every caller reimplementing it.

### Listing

```csharp
public interface IGitWorktreeListBuilder : IGitCommandBuilder<IReadOnlyList<GitWorktree>>;
```

No options. `git worktree list --porcelain` takes none this caller wants, and a builder with no
configuration still earns its place by matching every other read verb's shape rather than being the
one verb that is a bare method.

The porcelain format is blank-line-separated records of `attribute [value]` lines:

```
worktree /home/u/project
HEAD 7f3c9a1...
branch refs/heads/main

worktree /home/u/project-feature
HEAD 2b8e4d0...
branch refs/heads/feature
locked contains uncommitted experiment

worktree /home/u/project-review
HEAD 9c1a7f2...
detached
prunable gitdir file points to non-existent location
```

`branch` arrives fully qualified and is stripped to a bare `refs/heads/` name, the same normalisation
`AzureDevOpsProvider.StripRefsHeadsPrefix` already applies on the hosting side. A caller should not have
to know which half of this library produced a branch name to know its shape.

### Adding

```csharp
public interface IGitWorktreeAddBuilder : IGitCommandBuilder<GitCompleted>
{
    IGitWorktreeAddBuilder CheckingOut(GitBranchName branch);
    IGitWorktreeAddBuilder CreatingBranch(GitBranchName branch);
    IGitWorktreeAddBuilder CreatingOrResettingBranch(GitBranchName branch);
    IGitWorktreeAddBuilder Detached();
    IGitWorktreeAddBuilder From(GitRefName commitish);
    IGitWorktreeAddBuilder Force();
    IGitWorktreeAddBuilder WithoutCheckout();
}
```

`CheckingOut`, `CreatingBranch`, `CreatingOrResettingBranch` and `Detached` are four settings of one
mode field, and a later call replaces an earlier one. This follows `IGitBranchListBuilder`'s
`LocalOnly` and `RemoteOnly`, which already document themselves as replacing any previous selection.
Throwing on a second call would be defensible in isolation but would make this the only builder in the
library where ordering is an error rather than a resolution.

Git's syntax is `git worktree add [-f] [--detach] [-b <new-branch>] <path> [<commit-ish>]`, so the mode
settings divide across two places. `CreatingBranch` emits `-b`, `CreatingOrResettingBranch` emits `-B`,
and `Detached` emits `--detach`, all options. `CheckingOut` emits nothing and instead writes the
commit-ish operand.

`From` writes that **same** operand: the start point for the two branch-creating modes, and the commit
to detach at for `Detached`. So `CheckingOut` and `From` are one field under two names, and the later
call wins, by the same rule that governs the mode field. The two are typed differently because that is
the useful distinction — `CheckingOut` takes a `GitBranchName` and reads as the ordinary case, `From`
takes a `GitRefName` and admits a tag or a raw revision. Neither is a separate slot, and a builder that
called both would be saying the same thing twice.

The path operand is the builder's constructor argument, so it is never absent. Both it and the
commit-ish are passed after `--end-of-options`, as every caller-supplied operand in this library is.

**`--guess-remote` is deliberately omitted.** The launcher's case is a worktree for a branch that
exists on the remote but not locally, and plain `git worktree add <path> <branch>` already handles it:
git creates a local tracking branch when the name matches exactly one remote. `--guess-remote` extends
that to the case where the caller names no branch at all, which no caller here does.

### Removing and pruning

```csharp
public interface IGitWorktreeRemoveBuilder : IGitCommandBuilder<GitCompleted>
{
    IGitWorktreeRemoveBuilder Force();
}

public interface IGitWorktreePruneBuilder : IGitCommandBuilder<GitCompleted>;
```

`git worktree remove` refuses a worktree with modified or untracked files, and `Force()` is how a
caller says it knows. `PruneWorktrees()` clears administrative entries whose directory has gone,
which is the state a launcher reaches whenever a user deletes a worktree folder in a file manager.

`--dry-run` on prune is omitted: it would change the result type from `GitCompleted` to a list of what
would have been removed, and a caller that wants to know can call `Worktrees()` and read `IsPrunable`
from records it already has.

### Why no new exception type

Under one worktree per branch, `git worktree add` failing because the branch is already checked out
somewhere is not an edge case — it is what happens every time a user asks for a branch they already
have. That frequency argues for a typed exception the way `GitPushRejectedException` and
`GitNothingToCommitException` earned theirs.

It does not get one. Those two exceptions exist because the caller cannot know the outcome in advance:
whether a push will be rejected depends on the remote's state at the moment of the push. Whether a
branch already has a worktree is answerable from `Worktrees()`, which a caller managing worktrees has
necessarily already called. The failure is reachable only by racing yourself, and `TryExecuteAsync`
returns a non-throwing `GitCommandError` for that. Adding the exception would mean matching git's
English prose to tell a caller something it could have read structurally.

## GitHub owner kinds

### The property

```csharp
public enum GitHubOwnerKind
{
    User,
    Organization,
    AuthenticatedUser,
}
```

```csharp
public GitHubOwnerKind OwnerKind { get; init; } = GitHubOwnerKind.User;
```

`User` is the default so that a caller who says nothing gets exactly today's route, today's coverage,
and today's documented contract. This phase adds a way to ask for more, and changes nothing for anyone
who does not.

### Routing

| `OwnerKind` | Octokit call | Endpoint | Sees private |
|---|---|---|---|
| `User` | `Repository.GetAllForUser(Owner)` | `GET /users/{login}/repos` | no |
| `Organization` | `Repository.GetAllForOrg(Owner)` | `GET /orgs/{org}/repos` | yes, where the token can |
| `AuthenticatedUser` | `Repository.GetAllForCurrent(...)` | `GET /user/repos` | yes, where the token can |

`AuthenticatedUser` requests `affiliation=owner,organization_member` and then **filters the result to
`Owner`** client-side, comparing each repository's owner login to `Owner` case-insensitively. GitHub
treats logins as case-insensitive, so an ordinal comparison would drop a caller's repositories over a
capital letter the caller did not choose.

The filter is what keeps `Owner` meaningful. `GET /user/repos` describes the token's own reachable
repositories and takes no owner parameter, so routing to it unfiltered would silently ignore a
configured `Owner` — which is the precise objection recorded in `GetRepositoriesAsync`'s current
remarks against switching to that endpoint wholesale. Filtering answers it: the endpoint widens what
can be seen, and the filter preserves the invariant that this method describes `Owner`'s repositories
and nobody else's.

Filtering rather than validating the token's login against `Owner` is a deliberate choice between two
ways of honouring it. Validation costs an extra `GET /user` on every enumeration and rejects the
legitimate case of an organisation the token is a member of. The filter costs nothing and handles both.

### The single sign-on failure

A token that is valid but not authorised for an organisation's SAML single sign-on receives `403` with
an `X-GitHub-SSO` header whose value carries the URL the user must visit to authorise it.

`Translate` already routes `403` without rate-limit headers to `GitHostingAuthenticationException`,
which is the correct bucket and does not change. What changes is the message: when that header is
present, its URL is included.

Without it, the two failures a caller most needs to tell apart — a bad token, and a good token one
click away from working — are the same exception with the same text, and the fix is a URL the user
cannot see. The header is read case-insensitively by scanning, matching how `TryGetRetryAfterSeconds`
already reads `Retry-After`, and for the same reason.

## Device flow

### Shape

```csharp
public sealed record GitHubDeviceCode
{
    public required string UserCode { get; init; }
    public required string DeviceCode { get; init; }
    public required Uri VerificationUri { get; init; }
    public required TimeSpan ExpiresIn { get; init; }
    public required TimeSpan Interval { get; init; }
}

public sealed class GitHubDeviceFlow(GitHubOAuthClientId clientId, IReadOnlyList<string> scopes)
{
    public Task<GitHubDeviceCode> RequestDeviceCodeAsync(CancellationToken cancellationToken = default);
    public Task<HostingCredential> WaitForTokenAsync(GitHubDeviceCode code, CancellationToken cancellationToken = default);
}
```

`DeviceCode` is the opaque code the polling request carries, distinct from `UserCode`, which is the
short string a human types at `VerificationUri`. Showing one code where the other belongs fails in a
way that looks like a broken sign-in.

### Why two calls rather than one

Device flow has an inherent pause in the middle: GitHub issues a short user code, the user types it
into a browser, and only then does polling succeed. That pause is minutes long and the code must stay
on screen throughout.

A single `AuthenticateAsync(onCodeIssued)` would invoke its callback from whatever thread the HTTP
continuation resumed on, leaving every graphical caller to marshal the code back to a UI thread from
inside a callback it does not control. Two calls invert it: the caller awaits the first, renders the
code however it likes, and awaits the second wherever it wants the wait to happen. The seam lands where
the pause already is.

`Interval` and `ExpiresIn` are surfaced rather than kept private because a caller showing a countdown
or a "still waiting" hint needs both, and neither is derivable.

### Why it is not on GitHubProvider

Every other method on `GitHubProvider` is one request and one response. This is a two-stage exchange
that blocks for minutes and is driven by a human in a browser. Putting it there would give the type two
unrelated lifecycles and would mean a provider instance existed before it had anything to authenticate
with.

Keeping it separate also keeps the dependency direction clean: the flow produces a `HostingCredential`,
and `GitProvider` already consumes one through `CredentialSource` and the credential cache. Nothing new
connects them.

### Why it does not store anything

`WaitForTokenAsync` returns a `HostingCredential` and writes nothing. The caller persists it:

```csharp
HostingCredential credential = await flow.WaitForTokenAsync(code, cancellationToken);
CredentialCache.Instance.AddOrReplace(persona, new CredentialWithToken { Token = credential.Token!.As<CredentialToken>() });
```

Two lines at the call site, in exchange for a type that can be tested without a keyring and that does
not decide on its caller's behalf where a secret lives. A caller with its own secret storage is not
forced through `ktsu.CredentialCache` to use device flow.

The credential is built with `HostingCredential.FromToken`, not `FromBearerToken`. A GitHub OAuth token
travels under Octokit's `Token` scheme, which is what `FromToken` documents itself as meaning.
`FromBearerToken` exists for Entra ID access tokens against Azure DevOps.

### Transport and polling

Built on a raw `HttpClient` rather than Octokit's `OauthClient`, even though Octokit exposes the same
pair of calls. Two problems turned up empirically against the real package: `InitiateDeviceFlow`
percent-encodes the scope's colon, so `read:org` travels as `read%3Aorg`, and
`CreateAccessTokenForDeviceFlow` throws `Octokit.ApiException` for an OAuth `error` body instead of
returning it, collapsing a refused authorisation and an expired code into the same exception shape a
transport failure gets.

`GitHubDeviceFlow` instead posts JSON directly to `https://github.com/login/device/code` and
`https://github.com/login/oauth/access_token`, reading the `error` field itself so a refusal and an
expiry are told apart, and owns the poll loop: it waits `Interval` between attempts, widening by five
seconds on each `slow_down`.

### Failures

No new exception family. Everything lands in the hosting hierarchy that already exists:

| Condition | Exception |
|---|---|
| `access_denied` (user refused) | `GitHostingAuthenticationException` |
| `expired_token` (code timed out) | `GitHostingAuthenticationException` |
| `incorrect_client_credentials`, `unsupported_grant_type` | `GitHostingRequestException` |
| transport failure, unparsable body | `GitHostingRequestException` |

Denial and expiry share an exception and are distinguished by message. They are the same fact to a
caller — no credential was obtained, offer to start again — and splitting them would add a type nobody
switches on.

### The client identifier

`GitHubOAuthClientId`, a new semantic string in `SemanticTypes/GitProviderTypes.cs`, supplied by the
caller. The library embeds no identifier and ships no default.

An OAuth App's client ID is not a secret: device flow has no client secret precisely because a desktop
binary cannot keep one. So a consuming application can hold it in ordinary configuration. It is a
parameter here rather than a constant because the identifier belongs to whoever registered the app, and
this library is not that party.

### The external prerequisite

**Nothing in this section can be exercised against GitHub until an OAuth App exists.** Someone with
organisation ownership must register one with device flow enabled and approve it for SAML single
sign-on. Scopes: `repo` and `read:org`.

Until then the implementation is written and tested entirely against a fake transport, which is how the
rest of the hosting layer is tested anyway. No implementation step is blocked. Only a live end-to-end
confirmation is, and that is recorded as an unchecked item rather than a passing test.

## What this does not do for the launcher

Recorded so the next design does not assume otherwise.

**GitHub sign-in is optional in the consuming launcher.** A user who never signs in must still get a
working tree of their Azure DevOps repositories. This needs no change here:
`GitProvider.IsAuthenticated` already reports whether a request would carry a credential, and already
distinguishes that from whether a credential store holds an entry. A caller checks it and skips
enumeration rather than calling and handling a `403`.

**A worktree layout convention is the caller's.** `AddWorktree` takes any absolute path. The launcher
will enforce one worktree per branch at derived, predictable paths; that policy lives there, and this
library neither imposes nor validates it.

## Testing

Three existing patterns in `GitIntegration.Test`, no new ones.

**Parser tests** over fixture strings, as `GitStatusParserTests` does. The cases that matter are the
ones where porcelain omits fields: a bare entry with no `HEAD` and no `branch`; a detached entry with
`HEAD` and no `branch`; `locked` and `prunable` each with and without a reason; a single-record listing
where the only worktree is also the main one; a multi-record listing asserting `IsMain` on the first and
nowhere else; and trailing blank lines, which `--porcelain` emits and a naive record split turns into a
phantom entry.

**Builder tests** asserting the exact argument vector through a fake `IGitProcessRunner`. One per option,
plus three proving the mode field resolves to the last call rather than accumulating `-b` alongside
`--detach`.

**Provider tests** injecting a fake `HttpMessageHandler` through the internal `Handler` seam. One per
`GitHubOwnerKind` asserting the route actually requested; one proving `AuthenticatedUser` drops
repositories belonging to another owner; one asserting a `403` carrying `X-GitHub-SSO` produces a
`GitHostingAuthenticationException` whose message contains the header's URL; and one asserting a `403`
without that header still produces the same exception type, so the addition does not become a
requirement.

**Device flow tests** against a fake transport: a poll that returns `authorization_pending` before
succeeding, a `slow_down` that widens the interval, an expiry, and a denial.

## Implementation order

1. Verify Octokit 14.0.0's `OauthClient` device-flow surface. It is the only unknown, and it decides
   whether the device flow section is a translation layer or two HTTP posts.
2. Worktree parser and model. Independent of everything else, and the largest body of parsing.
3. Worktree builders and the four `GitRepository` factories.
4. `GitHubOwnerKind` and the enumeration routing.
5. The `X-GitHub-SSO` message.
6. `GitHubOAuthClientId`, `GitHubDeviceCode`, and `GitHubDeviceFlow`.
7. README and CHANGELOG.

Steps 2 and 3 have no dependency on 4 through 6 and could be built in either order, or concurrently.
