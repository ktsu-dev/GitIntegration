# ktsu.GitIntegration

> A .NET library that wraps the `git` binary behind a fluent, strongly-typed interface, and unifies access to hosted Git providers behind a single abstraction.

[![License](https://img.shields.io/github/license/ktsu-dev/GitIntegration.svg?label=License&logo=nuget)](LICENSE.md)
[![NuGet Version](https://img.shields.io/nuget/v/ktsu.GitIntegration?label=Stable&logo=nuget)](https://nuget.org/packages/ktsu.GitIntegration)
[![NuGet Version](https://img.shields.io/nuget/vpre/ktsu.GitIntegration?label=Latest&logo=nuget)](https://nuget.org/packages/ktsu.GitIntegration)
[![NuGet Downloads](https://img.shields.io/nuget/dt/ktsu.GitIntegration?label=Downloads&logo=nuget)](https://nuget.org/packages/ktsu.GitIntegration)
[![GitHub commit activity](https://img.shields.io/github/commit-activity/m/ktsu-dev/GitIntegration?label=Commits&logo=github)](https://github.com/ktsu-dev/GitIntegration/commits/main)
[![GitHub contributors](https://img.shields.io/github/contributors/ktsu-dev/GitIntegration?label=Contributors&logo=github)](https://github.com/ktsu-dev/GitIntegration/graphs/contributors)
[![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/ktsu-dev/GitIntegration/dotnet.yml?label=Build&logo=github)](https://github.com/ktsu-dev/GitIntegration/actions)

## Introduction

`ktsu.GitIntegration` is a two-layer library. The **local layer** wraps the `git` executable found
on `PATH` behind a fluent, strongly-typed interface: open or discover a repository, then build and
run both read-only commands (`status`, `log`, `diff`, `branches`, `remotes`, `rev-parse`) and
mutating commands (`init`, `clone`, `add`, `commit`, branch creation and deletion, `checkout`,
remote management, and remote sync via `fetch`, `pull`, and `push`) without shelling out or
hand-parsing porcelain output yourself. The **hosting layer** — the original half of this library —
unifies access to hosted Git providers behind a `GitProvider` abstraction: `GitHubProvider`, built on
Octokit, and `AzureDevOpsProvider`, built on a raw `HttpClient` against Azure DevOps's REST API.
Both enumerate repositories and list and create pull requests through the same
`IGitHostingProvider` contract.

Every value that would otherwise be a bare `string` — a branch name, a commit SHA, a remote name, an
author email — is instead a validated semantic type built on `ktsu.Semantics`, so a `GitBranchName`
can no longer be accidentally passed where a `GitCommitSha` is expected.

Azure DevOps hosting deliberately does not use `Microsoft.TeamFoundationServer.Client` or the other
official TFS/Azure DevOps client package — both pull in `System.Data.SqlClient`, which carries a
known high-severity advisory, as a direct dependency of the published package. `AzureDevOpsProvider`
builds and parses its requests by hand instead.

## Features

- **Local Git Client**: `IGitClient`/`GitClient` finds and opens repositories — `GetVersionAsync`,
  `IsRepositoryAsync`, `OpenAsync`, `DiscoverAsync` — and creates new ones — `Init(...)`,
  `Clone(...)` — by delegating every invocation to `ktsu.RunCommand`.
- **Fluent Verb Builders**: `GitRepository` exposes one builder per read-only verb — `Status()`,
  `Log()`, `Diff()`, `Branches()`, `Remotes()`, `RevParse(...)` — and one per mutating verb —
  `Add()`, `Commit(...)`, `CreateBranch(...)`, `DeleteBranch(...)`, `Checkout(...)`,
  `AddRemote(...)`, `RemoveRemote(...)`, `SetRemoteUrl(...)`, `Fetch()`, `Pull()`, `Push()` — each
  configurable via chained method calls and run with `ExecuteAsync` or the non-throwing
  `TryExecuteAsync`.
- **Remote Sync**: `Fetch()` downloads objects and refs without touching the working tree,
  `Pull()` fetches and integrates into the current branch, and `Push()` sends local commits —
  `fetch` and `push` report a machine-readable, per-reference account of what happened via
  `GitFetchResult`/`GitPushResult`, and a rejected push is the one place in this library where
  `ExecuteAsync` and `TryExecuteAsync` diverge in more than exception-versus-result.
- **Strongly-Typed Results**: `GitStatus`, `GitCommit`, `GitBranch`, `GitRemote`, `GitDiffEntry`,
  `GitVersion`, `GitInitResult`, `GitCompleted`, `GitFetchResult`, `GitPushResult`, and
  `GitRefUpdate` records replace ad-hoc porcelain parsing with typed models — `GitCompleted` is the
  shared result for mutating verbs whose only outcome is success.
- **Reproducible Failures**: every command is scoped with `git -C <path>` instead of a process
  working directory, so a failing invocation's exact argument vector can be read off a
  `GitCommandException` and rerun verbatim.
- **Locale-Safe Parsing**: every invocation runs with `GIT_TERMINAL_PROMPT=0` (no hanging credential
  prompts) and `LC_ALL=C` (English, machine-stable output), which is what makes the output parsers
  safe to write against fixed English text.
- **Dependency Injection**: `AddGitIntegration()` registers the client, process runner, and options
  as singletons in one call. The hosting layer is constructed directly instead — see
  [Working with a Hosting Provider](#working-with-a-hosting-provider).
- **Hosting Provider Abstraction**: `IGitHostingProvider` defines a common contract for enumerating
  repositories, listing open pull requests, and creating a pull request —
  `GitHubProvider` implements it on top of Octokit, `AzureDevOpsProvider` on a raw `HttpClient`
  against Azure DevOps's REST API.
- **Credential Resolution**: hosting providers integrate with `ktsu.CredentialCache`, so credentials
  come from the host's native keyring rather than configuration files.
- **Semantic Git Types**: validated wrapper types for every identifier Git tooling passes around, so
  mismatched arguments fail at compile time rather than at runtime.

## Installation

### Package Manager Console

```powershell
Install-Package ktsu.GitIntegration
```

### .NET CLI

```bash
dotnet add package ktsu.GitIntegration
```

### Package Reference

```xml
<PackageReference Include="ktsu.GitIntegration" Version="x.y.z" />
```

## Usage Examples

### Basic Example

Register the library with dependency injection, then resolve `IGitClient`:

```csharp
using ktsu.GitIntegration;
using Microsoft.Extensions.DependencyInjection;

ServiceCollection services = new();
services.AddGitIntegration();

using ServiceProvider provider = services.BuildServiceProvider();
IGitClient client = provider.GetRequiredService<IGitClient>();
```

### Discovering a Repository and Reading Its Status

```csharp
using ktsu.GitIntegration;
using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

AbsoluteDirectoryPath here = Environment.CurrentDirectory.As<AbsoluteDirectoryPath>();
GitRepository? repository = await client.DiscoverAsync(here);

if (repository is not null)
{
    GitStatus status = await repository.Status().ExecuteAsync();

    Console.WriteLine(status.IsClean
        ? "Working tree is clean."
        : $"{status.Entries.Count} changed path(s) on {status.Branch?.WeakString}.");
}
```

### Listing Commits and Diffs

```csharp
using ktsu.GitIntegration;
using ktsu.Semantics.Strings;

IReadOnlyList<GitCommit> commits = await repository.Log()
    .Take(10)
    .FirstParentOnly()
    .ExecuteAsync();

foreach (GitCommit commit in commits)
{
    Console.WriteLine($"{commit.Sha.WeakString[..7]} {commit.Subject}");
}

IReadOnlyList<GitDiffEntry> changes = await repository.Diff()
    .Staged()
    .DetectRenames()
    .ExecuteAsync();
```

### Initializing or Cloning a Repository

`Init` probes the target path before running `git init`, so `GitInitResult.AlreadyExisted` can tell
a caller whether a repository was already there — `git init` is idempotent and silently ignores
`--initial-branch` when re-initialising, so a caller that asked for a particular initial branch and
got `AlreadyExisted == true` did not get the branch it asked for:

```csharp
using ktsu.GitIntegration;
using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

AbsoluteDirectoryPath target = Environment.CurrentDirectory.As<AbsoluteDirectoryPath>();

GitInitResult init = await client.Init(target)
    .WithInitialBranch("main".As<GitBranchName>())
    .ExecuteAsync();

GitRepository repository = init.Repository;
```

`Clone` builds `git clone`. Its destination pre-check is advisory only — git enforces the same rule
itself, so the check exists solely to fail a doomed clone before it pays its network cost, and it is
deliberately racy:

```csharp
using ktsu.GitIntegration;
using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

GitRepositoryRemotePath source = "https://github.com/ktsu-dev/GitIntegration.git".As<GitRepositoryRemotePath>();
AbsoluteDirectoryPath destination = Environment.CurrentDirectory.As<AbsoluteDirectoryPath>();

GitRepository cloned = await client.Clone(source, destination)
    .WithDepth(1)
    .ReportingProgress(new Progress<string>(line => Console.WriteLine(line)))
    .ExecuteAsync();
```

### Staging and Committing Changes

`Commit` runs git twice: `git commit` itself, then `git log -1` with this library's pinned format,
because `commit`'s own output is a human summary carrying only an abbreviated object id, with no
machine-readable alternative:

```csharp
using ktsu.GitIntegration;
using ktsu.Semantics.Strings;

await repository.Add().All().ExecuteAsync();

GitCommit commit = await repository.Commit("Add feature X".As<GitCommitMessage>())
    .WithBody("Longer explanation of the change.")
    .WithAuthor("Ada Lovelace".As<GitAuthorName>(), "ada@example.com".As<GitAuthorEmail>())
    .ExecuteAsync();

Console.WriteLine(commit.Sha.WeakString);
```

Committing with nothing staged throws `GitNothingToCommitException`, a `GitCommandException`
specialization, rather than the generic base type — the one `commit` failure that is an ordinary
program state rather than a fault:

```csharp
using ktsu.GitIntegration;
using ktsu.Semantics.Strings;

try
{
    await repository.Commit("Nothing changed".As<GitCommitMessage>()).ExecuteAsync();
}
catch (GitNothingToCommitException)
{
    Console.WriteLine("Nothing was staged; skipping this commit.");
}
```

### Creating Branches and Switching

```csharp
using ktsu.GitIntegration;
using ktsu.Semantics.Strings;

await repository.CreateBranch("feature/new-thing".As<GitBranchName>())
    .StartingAt("main".As<GitRefName>())
    .ExecuteAsync();

await repository.Checkout("feature/new-thing".As<GitRefName>()).ExecuteAsync();

// Later, once the branch is no longer needed:
await repository.DeleteBranch("feature/new-thing".As<GitBranchName>())
    .Force()
    .ExecuteAsync();
```

### Managing Remotes

```csharp
using ktsu.GitIntegration;
using ktsu.Semantics.Strings;

GitRepositoryRemotePath url = "https://github.com/example/repo.git".As<GitRepositoryRemotePath>();

await repository.AddRemote("upstream".As<GitRemoteName>(), url)
    .WithFetch()
    .ExecuteAsync();

await repository.SetRemoteUrl("upstream".As<GitRemoteName>(), url)
    .ForPushOnly()
    .ExecuteAsync();

await repository.RemoveRemote("upstream".As<GitRemoteName>()).ExecuteAsync();
```

### Fetching

`fetch --porcelain` is only available from git 2.41 onward, so `Fetch()` probes the installed git's
version first. Below that threshold the fetch still runs and still succeeds, but
`GitFetchResult.DetailAvailable` is false and `Updates` is empty — check `DetailAvailable` before
trusting an empty `Updates` as "nothing changed":

```csharp
using ktsu.GitIntegration;
using ktsu.Semantics.Strings;

GitFetchResult fetched = await repository.Fetch()
    .FromRemote("origin".As<GitRemoteName>())
    .Prune()
    .WithTags()
    .ExecuteAsync();

if (fetched.DetailAvailable)
{
    Console.WriteLine(fetched.IsUpToDate
        ? "Already up to date."
        : $"{fetched.Updates.Count} reference(s) updated.");
}
else
{
    Console.WriteLine("Fetch completed, but this git is older than 2.41 so no per-reference detail is available.");
}
```

### Pulling

`Pull()` returns `GitCompleted` rather than a parsed result, because everything `git pull` prints is
human prose with no porcelain form. Use `Status()` and `Log()` afterwards to learn what changed. A
merge conflict is the one outcome with its own exception, `GitPullConflictException`, because it
leaves the repository mid-merge:

```csharp
using ktsu.GitIntegration;
using ktsu.Semantics.Strings;

try
{
    await repository.Pull()
        .FromRemote("origin".As<GitRemoteName>())
        .WithBranch("main".As<GitBranchName>())
        .FastForwardOnly()
        .ExecuteAsync();
}
catch (GitPullConflictException)
{
    GitStatus status = await repository.Status().ExecuteAsync();
    IEnumerable<GitStatusEntry> unmerged = status.Entries.Where(e => e.IndexState == GitFileState.Unmerged);

    Console.WriteLine($"Pull left {unmerged.Count()} unmerged path(s); resolve them and commit.");
}
```

`FastForwardOnly()` and `Rebase()` are mutually exclusive — combining them throws
`InvalidOperationException` when the argument vector is built, since they mean opposite things about
history.

### Pushing — Why `ExecuteAsync` and `TryExecuteAsync` Disagree

`push` is the one verb in this library where the two entry points mean genuinely different things,
not just exception-versus-result. A rejected push exits non-zero from git *and* prints a complete
porcelain record of every reference — git got far enough to talk about them and refused some. A
caller who does not know this will get it wrong by assuming `TryExecuteAsync` returning `Success`
means the push landed:

```csharp
using ktsu.GitIntegration;
using ktsu.Semantics.Strings;

// ExecuteAsync stays strict: a rejection throws, and the exception carries the full parsed result.
try
{
    GitPushResult pushed = await repository.Push()
        .ToRemote("origin".As<GitRemoteName>())
        .WithBranch("main".As<GitBranchName>())
        .SettingUpstream()
        .ExecuteAsync();
}
catch (GitPushRejectedException ex)
{
    // ex.Result is the same GitPushResult a successful push would have returned.
    foreach (GitRefUpdate update in ex.Result!.Updates.Where(u => u.IsRejected))
    {
        Console.WriteLine($"{update.Reference.WeakString}: {update.Summary}");
    }
}
```

`TryExecuteAsync` does **not** throw for a rejection — git ran and reported exactly what happened,
so that report comes back as a successful `GitResult`. Always check `HasRejections`, not just
`Success`:

```csharp
using ktsu.GitIntegration;
using ktsu.Semantics.Strings;

GitResult<GitPushResult> result = await repository.Push()
    .ToRemote("origin".As<GitRemoteName>())
    .WithBranch("main".As<GitBranchName>())
    .TryExecuteAsync();

if (result.Success && result.Value!.HasRejections)
{
    // This is still result.Success == true: TryExecuteAsync only fails when git never reached
    // the remote at all. A rejection is reported as data, not as GitResult failure.
    Console.WriteLine("Push ran but at least one reference was rejected — check result.Value.Updates.");
}
```

`ForceWithLease()` wins over `Force()` when both are set, being the safer of the two — it refuses if
the remote moved since it was last fetched.

### Resolving a Revision Without Throwing

`TryExecuteAsync` reports a non-zero exit as a result instead of an exception — useful when "no such
revision" is an expected outcome rather than a failure:

```csharp
using ktsu.GitIntegration;
using ktsu.Semantics.Strings;

GitResult<GitCommitSha> result = await repository
    .RevParse("maybe-missing-branch".As<GitRefName>())
    .TryExecuteAsync();

if (result.Success)
{
    Console.WriteLine(result.Value!.WeakString);
}
else
{
    Console.WriteLine($"git exited {result.Error!.ExitCode}: {result.Error.StandardError}");
}
```

### Reproducing a Failing Command

`ExecuteAsync` throws `GitCommandException` on a non-zero exit, carrying the exact argument vector
git was invoked with:

```csharp
try
{
    await repository.RevParse("no-such-ref".As<GitRefName>()).ExecuteAsync();
}
catch (GitCommandException ex)
{
    // ex.Arguments already begins with "-C <path>", so this can be pasted straight after `git`
    // on a command line to reproduce the failure exactly.
    Console.WriteLine("git " + string.Join(' ', ex.Arguments));
}
```

### Working with a Hosting Provider

Providers are constructed directly rather than resolved from DI: `GitHubProvider` and
`AzureDevOpsProvider` need only a caller-supplied `Owner` (and, for Azure DevOps, an optional
`Project`), so there is nothing a container would meaningfully wire up.

```csharp
using ktsu.GitIntegration;
using ktsu.Semantics.Strings;

IGitHostingProvider github = new GitHubProvider
{
    Owner = "ktsu-dev".As<GitProviderOwner>(),
};

// Credentials come from ktsu.CredentialCache, keyed by PersonaGUID — nothing to configure here
// unless a specific persona is needed.
IReadOnlyList<GitRepository> repositories = await github.GetRepositoriesAsync();
```

`GitHubProvider.GetRepositoriesAsync` returns only `Owner`'s **public** repositories — GitHub's
`GET /users/{login}/repos` does not honour authentication to reveal private ones. Azure DevOps has
no equivalent restriction: it returns everything the resolved credential can see.

```csharp
using ktsu.GitIntegration;
using ktsu.Semantics.Strings;

IGitHostingProvider azure = new AzureDevOpsProvider
{
    Owner = "my-org".As<GitProviderOwner>(),
    Project = "my-project".As<AzureDevOpsProjectName>(),
};

IReadOnlyList<GitRepository> repositories = await azure.GetRepositoriesAsync();
```

`Project` is only required for pull request operations — Azure DevOps has no project-less pull
request endpoint, and calling `GetPullRequestsAsync` or `CreatePullRequest` without it throws
`InvalidOperationException` immediately. `GetPullRequestsAsync` returns **open** pull requests
only, on both hosts:

```csharp
using ktsu.GitIntegration;
using ktsu.Semantics.Strings;

GitRepositoryName repositoryName = "GitIntegration".As<GitRepositoryName>();
IReadOnlyList<GitPullRequest> openPullRequests = await azure.GetPullRequestsAsync(repositoryName);

foreach (GitPullRequest pullRequest in openPullRequests)
{
    Console.WriteLine($"#{pullRequest.Number.WeakString} {pullRequest.Title.WeakString} ({pullRequest.State})");
}
```

Creating a pull request goes through a builder, the same idiom as the local layer's mutating verbs:

```csharp
using ktsu.GitIntegration;
using ktsu.Semantics.Strings;

GitPullRequest created = await azure.CreatePullRequest(repositoryName)
    .From("feature/new-thing".As<GitBranchName>())
    .Into("main".As<GitBranchName>())
    .Titled("Add feature X".As<GitPullRequestTitle>())
    .Describing("Longer explanation of the change.")
    .ExecuteAsync();

Console.WriteLine(created.WebURI?.WeakString);
```

A hosting failure — authentication, not found, rate limiting, or anything else the host reports —
surfaces as a `GitHostingException` subtype carrying the provider name, HTTP status, and response
body, mirroring how `GitCommandException` carries the argument vector for the local layer:

```csharp
try
{
    await azure.CreatePullRequest(repositoryName)
        .From("feature/new-thing".As<GitBranchName>())
        .Into("main".As<GitBranchName>())
        .Titled("Add feature X".As<GitPullRequestTitle>())
        .ExecuteAsync();
}
catch (GitHostingAuthenticationException ex)
{
    Console.WriteLine($"{ex.ProviderName} rejected the request: {ex.StatusCode}");
}
catch (GitHostingRateLimitException ex)
{
    Console.WriteLine($"Rate limited until {ex.ResetsAt}.");
}
```

### Working with Semantic Types

```csharp
using ktsu.GitIntegration;
using ktsu.Semantics.Strings;

GitBranchName branch = "main".As<GitBranchName>();
GitRemoteName remote = "origin".As<GitRemoteName>();
GitCommitSha sha = "9fceb02d0ae598e95dc970b74767f19372d61af8".As<GitCommitSha>();

// These are distinct types — passing a GitBranchName where a GitCommitSha
// is expected is a compile error, not a runtime surprise.
```

## Advanced Usage

### Argument Vectors Are Inspectable Before They Run

Every builder's `BuildArguments()` is a pure computation with no I/O, so the exact command can be
asserted or logged before it executes:

```csharp
IReadOnlyList<string> arguments = repository.Status().BuildArguments();
// ["-C", "<path>", "--no-pager", "-c", "core.quotepath=false", "-c", "color.ui=false",
//  "status", "--porcelain=v2", "--branch", "-z"]
```

### Metadata-Only Repositories

A `GitRepository` produced by a hosting provider (rather than `IGitClient.OpenAsync` or
`DiscoverAsync`) carries hosting metadata but no `ProcessRunner`. Calling any verb on it throws
`InvalidOperationException` immediately, rather than failing later inside git:

```csharp
GitRepository metadataOnly = new() { LocalPath = somePath, Name = "GitIntegration".As<GitRepositoryName>() };

// Throws InvalidOperationException — obtain a runnable repository from IGitClient first.
_ = metadataOnly.Status();
```

## API Reference

### `IGitClient` / `GitClient`

The entry point to the local layer: finds and opens repositories, and reports on the git binary.

#### Methods

| Name | Return Type | Description |
|------|-------------|-------------|
| `GetVersionAsync(CancellationToken)` | `Task<GitVersion>` | Reports the version of the git binary being invoked. |
| `IsRepositoryAsync(AbsoluteDirectoryPath, CancellationToken)` | `Task<bool>` | Decides whether a path is inside a git working tree. Never throws for a non-repository path. |
| `OpenAsync(AbsoluteDirectoryPath, CancellationToken)` | `Task<GitRepository>` | Opens the repository containing a path. Throws `GitRepositoryNotFoundException` when there is none. |
| `DiscoverAsync(AbsoluteDirectoryPath, CancellationToken)` | `Task<GitRepository?>` | Opens the repository containing a path, returning `null` instead of throwing when there is none. |
| `Init(AbsoluteDirectoryPath)` | `IGitInitBuilder` | Creates a repository at a path. Probes first, so the result's `AlreadyExisted` can tell a caller whether one was already there. |
| `Clone(GitRepositoryRemotePath, AbsoluteDirectoryPath)` | `IGitCloneBuilder` | Clones a repository into a local working copy. |
| `Clone(GitRepository)` | `IGitCloneBuilder` | Clones the repository a hosting provider described, using its `RemotePath` and intended `LocalPath`. |

### `GitRepository`

Carries `LocalPath` plus optional hosting metadata, and exposes one builder factory per verb.

#### Properties

| Name | Type | Description |
|------|------|-------------|
| `LocalPath` | `AbsoluteDirectoryPath` | The working tree's local filesystem path. |
| `Name` | `GitRepositoryName?` | The repository name, when known. |
| `WebURI` | `GitRepositoryWebURI?` | The browser-facing URI, when known. |
| `RemotePath` | `GitRepositoryRemotePath?` | The remote clone path, when known. |
| `ProcessRunner` | `IGitProcessRunner?` | The runner this repository's verbs execute through; `null` on a metadata-only repository. |

#### Methods

| Name | Return Type | Description |
|------|-------------|-------------|
| `Status()` | `IGitStatusBuilder` | Builds `git status --porcelain=v2 --branch -z`. |
| `Log()` | `IGitLogBuilder` | Builds `git log -z` with this library's pinned format. |
| `Diff()` | `IGitDiffBuilder` | Builds `git diff --name-status -z`. |
| `Branches()` | `IGitBranchListBuilder` | Builds `git for-each-ref` over the branch namespaces. |
| `Remotes()` | `IGitRemoteListBuilder` | Builds `git remote -v`. |
| `RevParse(GitRefName)` | `IGitRevParseBuilder` | Builds `git rev-parse --verify` for a revision. |
| `Add()` | `IGitAddBuilder` | Builds `git add`. |
| `Commit(GitCommitMessage)` | `IGitCommitBuilder` | Builds `git commit`, then reads the new commit back with `git log -1`. |
| `CreateBranch(GitBranchName)` | `IGitBranchCreateBuilder` | Builds `git branch <name> [<start-point>]`. |
| `DeleteBranch(GitBranchName)` | `IGitBranchDeleteBuilder` | Builds `git branch --delete <name>`. |
| `Checkout(GitRefName)` | `IGitCheckoutBuilder` | Builds `git checkout`. |
| `AddRemote(GitRemoteName, GitRepositoryRemotePath)` | `IGitRemoteAddBuilder` | Builds `git remote add <name> <url>`. |
| `RemoveRemote(GitRemoteName)` | `IGitRemoteRemoveBuilder` | Builds `git remote remove <name>`. |
| `SetRemoteUrl(GitRemoteName, GitRepositoryRemotePath)` | `IGitRemoteSetUrlBuilder` | Builds `git remote set-url <name> <url>`. |
| `Fetch()` | `IGitFetchBuilder` | Builds `git fetch`, with `--porcelain` where the installed git supports it. |
| `Pull()` | `IGitPullBuilder` | Builds `git pull`. |
| `Push()` | `IGitPushBuilder` | Builds `git push --porcelain`. |
| `IsClonedAsync(CancellationToken)` | `Task<bool>` | Decides whether `LocalPath` currently holds a git working tree. |
| `OpenWebClient()` | `void` | Opens `WebURI` in the default browser, when it is an absolute `http`/`https` URI. |

### `IGitCommandBuilder<TResult>`

The shared contract every verb builder implements. A builder is single-use and not thread-safe.

#### Methods

| Name | Return Type | Description |
|------|-------------|-------------|
| `BuildArguments()` | `IReadOnlyList<string>` | The exact argument vector this builder will pass to git. Pure, no I/O. |
| `ExecuteAsync(CancellationToken)` | `Task<TResult>` | Runs the command, throwing `GitCommandException` when git exits non-zero. |
| `TryExecuteAsync(CancellationToken)` | `Task<GitResult<TResult>>` | Runs the command, reporting a non-zero exit as a result instead of throwing. |

### Verb Builders

| Interface | Extra Methods | Result |
|-----------|----------------|--------|
| `IGitStatusBuilder` | `WithUntrackedFiles(GitUntrackedFilesMode)`, `IncludeIgnored()` | `GitStatus` |
| `IGitLogBuilder` | `Take(int)`, `Skip(int)`, `ForRevision(GitRefName)`, `ForPath(RelativeFilePath)`, `FirstParentOnly()` | `IReadOnlyList<GitCommit>` |
| `IGitDiffBuilder` | `Staged()`, `Against(GitRefName)`, `Between(GitRefName, GitRefName)`, `DetectRenames()`, `DetectCopies()`, `ForPath(RelativeFilePath)` | `IReadOnlyList<GitDiffEntry>` |
| `IGitBranchListBuilder` | `LocalOnly()`, `RemoteOnly()` | `IReadOnlyList<GitBranch>` |
| `IGitRemoteListBuilder` | *(none)* | `IReadOnlyList<GitRemote>` |
| `IGitRevParseBuilder` | *(none — revision supplied via `GitRepository.RevParse`)* | `GitCommitSha` |
| `IGitInitBuilder` | `Bare()`, `WithInitialBranch(GitBranchName)` | `GitInitResult` |
| `IGitCloneBuilder` | `WithBranch(GitBranchName)`, `WithDepth(int)`, `Bare()`, `ReportingProgress(IProgress<string>)` | `GitRepository` |
| `IGitAddBuilder` | `ForPath(RelativeFilePath)`, `All()`, `UpdateTrackedOnly()` | `GitCompleted` |
| `IGitCommitBuilder` | `WithBody(string)`, `AllowEmpty()`, `StageTrackedFiles()`, `WithAuthor(GitAuthorName, GitAuthorEmail)` | `GitCommit` |
| `IGitBranchCreateBuilder` | `StartingAt(GitRefName)`, `Force()` | `GitCompleted` |
| `IGitBranchDeleteBuilder` | `Force()` | `GitCompleted` |
| `IGitCheckoutBuilder` | `CreatingBranch()`, `Force()`, `Detach()` | `GitCompleted` |
| `IGitRemoteAddBuilder` | `WithFetch()` | `GitCompleted` |
| `IGitRemoteRemoveBuilder` | *(none)* | `GitCompleted` |
| `IGitRemoteSetUrlBuilder` | `ForPushOnly()` | `GitCompleted` |
| `IGitFetchBuilder` | `FromRemote(GitRemoteName)`, `AllRemotes()`, `Prune()`, `WithTags()`, `WithDepth(int)`, `ReportingProgress(IProgress<string>)` | `GitFetchResult` |
| `IGitPullBuilder` | `FromRemote(GitRemoteName)`, `WithBranch(GitBranchName)`, `FastForwardOnly()`, `Rebase()`, `Prune()`, `ReportingProgress(IProgress<string>)` | `GitCompleted` |
| `IGitPushBuilder` | `ToRemote(GitRemoteName)`, `WithBranch(GitBranchName)`, `SettingUpstream()`, `Force()`, `ForceWithLease()`, `DeletingRemoteBranch()`, `DryRun()`, `ReportingProgress(IProgress<string>)` | `GitPushResult` |

### Result and Execution Models

| Type | Description |
|------|-------------|
| `GitOptions` | Configures the git executable path and a per-invocation timeout. |
| `IGitProcessRunner` | Runs the git executable with a given argument vector; implemented by `RunCommandGitProcessRunner`. |
| `GitResult<T>` | The outcome of a command run with `TryExecuteAsync`: either `Value` or `Error`, never both. |
| `GitCommandError` | The exit code, argument vector, and standard error of a failed invocation. |

### Exceptions

| Type | Thrown When |
|------|-------------|
| `GitException` | Base type for every failure originating in this library. |
| `GitExecutableNotFoundException` | The git executable could not be started. |
| `GitTimeoutException` | Git did not complete within the configured `GitOptions.Timeout`. |
| `GitParseException` | Git succeeded but produced output the parser could not interpret. |
| `GitCommandException` | Git ran and exited non-zero. Carries `ExitCode`, `Arguments`, and `StandardError`. |
| `GitRepositoryNotFoundException` | A `GitCommandException` specialization: the path is not inside a git working tree. |
| `GitNothingToCommitException` | A `GitCommandException` specialization: `git commit` was run with nothing staged. The one `commit` failure that is an ordinary program state rather than a fault. |
| `GitPushRejectedException` | A `GitCommandException` specialization: git refused at least one reference during `push`. Carries the parsed `Result` (`GitPushResult`) so the rejection detail is not lost. |
| `GitPullConflictException` | A `GitCommandException` specialization: `pull` left conflicts in the working tree. Use `Status()` to see which paths are unmerged. |
| `GitHostingException` | Base type for every hosting-layer failure. Does **not** derive from `GitException` — it carries HTTP concepts, not process ones. Carries `ProviderName`, `StatusCode`, `ResponseBody`. |
| `GitHostingAuthenticationException` | A `GitHostingException` specialization: the host rejected the request as unauthenticated, or the credential no longer grants access. |
| `GitHostingNotFoundException` | A `GitHostingException` specialization: the requested resource does not exist, or is not visible to the caller's credentials. |
| `GitHostingRateLimitException` | A `GitHostingException` specialization: the caller has exhausted its request quota. Carries `ResetsAt`, when the host reported one. |
| `GitHostingRequestException` | A `GitHostingException` specialization for any other non-success response — a malformed request, a validation failure, or a server-side error. |

### Result Models

| Type | Description |
|------|-------------|
| `GitStatus` | `Branch`, `Upstream`, `Ahead`, `Behind`, `IsDetached`, `Entries`, `IsClean`. |
| `GitStatusEntry` | `IndexState`, `WorkTreeState`, `Path`, `OriginalPath` for one changed path. |
| `GitCommit` | `Sha`, `TreeSha`, `ParentShas`, `Author`, `Committer`, `Subject`, `Body`. |
| `GitSignature` | `Name`, `Email`, `Timestamp` recorded on a commit. |
| `GitBranch` | `Name`, `Sha`, `Upstream`, `IsCurrent`, `IsRemote`. |
| `GitRemote` | `Name`, `FetchUrl`, `PushUrl`. |
| `GitDiffEntry` | `Kind`, `Path`, `OriginalPath`, `SimilarityPercent`. |
| `GitVersion` | `Major`, `Minor`, `Patch`, `Raw`, plus `AtLeast(major, minor)`. |
| `GitFileState` | Enum: `Unmodified`, `Modified`, `Added`, `Deleted`, `Renamed`, `Copied`, `Untracked`, `Ignored`, `Unmerged`, `TypeChanged`. |
| `GitChangeKind` | Enum: `Added`, `Copied`, `Deleted`, `Modified`, `Renamed`, `TypeChanged`, `Unmerged`, `Unknown`. |
| `GitUntrackedFilesMode` | Enum: `No`, `Normal`, `All`. |
| `GitCompleted` | The result of a mutating verb whose only outcome is success — `add`, `checkout`, branch creation/deletion, remote commands, and `pull`. Carries `Arguments`. |
| `GitInitResult` | `Repository`, `AlreadyExisted` — the outcome of `IGitClient.Init`. |
| `GitFetchResult` | `Updates`, `DetailAvailable`, `IsUpToDate` — the outcome of `Fetch()`. `IsUpToDate` is gated on `DetailAvailable` so an empty `Updates` from a pre-2.41 git is never mistaken for "nothing changed". |
| `GitPushResult` | `Updates`, `HasRejections` — the outcome of `Push()`. |
| `GitRefUpdate` | `Kind`, `Reference`, `Source`, `OldSha`, `NewSha`, `Summary`, `IsRejected` — one reference changed by a fetch or a push. |
| `GitRefUpdateKind` | Enum: `FastForward`, `Forced`, `Removed`, `Created`, `Rejected`, `UpToDate`, `TagUpdate`, `Unknown`. |
| `GitPullRequest` | `Number`, `Title`, `Description`, `SourceBranch`, `TargetBranch`, `Author`, `State`, `IsDraft`, `WebURI`, `CreatedAt` — one pull request, as reported by a hosting provider. A `null` optional field means the host did not report that value. |
| `GitPullRequestState` | Enum: `Open`, `Merged`, `Closed`. |

### `IGitHostingProvider`

The contract every hosting provider implements: repository enumeration, pull request listing, and
pull request creation, over whichever transport and authentication scheme the host requires.

#### Properties

| Name | Type | Description |
|------|------|-------------|
| `Name` | `GitProviderName` | Display name of the provider. |
| `Owner` | `GitProviderOwner` | The owner of the repositories in this provider. |
| `PersonaGUID` | `PersonaGUID` | The persona GUID used for authentication with the provider (from `ktsu.CredentialCache`). |
| `IsAuthenticated` | `bool` | Whether a credential is currently resolvable for this provider. |

#### Methods

| Name | Return Type | Description |
|------|-------------|-------------|
| `GetRepositoriesAsync(CancellationToken)` | `Task<IReadOnlyList<GitRepository>>` | Retrieves the repositories `Owner` has, from the host. Coverage differs by host — see `GitHubProvider` and `AzureDevOpsProvider` below. |
| `GetPullRequestsAsync(GitRepositoryName, CancellationToken)` | `Task<IReadOnlyList<GitPullRequest>>` | Retrieves a repository's **open** pull requests. The filter is requested explicitly of the host, not left to its default. |
| `CreatePullRequest(GitRepositoryName)` | `IGitPullRequestCreateBuilder` | Starts building a pull request for a repository. |
| `TryGetCredential(out Credential?)` | `bool` | Attempts to resolve a credential for this provider from the credential cache. |

### `GitProvider`

The abstract base both hosting providers derive from. Implements `IGitHostingProvider`; resolves
credentials via `TryGetCredential`/`ResolveCredential`, and issues every request through an
`HttpClient` it constructs itself.

### `GitHubProvider`

`GitProvider` implementation backed by Octokit. `GetRepositoriesAsync` returns only `Owner`'s
**public** repositories — GitHub's `GET /users/{login}/repos` does not honour authentication to
reveal private ones, and supplying a token does not widen this.

### `AzureDevOpsProvider`

`GitProvider` implementation built on a raw `HttpClient` against Azure DevOps's REST API
(`api-version=7.1`) — no Azure DevOps client library is referenced (see the Introduction).

#### Additional Properties

| Name | Type | Description |
|------|------|-------------|
| `Project` | `AzureDevOpsProjectName?` | Scopes repository enumeration to a project, or `null` to enumerate the whole organisation. **Required** for `GetPullRequestsAsync` and `CreatePullRequest` — Azure DevOps has no project-less pull request endpoint, and calling either without it throws `InvalidOperationException`. |

`GetRepositoriesAsync` returns everything the resolved credential can see — unlike
`GitHubProvider`, there is no public-only restriction.

### `IGitPullRequestCreateBuilder`

Collects a pull request's details before submitting it. `From`, `Into`, and `Titled` are required;
`Describing` and `AsDraft` are optional. A missing required value throws `InvalidOperationException`
from `ExecuteAsync`, not from the setter that left it unset, since parts may be supplied in any
order.

| Name | Return Type | Description |
|------|-------------|-------------|
| `From(GitBranchName)` | `IGitPullRequestCreateBuilder` | Sets the source branch. |
| `Into(GitBranchName)` | `IGitPullRequestCreateBuilder` | Sets the target branch. |
| `Titled(GitPullRequestTitle)` | `IGitPullRequestCreateBuilder` | Sets the title. |
| `Describing(string)` | `IGitPullRequestCreateBuilder` | Sets the description. |
| `AsDraft()` | `IGitPullRequestCreateBuilder` | Marks the pull request as a draft. |
| `ExecuteAsync(CancellationToken)` | `Task<GitPullRequest>` | Submits the pull request to the host. |

### `ServiceCollectionExtensions`

| Name | Return Type | Description |
|------|-------------|-------------|
| `AddGitIntegration(IServiceCollection)` | `IServiceCollection` | Registers git integration with default options, invoking the `git` found on `PATH`. |
| `AddGitIntegration(IServiceCollection, Action<GitOptions>)` | `IServiceCollection` | Registers git integration with configured options. Idempotent per service. |

Registers only the local layer. Hosting providers are constructed directly — see
[Working with a Hosting Provider](#working-with-a-hosting-provider) — since neither `GitHubProvider`
nor `AzureDevOpsProvider` has a constructor dependency a container could supply.

### Semantic Types

| Type | Wraps |
|------|-------|
| `GitAuthorEmail` | Commit author or committer email address |
| `GitAuthorName` | Commit author or committer name |
| `GitBranchName` | Branch name |
| `GitCommitMessage` | Commit message |
| `GitCommitSha` | Commit object id (abbreviated or full, including SHA-256 repositories) |
| `GitProviderName` | Hosting provider display name |
| `GitProviderOwner` | Account or organization owning a repository |
| `GitRefName` | A branch, tag, SHA, or revision expression |
| `GitRemoteName` | Remote name |
| `GitRepositoryName` | Repository name |
| `GitRepositoryRemotePath` | Clone path or URL |
| `GitRepositoryWebURI` | Repository web address |
| `AzureDevOpsProjectName` | Azure DevOps project name — scopes `AzureDevOpsProvider` repository enumeration, and required for its pull request operations |
| `GitPullRequestNumber` | Pull request's host-assigned number |
| `GitPullRequestTitle` | Pull request title |
| `GitPullRequestAuthor` | Host's identifier for the account that opened a pull request (a GitHub login, or an Azure DevOps unique name) |
| `GitPullRequestWebURI` | Pull request web address |

## Contributing

Contributions are welcome! Feel free to open issues or submit pull requests.

## License

This project is licensed under the MIT License. See the [LICENSE.md](LICENSE.md) file for details.
