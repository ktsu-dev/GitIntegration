# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
# Restore, build, and test (standard workflow)
dotnet restore
dotnet build
dotnet test

# Run a single test
dotnet test --filter "FullyQualifiedName~TestName"

# Build specific configuration
dotnet build -c Release
```

**Do not add `--nologo` to `dotnet test`.** The test project uses Microsoft Testing Platform (via
`MSTest.Sdk`), and under that platform `dotnet test --nologo` silently runs zero tests and exits
with code 5 instead of failing loudly. Always invoke plain `dotnet test`.

## Project Structure

This is a .NET library (`ktsu.GitIntegration`) with two layers: a local layer that wraps the `git`
executable found on `PATH`, and a hosting layer that talks to remote Git providers (GitHub, via
Octokit, and Azure DevOps, over a raw `HttpClient`). The solution uses:

- **ktsu.Sdk** — custom SDK providing shared build configuration
- **MSTest.Sdk** — test project SDK with Microsoft Testing Platform
- `GitIntegration` targets `net10.0;net9.0`; `GitIntegration.Test` targets `net10.0` only

### Key Files

- `GitIntegration/IGitClient.cs`, `GitIntegration/GitClient.cs` — entry point to the local layer:
  `GetVersionAsync`, `IsRepositoryAsync`, `OpenAsync`, `DiscoverAsync`, plus the repository-creating
  `Init(AbsoluteDirectoryPath)` and the two `Clone(...)` overloads.
- `GitIntegration/GitRepository.cs` — carries `LocalPath` plus optional hosting metadata, and
  exposes one builder factory per read-only verb (`Status()`, `Log()`, `Diff()`, `Branches()`,
  `Remotes()`, `RevParse(...)`) and per mutating verb (`Add()`, `Commit(...)`, `CreateBranch(...)`,
  `DeleteBranch(...)`, `Checkout(...)`, `AddRemote(...)`, `RemoveRemote(...)`,
  `SetRemoteUrl(...)`, `Fetch()`, `Pull()`, `Push()`), plus `IsClonedAsync` and `OpenWebClient`.
- `GitIntegration/Builders/` — public verb-builder interfaces and their internal implementations:
  `GitInitBuilder`, `GitCloneBuilder`, `GitAddBuilder`, `GitCommitBuilder`,
  `GitBranchCreateBuilder`, `GitBranchDeleteBuilder`, `GitCheckoutBuilder`, `GitRemoteAddBuilder`,
  `GitRemoteRemoveBuilder`, `GitRemoteSetUrlBuilder`, `GitFetchBuilder`, `GitPullBuilder`, and
  `GitPushBuilder`. `IGitVersionBuilder` and every concrete `Git*Builder` class are `internal` —
  not part of the public API surface. Only the `IGit*Builder` interfaces are public.
- `GitIntegration/Models/` — result records and enums: `GitStatus`, `GitStatusEntry`, `GitCommit`,
  `GitSignature`, `GitBranch`, `GitRemote`, `GitDiffEntry`, `GitVersion`, `GitFileState`,
  `GitChangeKind`, `GitUntrackedFilesMode`, `GitInitResult`, `GitCompleted` — the shared "unit"
  result for mutating verbs whose only outcome is success (C# has no generic `void`) — and the
  remote-sync models `GitRefUpdate`, `GitRefUpdateKind`, `GitFetchResult` (`Updates`,
  `DetailAvailable`, `IsUpToDate`), and `GitPushResult` (`Updates`, `HasRejections`).
- `GitIntegration/Execution/` — `GitOptions`, `IGitProcessRunner`, `RunCommandGitProcessRunner`,
  `GitResult<T>`, and the exception hierarchy (`GitException` → `GitExecutableNotFoundException`,
  `GitTimeoutException`, `GitParseException`, `GitCommandException` → `GitRepositoryNotFoundException`,
  `GitNothingToCommitException`, `GitPushRejectedException`, `GitPullConflictException`).
- `GitIntegration/Parsing/` — internal parsers turning raw git output into the `Models/` records,
  including `GitFetchParser` and `GitPushParser` for the two porcelain formats `fetch --porcelain`
  and `push --porcelain` emit.
- `GitIntegration/SemanticTypes/` — the `ktsu.Semantics` wrapper types for git identifiers, including
  the pull-request types (`GitPullRequestNumber`, `GitPullRequestTitle`, `GitPullRequestAuthor`,
  `GitPullRequestWebURI`) added in Phase 5b.
- `GitIntegration/GitProvider.cs` — the abstract hosting base: credential resolution
  (`TryGetCredential`, `ResolveCredential`), the internal `HttpMessageHandler? Handler` transport
  seam, and `CreatePullRequest(GitRepositoryName)`.
- `GitIntegration/GitHubProvider.cs` — the GitHub implementation, over Octokit.
- `GitIntegration/Hosting/` — `IGitHostingProvider` and `IGitPullRequestCreateBuilder` (the public
  hosting contracts), `AzureDevOpsProvider` (the Azure DevOps implementation, over a raw
  `HttpClient`), `AzureDevOpsJson.cs` (its `JsonSerializerContext` and wire-format DTOs),
  `GitPullRequestCreateBuilder` (the one shared builder implementation both providers use), and
  `GitHostingExceptions.cs` (the `GitHostingException` hierarchy).
- `GitIntegration/Models/GitPullRequest.cs` — `GitPullRequest` and `GitPullRequestState`.
- `GitIntegration/ServiceCollectionExtensions.cs` — `AddGitIntegration()` DI registration. Registers
  only the local layer — see the Hosting layer section below for why the hosting layer is not
  registered.

### Dependencies

- `ktsu.RunCommand` — runs the git executable without a shell, via an argument-vector overload.
- `ktsu.Semantics.Strings`, `ktsu.Semantics.Paths` — the semantic string/path base types.
- `ktsu.Essentials`, `ktsu.Essentials.FileSystemProviders.Native` — the filesystem abstraction
  `GitCloneBuilder` uses for its advisory destination pre-check (`Directory.Exists`,
  `Directory.GetFileSystemEntries`); discovery itself needs none, since
  `git rev-parse --show-toplevel` does its own upward walk.
- `Testably.Abstractions.FileSystem.Interface` (`PrivateAssets="all"`, `VersionOverride="10.0.0"`) —
  see the KTSU0006 note below.
- `ktsu.CredentialCache` — resolves hosting-provider credentials from the host's native keyring.
- `Octokit` — GitHub API client backing `GitHubProvider`.
- `System.Text.Json` (`PrivateAssets="all"`) — `AzureDevOpsProvider` and `AzureDevOpsJson` use
  `JsonSerializer`/`JsonSerializerContext` directly to build and parse Azure DevOps's REST payloads
  by hand; see the KTSU0006 note below for why this reference needs its own careful version pin even
  though the package ships in-box.
- `Microsoft.Extensions.DependencyInjection.Abstractions` — DI registration surface.
- `Polyfill` (`PrivateAssets="all"`) — backports newer BCL APIs to the older target frameworks.

### KTSU0006 and `VersionOverride` — a constraint that will recur

`ktsu.Essentials`'s `IFileSystemProvider` is a marker interface with no members of its own; it
inherits `System.IO.Abstractions.IFileSystem` straight from
`Testably.Abstractions.FileSystem.Interface`. `GitCloneBuilder`'s destination pre-check calls
members declared on that base interface directly, which the KTSU0006 analyzer treats as direct use
of a transitively-referenced package requiring its own `PackageReference`.

That reference must carry **both** `PrivateAssets="all"` (it exists only to satisfy the analyzer,
not as part of this library's public surface) **and** a `VersionOverride` pinning it to the lowest
version any consumer could resolve — here, `10.0.0`, because `ktsu.Essentials` 2.0.0's own nuspec
pins that version, while the repo-wide central-package-management version floats higher (`10.3.0`).
Without the override, the library compiles against the higher version, but a consumer resolves
whatever `ktsu.Essentials` itself pins — the lower one. CoreCLR rolls assembly binds forward but
never backward, so a compiled reference to a higher version than what's actually present throws
`FileNotFoundException` for every consumer at runtime. This is invisible in the package's own build
and even in its nuspec; it only surfaces when something actually consumes the packed artifact.
**Verifying the nuspec is not sufficient.** Any future `PackageReference` added solely to satisfy an
analyzer needs this same treatment, not just this one.

### A second KTSU0006 mechanism: the shared framework as the lower resolver

The `Testably.Abstractions.FileSystem.Interface` case above is one way a `VersionOverride`-shaped
hazard shows up: **another package** pins a lower version than central package management floats.
Phase 5b hit a **second** mechanism with the identical outcome, and it nearly shipped.

`AzureDevOpsProvider` and `AzureDevOpsJson` call `JsonSerializer`/`JsonSerializerContext` directly,
which KTSU0006 treats the same way it treats `GitCloneBuilder`'s filesystem calls: direct use of a
transitively-available package requiring its own `PackageReference`. Adding
`<PackageReference Include="System.Text.Json" PrivateAssets="all" />` with no version pin resolved
the package's real **10.0.2** assembly on **net9.0** — overriding the net9.0 shared framework's own
9.0.x `System.Text.Json` — while `PrivateAssets="all"` correctly kept the package out of the nupkg.
A net9.0 consumer would then run on a 9.0.x shared framework carrying `System.Text.Json` 9.0.0.0,
against a compiled reference to 10.0.0.0. CoreCLR rolls binds forward but never backward:
`FileNotFoundException` for every net9.0 consumer.

**The documented check does not catch this.** "Does another package pin a lower version?" answers
*no* here — nothing else in the graph pins `System.Text.Json` at all — and reads as safe. The
resolver forcing the low bound this time was not another package; it was **the target framework's
own shared framework**, which the first mechanism's check never looks at.

The check that does catch it: read `obj/<TargetFramework>/project.assets.json` and confirm what each
target framework actually resolves for the reference's `compile` asset.

- `"compile": { "lib/net9.0/_._": {} }` (an empty placeholder) means the framework wins — the
  package contributes nothing at compile time for that target, and the reference is safe as-is.
- A real assembly path (e.g. `"lib/net9.0/System.Text.Json.dll"`) means the package is overriding
  the framework, and the reference needs a version pinned to what the **lowest-supported** framework
  ships.

This repository's fix pins `System.Text.Json` centrally, in `Directory.Packages.props`, to `9.0.13`
— confirmed via `project.assets.json`'s `compile` asset resolving to the empty `lib/net9.0/_._`
placeholder for **both** `net9.0` and `net10.0`, and confirmed a second way by inspecting the built
assemblies' own references: `bin/Debug/net9.0/ktsu.GitIntegration.dll` references `System.Text.Json`
`9.0.0.0`, `bin/Debug/net10.0/ktsu.GitIntegration.dll` references `10.0.0.0` — each exactly matching
what that target framework's own shared framework ships. No `VersionOverride` was needed here,
unlike the `Testably.Abstractions` case: nothing else in the graph pins `System.Text.Json` to a
conflicting version, so a single central `PackageVersion` below both frameworks' floor is enough.

One consequence of pinning it centrally rather than on the one project: this repo sets
`CentralPackageTransitivePinningEnabled`, so `9.0.13` now applies to any future **transitive**
dependency on `System.Text.Json` too, repo-wide. That is a note rather than a hazard, because a
package needing a higher version would fail restore loudly rather than resolve to something
unexpected. Raising the pin is then the fix, subject to the same `project.assets.json` check above.

This fix also needs `NoWarn="NU1510"`, scoped to that one `PackageReference` item. Pinning to a
version the SDK's package-pruning feature recognises as already covered by the framework makes NuGet
warn "Remove this PackageReference" — the opposite of what KTSU0006 demands. Both diagnostics are
correct from where each analyzer sits; `NoWarn="NU1510"` on the item is what lets both be satisfied
at once without a project-wide suppression.

## Architecture

**Local layer.** `GitClient` implements `IGitClient` by running everything through an
`IGitProcessRunner`. Repository discovery is delegated to `git rev-parse --show-toplevel`, which
performs the upward directory walk itself — this is why `GitClient` needs no filesystem abstraction
of its own. Every verb builder (`GitStatusBuilder`, `GitLogBuilder`, `GitDiffBuilder`, etc.) derives
from `GitCommandBuilder<TResult>`, which owns argument assembly, execution, and failure translation.
A builder is single-use and not thread-safe; the underlying `IGitProcessRunner` is a shared,
thread-safe singleton.

Two non-obvious, load-bearing design points:

1. **Every command is scoped with `git -C <path>`, never a process working directory.**
   `ktsu.RunCommand` has no notion of a working directory, and scoping this way means a failing
   command's exact argument vector — captured on `GitCommandException.Arguments` — can be copied out
   and rerun verbatim on a command line (`git` + the arguments) to reproduce the failure exactly.

2. **Every invocation runs with `GIT_TERMINAL_PROMPT=0` and `LC_ALL=C`.** The former stops git
   blocking forever on a credential prompt that output redirection makes impossible to answer. The
   latter forces English, machine-stable output — this is the whole reason the parsers in
   `Parsing/` can safely match on fixed English phrases (e.g. `"not a git repository"`); without it,
   every message-matching decision would silently degrade on a non-English host.

3. **`Commit` runs git twice.** `git commit` itself, then `git log -1` with this library's pinned
   format, because `commit`'s own output is a human summary — `[main (root-commit) 6b93c10] first
   commit` — carrying only an abbreviated object id, with no machine-readable alternative.

4. **`Init` probes before running, so `GitInitResult.AlreadyExisted` can tell a caller whether a
   repository was already there.** `git init` is idempotent and announces the difference only in
   prose, and it silently ignores `--initial-branch` when re-initialising — the probe (a
   `rev-parse --git-dir` check) is the only way to know either fact.

5. **`Clone`'s destination check is advisory.** Git enforces the same rule itself; the pre-check
   exists only so a doomed clone fails before paying its network cost, and it is deliberately racy —
   a directory can appear between the check and the clone, so git's own refusal is the authority,
   not this check.

6. **`Push` makes `ExecuteAsync` and `TryExecuteAsync` mean different things.** Every other verb's
   two entry points differ only in exception-versus-result; `push` is the one place they diverge in
   more than that. A rejected push exits non-zero *and* prints a complete porcelain record of every
   reference — git got far enough to talk about them and refused some. `ExecuteAsync` stays strict
   and throws `GitPushRejectedException`, which carries the parsed `GitPushResult` so nothing is
   lost. `TryExecuteAsync` returns that same result as a value and leaves the caller to check
   `GitPushResult.HasRejections` — git ran and said exactly what happened, which is what "try"
   should surface rather than forcing a caller to catch an exception for an outcome git already
   described in full.

7. **`Fetch` degrades below git 2.41 rather than parsing human output.** `fetch --porcelain` exists
   only from that version onward, so the builder probes the installed git's version before building
   the command. Below the threshold the fetch still runs and still succeeds, but
   `GitFetchResult.DetailAvailable` is false and `Updates` is empty — so an empty list is never
   mistaken for "nothing changed"; `IsUpToDate` is gated on `DetailAvailable` for exactly that
   reason, rather than trusting an empty `Updates` on its own. The probe deliberately goes through
   the *result-based* `TryExecuteAsync` rather than the throwing `ExecuteAsync`, and the same way
   regardless of which of `Fetch`'s own two entry points is running: a failed probe means the
   version genuinely could not be established, and degrading to unsupported is the truthful answer,
   not a guess — `DetailAvailable` already exists to say exactly that. A genuinely broken git still
   fails loudly moments later, at the fetch itself, in whichever entry point's own idiom.

8. **`Pull` returns `GitCompleted`, not a parsed result.** Everything `git pull` prints is human
   prose with no porcelain form, and this design forbids parsing that prose for every other verb —
   so `pull` does not get a special exemption either. A caller who needs to know what changed uses
   `Status()` and `Log()` afterwards, both of which are precise. A merge conflict is the one outcome
   that gets its own type, `GitPullConflictException`, because it leaves the repository mid-merge —
   `CreateException` is overridden to look for `CONFLICT` on standard *output*, the same trap
   `commit` sets with "nothing to commit" on stderr-vs-stdout, and `LC_ALL=C` is what makes matching
   the literal word dependable.

**Hosting layer.** `GitProvider` is an abstract base with two implementations: `GitHubProvider` over
Octokit, and `AzureDevOpsProvider` over a raw `HttpClient` — Azure DevOps has no client library this
library uses (see the dependency note below). Both go through the same shape: every request-issuing
method resolves a credential via `TryGetCredential`/`ResolveCredential`, which reads a `Credential`
from `ktsu.CredentialCache` keyed by `PersonaGUID`, and every request goes through the provider's own
`HttpClient`-based transport — the two providers differ in how they build it, which the next point
covers.

**`IsAuthenticated` answers "would a request carry a credential", not "does the cache hold an
entry".** A resolved `CredentialWithNothing` is an entry that means "proceed unauthenticated", and a
subtype `ResolveCredential` does not recognise is one no request can ever carry. Both report `false`.
The unrecognised case is *reported* rather than thrown, because a property getter that throws makes a
plain `if (provider.IsAuthenticated)` a hazard, and CA1065 rightly objects. Both it and
`ResolveCredential` go through one private `ResolveRecognisedCredential`, whose only difference is
that it returns `null` for an unrecognised subtype where `ResolveCredential` throws — so the two
answers cannot drift apart as `Credential` subtypes are added.

**One shared `SocketsHttpHandler` per provider type, and a short-lived client or adapter per call.**
A handler owns a connection pool, so building and disposing one per call tears the pool down each
time and leaves its sockets in `TIME_WAIT` — the same socket-exhaustion antipattern as never
disposing anything, approached from the other side. `GitProvider.CreateDefaultHandler` builds both
instances so their settings cannot drift, and sets `PooledConnectionLifetime` (two minutes, matching
`IHttpClientFactory`) so a process-lifetime handler does not go on using a host's original address
after DNS moves it. Neither shared handler is ever disposed, and neither provider is `IDisposable`.

The two providers express "this transport is not mine to dispose" differently, and that asymmetry is
forced, not accidental. `GitProvider.CreateHttpClient` passes `disposeHandler: false` to
`HttpClient`'s own constructor. `GitHubProvider` cannot: Octokit's `HttpClientAdapter` disposes
whatever its factory produced and offers no equivalent flag, so it wraps in `NonOwningHandler`
instead. Wrapping on both sides was tried and rejected — CA2000 cannot see that `HttpClient` takes
ownership of a handler passed to it, so the uniform version needs a suppression, and this repository
allows none.

**One transport seam fakes both providers.** `GitProvider`'s `internal HttpMessageHandler? Handler`
init property is the only test seam this layer has. `AzureDevOpsProvider` uses it directly, building
an `HttpClient` over it. `GitHubProvider` hands the *same* handler to Octokit through
`HttpClientAdapter` (Octokit's own transport abstraction), so a test never has to fake two different
transports for two different vendor SDKs — one `FakeHttpMessageHandler` covers both providers'
tests.

**`GetPullRequestsAsync` returns open pull requests only, requested explicitly of each host.** Both
GitHub and Azure DevOps happen to default to open/active when a caller sends no filter, but
`IGitHostingProvider`'s contract is defined by this library, not by restating whatever a vendor
happens to default to today — GitHub gets `PullRequestRequest { State = ItemStateFilter.Open }`,
Azure DevOps gets `searchCriteria.status=active` on the query string, both sent unconditionally.

**Azure DevOps pull request listing pages; repository enumeration does not.**
`GET .../pullrequests` documents `$top`/`$skip` and no continuation-token header, so a short page is
the only end-of-results signal there is, and `$top` has to be sent on the first page too — without
it, a short page could equally mean the service applied its own default, and the provider could not
tell a last page from a truncated one. `GetPullRequestsAsync` therefore loops until a page comes back
holding fewer than `PullRequestPageSize` entries, advancing `$skip` by what actually arrived rather
than by the size asked for. `GET .../repositories` documents no pagination at all and issues exactly
one request. Octokit pages GitHub's listing itself, so both providers answer the same question the
same way under one interface.

**A plain 403 maps to `GitHostingAuthenticationException` on both hosts, and GitHub needs an explicit
arm to get there.** Octokit's `AuthorizationException` derives from `ApiException`, *not* from
`ForbiddenException` — verified by reflection against the Octokit 14 assembly, not assumed. Without
its own `ForbiddenException` arm, GitHub's most common auth failure ("resource not accessible by
personal access token") falls through to `GitHostingRequestException` while Azure DevOps maps the
same 403 to `GitHostingAuthenticationException`. `RateLimitExceededException`,
`SecondaryRateLimitExceededException`, and `AbuseException` all derive from `ForbiddenException`, so
**arm order is load-bearing**: the three specific ones must precede it. Only `AbuseException` carries
a relative delay (`RetryAfterSeconds`) rather than an absolute instant, so it is anchored to
`DateTimeOffset.UtcNow`; `SecondaryRateLimitExceededException` carries no reset time at all.

**Azure DevOps pull request operations require `Project`; repository enumeration does not.** Azure
DevOps nests repositories under a project — GitHub has no equivalent — so
`AzureDevOpsProvider.Project` is optional: unset, `GetRepositoriesAsync` enumerates the whole
organisation; set, it scopes to that project. Pull requests have no project-less endpoint at all, so
`GetPullRequestsAsync` and pull-request creation both throw `InvalidOperationException` immediately
when `Project` is null, rather than guessing a project by re-enumerating and matching repository
names (rejected: an extra call, and ambiguous whenever two projects share a repository name).

**GitHub's `GetRepositoriesAsync` returns public repositories only, and a token does not widen
that.** It calls `GET /users/{login}/repos`, which does not honour authentication to reveal an
owner's private repositories the way `GET /user/repos` would for the *token's own* account — and
switching to that endpoint would silently stop honouring the configured `Owner`, since it always
describes the token's own repositories regardless of which owner was asked for. Azure DevOps has no
equivalent restriction: its enumeration returns everything the supplied token can see. Two
implementations of the same `IGitHostingProvider.GetRepositoriesAsync` contract genuinely differ
here — do not assume GitHub coverage matches Azure DevOps's, or vice versa.

**A success status code is not a promise the body is JSON.** A proxy interstitial, a captive portal,
or a single sign-on redirect page all arrive as HTML under a `200`. `AzureDevOpsProvider`'s three
success-path deserializations go through `DeserializeSuccessBody`, which turns a `JsonException` into
a `GitHostingRequestException` carrying the status and the original body — a public method whose
documented failure surface is the `GitHostingException` hierarchy must not leak
`System.Text.Json.JsonException`. Failure bodies already had this covered by `ExtractMessage`, which
treats an unparsable body as the message itself. Octokit wraps its own deserialization failures in
`ApiException`, so GitHub needs no equivalent.

**This layer has no integration tier.** Every other tier in this repository that talks to a real
external system (git itself) has one; the hosting layer does not, because hitting real GitHub and
real Azure DevOps needs credentials and network access CI does not have. Its tests — including the
captured-fixture tests — verify this layer's client code against *this project's understanding* of
each API's documented contract, not against the APIs themselves. Fixtures captured from real
responses (see `docs/superpowers/research/2026-08-21-azure-devops-rest-findings.md`) narrow the gap
between "matches our understanding" and "matches reality," but they do not close it: a fixture is a
snapshot, and a vendor can change undocumented behaviour a snapshot never captured. Treat every test
in `GitIntegration.Test/Hosting/` as handler-level verification, never as end-to-end assurance that
either host still behaves this way.

**No DI registration for the hosting layer, and that is deliberate — see
`ServiceCollectionExtensions`'s remarks.** Neither `GitHubProvider` nor `AzureDevOpsProvider` has a
constructor dependency a container could supply: `Owner` is a caller-supplied value with no
sensible default, credential resolution goes through the process-wide `CredentialCache.Instance`
rather than an injected service, and each provider builds its own `HttpClient`. A registered factory
would only wrap `new GitHubProvider { Owner = owner }` — no wiring, no value — so construct a
provider directly instead:

```csharp
GitHubProvider github = new() { Owner = "ktsu-dev".As<GitProviderOwner>() };
AzureDevOpsProvider azure = new() { Owner = "my-org".As<GitProviderOwner>(), Project = "my-project".As<AzureDevOpsProjectName>() };
```

Azure DevOps hosting support does **not** use `Microsoft.TeamFoundationServer.Client` or the other
official TFS/Azure DevOps client package — both were deliberately left off. They pull
`System.Data.SqlClient` (a package with a known high-severity advisory, GHSA-98g6-xh36-x2p7) into
the published package as a direct transitive dependency. `AzureDevOpsProvider` instead builds and
parses every request by hand against Azure DevOps's REST API (`api-version=7.1`), over the same raw
`HttpClient` seam `GitProvider` already provides — the package concern is resolved by not needing
the package, not by working around it.

**Deliberately out of scope for the hosting layer:** merging or completing a pull request, comments,
reviews, repository creation, and webhooks — none of these are implemented, and none should be
documented as present. Deliberately out of scope for the local layer, even later: `commit --amend`,
`add --force`, `switch` (see `IGitCheckoutBuilder`'s remarks for why `checkout` was chosen instead),
and submodule support.

## Testing

Uses MSTest via `MSTest.Sdk`. Two fakes drive the builder and client tests without invoking a real
git binary:

- `Fakes/RecordingGitProcessRunner.cs` — captures the argument vector(s) passed to it, for asserting
  exactly what a builder sends to git.
- `Fakes/ScriptedGitProcessRunner.cs` — returns pre-scripted output/exit codes per call, for testing
  parsing and multi-invocation flows (e.g. `GitClient.DiscoverAsync` running `rev-parse` then
  `remote get-url origin`).

`GitRepositoryMetadataTests.TestPaths` provides a cross-platform `AbsoluteDirectoryPath` root used
across the builder and repository tests.

A third, slower tier lives under `GitIntegration.Test/Integration/` (e.g. `GitRoundTripTests`,
`GitRemoteSyncTests`), marked `[TestCategory("Integration")]`. These run a real git binary against
a throwaway repository per test rather than a fake runner, and self-skip with
`Assert.Inconclusive` when git is not found on `PATH`, so a contributor who has not installed git
still sees a green suite instead of a wall of failures. `GitRemoteSyncTests` exercises `fetch`,
`pull`, and `push` against a bare repository on the local filesystem standing in for a remote —
real push negotiation and real rejection behaviour with no network and no credentials, which is
what would otherwise make these tests flaky or unrunnable in CI. Run this tier alone with:

```bash
dotnet test --filter "TestCategory=Integration"
```

Self-skipping is right for a contributor and wrong for CI, where a runner missing git would report
success having exercised nothing. Setting `KTSU_GIT_INTEGRATION_TESTS_REQUIRED` reverses the
behaviour and makes an absent git a hard failure:

```bash
KTSU_GIT_INTEGRATION_TESTS_REQUIRED=1 dotnet test --filter "TestCategory=Integration"
```

Any value other than empty, `0`, or `false` counts as set.

### A green Windows run does not mean the POSIX runners will pass

Git for Windows ships a **system-level** config at `C:/Program Files/Git/etc/gitconfig` that stock
Linux and macOS git do not have — it sets `pull.rebase=false`, among other things. An integration
test that depends on such a default passes on Windows and fails on both POSIX runners. This has
already happened once: `git pull` refuses divergent branches outright unless a reconciliation
strategy is configured, so the conflicting-pull test merged on Windows and died with
`fatal: Need to specify how to reconcile divergent branches` everywhere else.

`GIT_CONFIG_NOSYSTEM=1` makes git ignore that system file, which reproduces the POSIX environment
closely enough to catch this class of bug from Windows before pushing:

```bash
KTSU_GIT_INTEGRATION_TESTS_REQUIRED=1 GIT_CONFIG_NOSYSTEM=1 dotnet test --filter "TestCategory=Integration"
```

The durable fix is for each test to pin whatever host config it depends on into the throwaway
repository itself, the way `GitRemoteSyncTests.CreateWorkingCopyAsync` pins `user.name`,
`user.email`, `commit.gpgsign`, and `pull.rebase`.

**Remember:** plain `dotnet test`, never `dotnet test --nologo` — see Build Commands above.

## CI/CD

Two workflows, deliberately separate:

- **`.github/workflows/dotnet.yml`** is **synced from a shared ktsu-dev template** — its history is
  a run of `Sync .github\workflows\dotnet.yml` commits. Editing it here is pointless: the next sync
  overwrites the change. It runs on `windows-latest` only and owns versioning, SonarCloud, releases,
  and NuGet publishing.
- **`.github/workflows/cross-platform-tests.yml`** is repo-local and not templated. It builds and
  tests on `ubuntu-latest` and `macos-latest` with `KTSU_GIT_INTEGRATION_TESTS_REQUIRED=1`, so the
  POSIX path is actually exercised. It releases nothing — duplicating the release steps per platform
  would publish more than once.

If a change belongs in the shared pipeline, it goes upstream to the template, not into this repo's
copy of `dotnet.yml`.

Version increments are controlled by commit message tags: `[major]`, `[minor]`, `[patch]`, `[pre]`.

## Code Quality

Do not add global suppressions for warnings. Use explicit suppression attributes with justifications
when needed, with preprocessor defines only as fallback. Make the smallest, most targeted
suppressions possible.

This repository currently has **zero** `[SuppressMessage]` attributes anywhere in `GitIntegration/`.
That is deliberate, not incidental — every analyzer complaint encountered so far has been fixable
by changing the code rather than suppressing the warning. Reach for a suppression only after
confirming the complaint genuinely cannot be addressed by a code change, and keep that bar high.

## Code Style

- `.cs` files use **LF** line endings, not the CRLF that is the general ktsu convention. The repo's
  `.gitattributes` sets `* text=auto eol=lf` for all text files, which overrides `core.autocrlf` so
  the working tree is byte-identical across Windows, Linux, and macOS.
- Tabs for indentation.
- File-scoped namespaces (`namespace ktsu.GitIntegration;`).
- `using` directives go **inside** the namespace, after the namespace declaration — see any file
  under `GitIntegration/` for the pattern.
