# GitIntegration v2 — Design

Date: 2026-08-19
Status: approved for planning

## Summary

`ktsu.GitIntegration` becomes a two-layer library:

- a **local layer** that wraps the `git` binary found in `PATH`, exposing operations through a
  fluent, repository-rooted interface with typed result models, delegating all process execution
  to `ktsu.RunCommand`;
- a **hosting layer** that talks to hosting providers' REST APIs (GitHub via Octokit, Azure DevOps
  via `VssConnection`) to enumerate repositories and hold credentials.

Alongside this, the library migrates from `ktsu.StrongStrings`/`ktsu.StrongPaths` to
`ktsu.Semantics`, drops `LibGit2Sharp` and `ktsu.AppDataStorage`, and adopts
`ktsu.Essentials`-style dependency injection throughout.

## Goals

1. Replace `StrongStrings`/`StrongPaths` with `ktsu.Semantics.Strings`/`ktsu.Semantics.Paths`.
2. Remove `LibGit2Sharp` entirely.
3. Remove `ktsu.AppDataStorage`; use `ktsu.Essentials` DI conventions instead.
4. Expose git operations through a fluent interface over the `git` binary.
5. Delegate all process execution to `ktsu.RunCommand`.
6. Add Azure DevOps as a hosting provider beside GitHub.
7. Establish a test project, which the repository currently lacks.

## Non-goals

- Reimplementing git plumbing in managed code. Every operation shells out.
- Credential *injection* into git subprocesses (see [Constraints](#constraints)).
- Interactive or conflict-resolving commands: `merge`, `rebase`, `bisect`, `stash`, `worktree`,
  `submodule`, `tag`. Deferred to a later version.

## Constraints

These are properties of `ktsu.RunCommand` as it stands, verified against its source. They shape
the design and are not worked around silently.

| Constraint | Consequence | Mitigation |
|---|---|---|
| No working-directory parameter | Cannot set the child process cwd | Scope every command with `git -C <path>` |
| No environment-variable support | Cannot set `GIT_ASKPASS`, `GIT_TERMINAL_PROMPT`, `GIT_CONFIG_*` | Remote operations rely on ambient credential configuration; git may block on an auth prompt, bounded by `GitOptions.Timeout` and the caller's `CancellationToken` |
| `RunCommand` is a static class | Cannot implement an interface | GitIntegration owns `IGitProcessRunner`; the shipped implementation delegates to the static API |

`ktsu.Essentials.ICommandExecutor` is **not** used for git execution. Its contract takes a single
`string command` and runs it through `cmd.exe /c` or `/bin/sh -c`, which would flatten the
argument array into a shell string — a quoting bug for any commit message containing `"`, `` ` ``,
`$`, or `&`, and a shell-injection vector in the general case. `RunCommand`'s
`ExecuteAsync(fileName, IEnumerable<string> arguments, …)` overload preserves the argv array and
bypasses the shell, which is why it is the execution engine here.

Adding argv-aware and environment-aware overloads upstream (either to `ICommandExecutor` or to
`RunCommand`) is a worthwhile follow-up, tracked outside this repository.

## Architecture

```
ktsu.GitIntegration
│
├─ Local layer ─────────────────────────────── git binary in PATH
│    IGitClient
│      └─ GitRepository ── verb builders ── typed models
│                              │
│                        IGitProcessRunner
│                              │
│                  RunCommandGitProcessRunner ──► ktsu.RunCommand
│
└─ Hosting layer ───────────────────────────── provider REST APIs
     IGitHostingProvider
       ├─ GitHubProvider       (Octokit)
       └─ AzureDevOpsProvider  (Microsoft.TeamFoundationServer.Client)
                              │
                      ktsu.CredentialCache
```

The layers are independent. They meet at exactly one seam: a hosting provider produces
`GitRepository` values carrying remote metadata, and `IGitClient.Clone(...)` turns such a value
into a local working copy. Neither layer references the other's internals.

## Local layer

### Execution

```csharp
public sealed record GitProcessResult
{
    public required int ExitCode { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }
    public required IReadOnlyList<string> Arguments { get; init; }
    public bool Success => ExitCode == 0;
}

public interface IGitProcessRunner
{
    Task<GitProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default);
}

public sealed class GitOptions
{
    /// <summary>Executable to invoke. Resolved through PATH unless an absolute path is given.</summary>
    public string ExecutablePath { get; set; } = "git";

    /// <summary>Wall-clock bound on a single git invocation. Null disables the bound.</summary>
    public TimeSpan? Timeout { get; set; } = TimeSpan.FromMinutes(5);
}
```

`RunCommandGitProcessRunner` is the shipped implementation. It calls
`RunCommand.ExecuteAsync(options.ExecutablePath, arguments, outputHandler, cancellationToken)`,
accumulating stdout and stderr into separate `StringBuilder` instances through an `OutputHandler`.
When `Timeout` is set it links a timeout token to the caller's token; `RunCommand` kills the
process tree on cancellation.

### Global arguments

Every invocation is prefixed with the same arguments, applied by the builder base rather than by
each verb:

```
git -C <LocalPath> --no-pager -c core.quotepath=false -c color.ui=false <verb> …
```

- `-C <LocalPath>` — repo scoping, since the runner cannot set a working directory.
- `--no-pager` — git must never block waiting on a pager.
- `core.quotepath=false` — non-ASCII paths are emitted literally, not octal-escaped.
- `color.ui=false` — no ANSI escapes contaminating parsed output.

Commands that are not repository-scoped (`init`, `clone`, `--version`) omit `-C`.

### Fluent surface

```csharp
public interface IGitClient
{
    Task<GitVersion> GetVersionAsync(CancellationToken cancellationToken = default);
    Task<bool> IsRepositoryAsync(AbsoluteDirectoryPath path, CancellationToken cancellationToken = default);
    Task<GitRepository> OpenAsync(AbsoluteDirectoryPath path, CancellationToken cancellationToken = default);
    Task<GitRepository?> DiscoverAsync(AbsoluteDirectoryPath startingPath, CancellationToken cancellationToken = default);

    IGitInitBuilder Init(AbsoluteDirectoryPath path);
    IGitCloneBuilder Clone(GitRepositoryRemotePath source, AbsoluteDirectoryPath destination);
    IGitCloneBuilder Clone(GitRepository repository);
}
```

`OpenAsync` throws `GitRepositoryNotFoundException` when the path is not a work tree.
`DiscoverAsync` walks up from `startingPath` looking for one and returns `null` if it finds none.

`GitRepository` is a single type carrying both hosting metadata and the local verbs.

```csharp
public sealed class GitRepository
{
    /// <summary>Where the working copy is, or is intended to be.</summary>
    public required AbsoluteDirectoryPath LocalPath { get; init; }

    // Hosting metadata. Null means "not known", which is distinct from "known to be empty".
    public GitRepositoryName? Name { get; init; }
    public GitRepositoryWebURI? WebURI { get; init; }
    public GitRepositoryRemotePath? RemotePath { get; init; }

    public Task<bool> IsClonedAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens <see cref="WebURI"/> in the default browser. No-op when WebURI is null.</summary>
    public void OpenWebClient();

    // Inspection
    public IGitStatusBuilder Status();
    public IGitLogBuilder Log();
    public IGitDiffBuilder Diff();
    public IGitRevParseBuilder RevParse(GitRefName revision);

    // Branches
    public IGitBranchListBuilder Branches();
    public IGitBranchCreateBuilder CreateBranch(GitBranchName name);
    public IGitBranchDeleteBuilder DeleteBranch(GitBranchName name);
    public IGitCheckoutBuilder Checkout(GitRefName target);

    // Staging and history
    public IGitAddBuilder Add();
    public IGitCommitBuilder Commit();

    // Remotes
    public IGitRemoteListBuilder Remotes();
    public IGitRemoteAddBuilder AddRemote(GitRemoteName name, GitRepositoryRemotePath url);
    public IGitRemoteRemoveBuilder RemoveRemote(GitRemoteName name);
    public IGitRemoteSetUrlBuilder SetRemoteUrl(GitRemoteName name, GitRepositoryRemotePath url);
    public IGitFetchBuilder Fetch();
    public IGitPullBuilder Pull();
    public IGitPushBuilder Push();
}
```

Metadata nullability is deliberate. A repository produced by a hosting provider has `Name`,
`WebURI`, and `RemotePath` populated and a `LocalPath` that may not exist yet. A repository
produced by `OpenAsync` has `LocalPath` populated, and `RemotePath` back-filled from
`git remote get-url origin` when an `origin` remote exists — so "opened locally" does not imply
blank metadata in the common case. `WebURI` stays null unless a provider supplied it.

`OpenWebClient` is additionally fixed while migrating it. It currently hardcodes `FileName =
"explorer"`, which fails on Linux and macOS even though the library multi-targets and the type
carries no Windows-only marker. It becomes `UseShellExecute = true` with the URI as `FileName`,
which is the portable form, and a no-op when `WebURI` is null.

Verbs do not check `IsCloned` eagerly. Executing a verb against a path that is not a work tree
produces `GitRepositoryNotFoundException`, which derives from `GitCommandException`, so callers
that only catch the base type still behave sensibly.

### Builder contract

```csharp
public interface IGitCommandBuilder<TResult>
{
    /// <summary>The exact argument vector this builder will pass to git. Pure; no I/O.</summary>
    IReadOnlyList<string> BuildArguments();

    Task<TResult> ExecuteAsync(CancellationToken cancellationToken = default);
    Task<GitResult<TResult>> TryExecuteAsync(CancellationToken cancellationToken = default);
}
```

`BuildArguments()` is public because it makes the argument vector directly assertable in tests and
inspectable when debugging, without running anything.

Builders are mutable and return `this` from each configuration method. They are single-use and not
thread-safe; each `repo.Verb()` call returns a fresh builder.

The API is asynchronous only. No synchronous `Execute()` overloads are provided: they would be
`ExecuteAsync().Result` underneath and deadlock-prone in UI and ASP.NET synchronization contexts.

### Error handling

```csharp
public class GitException : Exception { }

/// <summary>The git binary could not be started. Carries no exit code, because nothing ran.</summary>
public sealed class GitExecutableNotFoundException : GitException { }

/// <summary>Git exceeded GitOptions.Timeout and was terminated. Distinct from a caller's cancellation.</summary>
public sealed class GitTimeoutException : GitException
{
    public TimeSpan Timeout { get; }
}

/// <summary>Git ran and exited non-zero.</summary>
public class GitCommandException : GitException
{
    public int ExitCode { get; }
    public IReadOnlyList<string> Arguments { get; }
    public string StandardError { get; }
}

public sealed class GitRepositoryNotFoundException : GitCommandException { }

public sealed record GitResult<T>
{
    private GitResult() { }
    public bool Success => Error is null;   // derived, so it can never disagree with Error
    public T? Value { get; private init; }
    public GitCommandError? Error { get; private init; }

    public static GitResult<T> FromValue(T value);
    public static GitResult<T> FromError(GitCommandError error);   // throws on null
}
```

`ExecuteAsync` throws on a non-zero exit code. `TryExecuteAsync` never throws for a non-zero exit
code and returns a `GitResult<T>`; it still propagates cancellation and programmer errors such as
`ArgumentNullException`.

`GitResult<T>` is a sealed record *class* with a private constructor, not a struct, and `Success`
is derived from `Error` rather than stored. Both choices close the same hole: a struct has a
reachable `default` — from an uninitialised field, an array allocation, or a failed
`TryGetValue` — and with three independently stored members that default reads as "failed, but
with no error", so a consumer writing `result.Error!.ExitCode` on the failure branch gets a
`NullReferenceException`. Deriving `Success` alone would only move the trap to "succeeded with a
null value". As a class with no public constructor, no such instance exists.

`GitCommandException.ExitCode` defaults to `-1`, not `0`, for the same class of reason: `0` is
what this codebase means by success, so a caller reading `ExitCode` after a message-only
construction would see something indistinguishable from a successful run instead of "no data".

### Output parsing

Every command uses git's machine-readable form. Human-facing output is never parsed.

| Verb | Invocation | Parsed into |
|---|---|---|
| `Status` | `status --porcelain=v2 --branch -z [--untracked-files=<mode>]` | `GitStatus` |
| `Log` | `log -z --format=<unit-separated>` | `IReadOnlyList<GitCommit>` |
| `Diff` | `diff --name-status -z [--find-renames]` | `IReadOnlyList<GitDiffEntry>` |
| `Branches` | `for-each-ref --format=<unit-separated> refs/heads refs/remotes` | `IReadOnlyList<GitBranch>` |
| `Remotes` | `remote -v` | `IReadOnlyList<GitRemote>` |
| `RevParse` | `rev-parse --verify <rev>` | `GitCommitSha` |
| `Push` | `push --porcelain` | `GitPushResult` |
| `Fetch` | `fetch --porcelain` (git ≥ 2.41), else stderr | `GitFetchResult` |
| `Commit` | `commit -m <msg>` then `log -1` with the format below | `GitCommit` |

NUL delimiting (`-z`) is used wherever git supports it, so paths containing spaces, quotes, or
newlines survive intact. Fields within a record are separated by the ASCII unit separator `0x1F`,
which cannot occur in a path or a ref name.

The log format string is fixed as:

```
%H%x1f%T%x1f%P%x1f%an%x1f%ae%x1f%aI%x1f%cn%x1f%ce%x1f%cI%x1f%s%x1f%b
```

giving sha, tree, parents, author name/email/ISO-date, committer name/email/ISO-date, subject, and
body — with `-z` terminating each commit record, so a multi-line body cannot be confused for a new
record. `%aI`/`%cI` are strict ISO-8601, parsed directly into `DateTimeOffset` with the correct
offset preserved.

`for-each-ref` uses the same `%1f` separator, which git accepts as a hex escape in format strings:

```
%(refname:short)%1f%(objectname)%1f%(upstream:short)%1f%(HEAD)
```

Both format strings are pinned by parser fixtures and confirmed against the installed git during
implementation.

Each parser is an `internal static class` exposing a pure `string → model` method with no I/O and
no dependency on `IGitProcessRunner`. This is the primary testability lever: the majority of the
test suite exercises parsers against captured fixtures, needing neither a git binary nor a
filesystem.

### Result models

```csharp
public sealed record GitStatus
{
    public GitBranchName? Branch { get; init; }
    public GitBranchName? Upstream { get; init; }
    public int Ahead { get; init; }
    public int Behind { get; init; }
    public bool IsDetached { get; init; }
    public required IReadOnlyList<GitStatusEntry> Entries { get; init; }
    public bool IsClean => Entries.Count == 0;
}

public sealed record GitStatusEntry
{
    public required GitFileState IndexState { get; init; }
    public required GitFileState WorkTreeState { get; init; }
    public required RelativeFilePath Path { get; init; }
    public RelativeFilePath? OriginalPath { get; init; }   // renames and copies
}

public sealed record GitCommit
{
    public required GitCommitSha Sha { get; init; }
    public required GitCommitSha TreeSha { get; init; }
    public required IReadOnlyList<GitCommitSha> ParentShas { get; init; }
    public required GitSignature Author { get; init; }
    public required GitSignature Committer { get; init; }
    public required string Subject { get; init; }
    public string Body { get; init; } = string.Empty;
}

public sealed record GitSignature
{
    public required GitAuthorName Name { get; init; }
    public required GitAuthorEmail Email { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}

public sealed record GitBranch
{
    public required GitBranchName Name { get; init; }
    public required GitCommitSha Sha { get; init; }
    public GitBranchName? Upstream { get; init; }
    public bool IsCurrent { get; init; }
    public bool IsRemote { get; init; }
}

public sealed record GitRemote
{
    public required GitRemoteName Name { get; init; }
    public required GitRepositoryRemotePath FetchUrl { get; init; }
    public required GitRepositoryRemotePath PushUrl { get; init; }
}

public sealed record GitDiffEntry
{
    public required GitChangeKind Kind { get; init; }
    public required RelativeFilePath Path { get; init; }
    public RelativeFilePath? OriginalPath { get; init; }
    public int? SimilarityPercent { get; init; }
}

public enum GitFileState { Unmodified, Modified, Added, Deleted, Renamed, Copied, Untracked, Ignored, Unmerged, TypeChanged }
public enum GitChangeKind { Added, Copied, Deleted, Modified, Renamed, TypeChanged, Unmerged, Unknown }
```

`GitVersion`, `GitPushResult`, `GitFetchResult`, and `GitRefUpdate` follow the same shape.

## Hosting layer

The existing provider layer is retained, migrated to Semantics, and given a DI-friendly interface.
The abstract class keeps its current name so existing consumers are not broken more than the
Semantics migration already requires.

```csharp
public interface IGitHostingProvider
{
    GitProviderName Name { get; }
    GitProviderOwner Owner { get; }
    PersonaGUID PersonaGUID { get; }
    bool IsAuthenticated { get; }

    Task<IReadOnlyList<GitRepository>> GetRepositoriesAsync(CancellationToken cancellationToken = default);
}

public abstract class GitProvider : IGitHostingProvider { … }
public sealed class GitHubProvider : GitProvider { … }
public sealed class AzureDevOpsProvider : GitProvider { … }
```

Two changes to the current shape:

- `RefreshRemoteRepositories()` (synchronous, and today a no-op that only sets credentials) becomes
  `GetRepositoriesAsync`, which actually enumerates repositories. Network calls should not be
  synchronous.
- `ConcurrentBag<GitRepository> Repositories` is replaced by the returned
  `IReadOnlyList<GitRepository>`. The bag was unordered, never cleared between refreshes, and
  therefore accumulated duplicates.

### GitHubProvider

Continues to use Octokit, authenticating from `ktsu.CredentialCache` when a
`CredentialWithUsernamePassword` is available, and enumerating repositories for `Owner`.

### AzureDevOpsProvider

Follows the pattern already established in `ktsu.BuildMonitor`: a `VssConnection` to
`https://dev.azure.com/{Owner}` with `VssBasicCredential(string.Empty, personalAccessToken)`, then
`GitHttpClient.GetRepositoriesAsync`. Azure DevOps nests repositories under a project, so the
provider gains an optional project filter:

```csharp
public sealed record AzureDevOpsProjectName : SemanticString<AzureDevOpsProjectName>;

public sealed class AzureDevOpsProvider : GitProvider
{
    public AzureDevOpsProjectName? Project { get; init; }   // null enumerates all projects
}
```

`Owner` carries the organization name.

## Semantics migration

### Type mapping

| Current | Replacement |
|---|---|
| `sealed record class X : StrongStringAbstract<X>` | `sealed record X : SemanticString<X>` with a validation attribute |
| `ktsu.StrongPaths.AbsoluteDirectoryPath` | `ktsu.Semantics.Paths.AbsoluteDirectoryPath` |
| `"…".As<X>()` | unchanged — `As<T>` is provided by `SemanticStringExtensions` in `ktsu.Semantics.Strings` |

### Construction

Existing members default with `= new()`. `SemanticString<TDerived>` is a `record`, so `new()`
compiles but bypasses `Create`'s validation and yields an empty value — exactly the primitive
obsession the migration is meant to remove. All such members become `required`, forcing
construction through `Create`, `TryCreate`, or `As<T>()`.

### Types

Migrated: `GitProviderGUID`, `GitProviderName`, `GitProviderOwner`, `GitRepositoryName`,
`GitRepositoryWebURI`, `GitRepositoryRemotePath`.

Added for the local layer: `GitBranchName`, `GitRemoteName`, `GitRefName`, `GitCommitMessage`,
`GitAuthorName`, `GitAuthorEmail`, `AzureDevOpsProjectName`, and

```csharp
[RegexMatch("^[0-9a-fA-F]{4,40}$")]
public sealed record GitCommitSha : SemanticString<GitCommitSha>
{
    protected override string MakeCanonical(string input) => input.Trim().ToLowerInvariant();
}
```

Validation attributes are applied where a genuine rule exists (SHA hex, branch names excluding
git's forbidden character sequences, non-whitespace content). Types without a real rule get
`[HasNonWhitespaceContent]` rather than an invented constraint.

## Dependency injection

Registration follows the `ktsu.Essentials` convention exactly: idempotent via `TryAdd*`, exposed
by both concrete type and interface, container-owned lifetimes.

```csharp
services.AddGitIntegration();
services.AddGitIntegration(options => options.ExecutablePath = "/usr/local/bin/git");

services.AddGitHubProvider();
services.AddAzureDevOpsProvider();
```

`AddGitIntegration` registers `GitOptions`, `IGitProcessRunner` →
`RunCommandGitProcessRunner`, and `IGitClient` → `GitClient`, all as singletons. Provider
registrations additionally `TryAddEnumerable` their `IGitHostingProvider` so
`GetServices<IGitHostingProvider>()` returns every configured host.

`GitRepository` is never resolved from the container; it is produced by `IGitClient`.

### Filesystem access

`ktsu.Essentials` is a real dependency, not a convention-only one. Every filesystem touchpoint —
`DiscoverAsync` walking up for a work tree, `Clone` checking its destination, `Init` checking its
target — goes through `ktsu.Essentials.IFileSystemProvider`, which derives from
`System.IO.Abstractions.IFileSystem`. `AddGitIntegration` calls `AddNativeFileSystemProvider` so
the default is the real filesystem, and tests substitute an in-memory filesystem instead of
touching disk.

This is also what replaces `ktsu.AppDataStorage`: the removed package was the only thing in the
manifest gesturing at storage, and it was unreferenced. Filesystem access now has an injected,
testable abstraction rather than static `Directory`/`File` calls.

## Package manifest

| Action | Package | Reason |
|---|---|---|
| Remove | `LibGit2Sharp` | Replaced by the binary wrapper; referenced by no source file today |
| Remove | `ktsu.AppDataStorage` | Replaced by Essentials DI; referenced by no source file today |
| Remove | `ktsu.StrongPaths` | Superseded by `ktsu.Semantics.Paths` |
| Remove | `ktsu.StrongStrings` | Superseded by `ktsu.Semantics.Strings` |
| Add | `ktsu.Semantics.Strings` | Semantic string types |
| Add | `ktsu.Semantics.Paths` | Semantic path types |
| Add | `ktsu.RunCommand` | Process execution |
| Add | `ktsu.Essentials` | `IFileSystemProvider` for filesystem access |
| Add | `ktsu.Essentials.FileSystemProviders.Native` | Default filesystem registration |
| Add | `Microsoft.Extensions.DependencyInjection.Abstractions` | `IServiceCollection` extensions |
| Add | `Microsoft.TeamFoundationServer.Client` | Azure DevOps repositories |
| Add | `Microsoft.VisualStudio.Services.Client` | Azure DevOps authentication |
| Keep | `Octokit`, `ktsu.CredentialCache`, `ktsu.Extensions`, `Polyfill` | Still used |

Target frameworks remain `net10.0;net9.0`.

## Testing

A new `GitIntegration.Test` project (MSTest via `MSTest.Sdk`, latest target only), in three tiers.

**Tier 1 — parser tests.** Captured git output in, expected model out. Pure functions; no git
binary, no filesystem. The bulk of the suite. Fixtures cover the awkward cases deliberately: paths
with spaces and non-ASCII characters, renames and copies with similarity scores, merge commits with
multiple parents, detached HEAD, empty repositories with no commits, ahead/behind counts, and
multi-line commit bodies.

**Tier 2 — argument-vector tests.** A recording `IGitProcessRunner` fake captures the argv a
builder produces; assertions pin the exact array. Verifies global-argument injection, `-C` scoping,
and that fluent options map to the intended flags. No git binary.

**Tier 3 — integration tests.** Real git against temporary repositories created per-test, marked
`[TestCategory("Integration")]`, self-skipping when no git binary is present so CI without git
still passes. These cover the round trip: init, add, commit, branch, checkout, status, log.

Assertions use semantic forms (`Assert.AreEqual`, `Assert.IsNotNull`, `CollectionAssert`) rather
than `Assert.IsTrue`, per the repository's conventions.

## Implementation phases

The scope is large for a single pass, so it is sequenced into five phases. Each ends with a
building, passing solution, so work can stop at a phase boundary without leaving the repository
broken.

1. **Foundation.** Package manifest changes, Semantics migration of the six existing types,
   `required` conversion, `OpenWebClient` fix, test project scaffolding. No new functionality —
   this phase is complete when the existing code compiles against Semantics with LibGit2Sharp and
   AppDataStorage gone.
2. **Execution core.** `GitOptions`, `GitProcessResult`, `IGitProcessRunner`,
   `RunCommandGitProcessRunner`, exception hierarchy, `GitResult<T>`, the builder base with global
   argument injection, and `AddGitIntegration`. Tier-2 argv tests land here.
3. **Read-only verbs.** `IGitClient`, `GitRepository`, and `Status`, `Log`, `Diff`, `RevParse`,
   `Branches`, `Remotes`, with their parsers and models. The bulk of tier-1 parser tests land here.
4. **Mutating local verbs.** `Init`, `Clone`, `Add`, `Commit`, `CreateBranch`, `DeleteBranch`,
   `Checkout`, and the remote add/remove/set-url commands. Tier-3 integration tests land here.
5. **Remote sync and hosting.** `Fetch`, `Pull`, `Push`; then `IGitHostingProvider`, the
   `GitProvider` async refactor, `GitHubProvider` repository enumeration, and `AzureDevOpsProvider`
   with its DI registration.

Phases 1–2 are prerequisites for everything. Phases 3 and 4 are largely independent of phase 5's
hosting work and could proceed in parallel if desired.

## Risks

| Risk | Mitigation |
|---|---|
| git output format drift between versions | Parse only documented machine formats; pin with fixtures; assert a minimum git version |
| `fetch --porcelain` requires git ≥ 2.41 | Detect version once via `GetVersionAsync`; fall back to stderr parsing below that |
| Remote operations block on an auth prompt | `GitOptions.Timeout` plus caller `CancellationToken`; RunCommand kills the process tree. The timeout surfaces as `GitTimeoutException`, never as a bare `OperationCanceledException`, so a caller can tell "git hung" (retryable) from "I cancelled" (not) |
| Azure DevOps client packages are large and `netstandard2.0` | Acceptable; the same pair is already used by `ktsu.BuildMonitor` |
| Merged `GitRepository` mixes local and hosting concerns | Metadata is nullable rather than blank; `RemotePath` back-filled from `origin`; verbs fail with a specific exception type |

## Open follow-ups (outside this repository)

1. Add environment-variable support to `ktsu.RunCommand`, enabling `GIT_TERMINAL_PROMPT=0` and
   `GIT_ASKPASS`, which would in turn allow credential injection for remote operations.
2. Add a working-directory parameter to `ktsu.RunCommand`, removing the need for `git -C`.
3. Consider an argv-aware overload on `ktsu.Essentials.ICommandExecutor`, after which a
   `ktsu.Essentials.CommandExecutors.RunCommand` package would be worth shipping.
