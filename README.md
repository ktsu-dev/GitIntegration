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
mutating commands (`init`, `clone`, `add`, `commit`, branch creation and deletion, `checkout`, and
remote management) without shelling out or hand-parsing porcelain output yourself. The **hosting
layer** — the original half of this library — unifies access to hosted Git providers behind a
`GitProvider` abstraction, with a `GitHubProvider` implementation built on Octokit.

Every value that would otherwise be a bare `string` — a branch name, a commit SHA, a remote name, an
author email — is instead a validated semantic type built on `ktsu.Semantics`, so a `GitBranchName`
can no longer be accidentally passed where a `GitCommitSha` is expected.

Azure DevOps hosting support is planned but not yet implemented. The two Azure DevOps client
packages were deliberately left out of this release because they pull in `System.Data.SqlClient`,
which carries a known high-severity advisory, as a direct dependency of the published package.

## Features

- **Local Git Client**: `IGitClient`/`GitClient` finds and opens repositories — `GetVersionAsync`,
  `IsRepositoryAsync`, `OpenAsync`, `DiscoverAsync` — and creates new ones — `Init(...)`,
  `Clone(...)` — by delegating every invocation to `ktsu.RunCommand`.
- **Fluent Verb Builders**: `GitRepository` exposes one builder per read-only verb — `Status()`,
  `Log()`, `Diff()`, `Branches()`, `Remotes()`, `RevParse(...)` — and one per mutating verb —
  `Add()`, `Commit(...)`, `CreateBranch(...)`, `DeleteBranch(...)`, `Checkout(...)`,
  `AddRemote(...)`, `RemoveRemote(...)`, `SetRemoteUrl(...)` — each configurable via chained method
  calls and run with `ExecuteAsync` or the non-throwing `TryExecuteAsync`.
- **Strongly-Typed Results**: `GitStatus`, `GitCommit`, `GitBranch`, `GitRemote`, `GitDiffEntry`,
  `GitVersion`, `GitInitResult`, and `GitCompleted` records replace ad-hoc porcelain parsing with
  typed models — `GitCompleted` is the shared result for mutating verbs whose only outcome is
  success.
- **Reproducible Failures**: every command is scoped with `git -C <path>` instead of a process
  working directory, so a failing invocation's exact argument vector can be read off a
  `GitCommandException` and rerun verbatim.
- **Locale-Safe Parsing**: every invocation runs with `GIT_TERMINAL_PROMPT=0` (no hanging credential
  prompts) and `LC_ALL=C` (English, machine-stable output), which is what makes the output parsers
  safe to write against fixed English text.
- **Dependency Injection**: `AddGitIntegration()` registers the client, process runner, and options
  as singletons in one call.
- **Hosting Provider Abstraction**: `GitProvider` defines a common contract for enumerating and
  refreshing remote repositories; `GitHubProvider` implements it on top of Octokit.
- **Credential Resolution**: hosting providers integrate with `ktsu.CredentialCache`, so credentials
  come from the host's native keyring rather than configuration files.
- **Semantic Git Types**: 13 validated wrapper types for every identifier Git tooling passes around,
  so mismatched arguments fail at compile time rather than at runtime.

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

```csharp
using ktsu.GitIntegration;
using ktsu.Semantics.Strings;

GitProvider provider = new GitHubProvider
{
    Owner = "ktsu-dev".As<GitProviderOwner>(),
};

// Pulls credentials from the credential cache, then authenticates the client.
provider.RefreshRemoteRepositories();
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
| `GitCompleted` | The result of a mutating verb whose only outcome is success — `add`, `checkout`, branch creation/deletion, and the remote commands. Carries `Arguments`. |
| `GitInitResult` | `Repository`, `AlreadyExisted` — the outcome of `IGitClient.Init`. |

### `GitProvider`

Abstract base class describing a hosted Git provider.

#### Properties

| Name | Type | Description |
|------|------|-------------|
| `Name` | `GitProviderName` | Display name of the provider. |
| `Owner` | `GitProviderOwner` | The owner of the repositories in this provider. |
| `PersonaGUID` | `PersonaGUID` | The persona GUID used for authentication with the provider (from `ktsu.CredentialCache`). |
| `IsAuthenticated` | `bool` | Whether a credential is currently resolvable for this provider. |
| `Repositories` | `ConcurrentBag<GitRepository>` | The repositories known from the provider. |

#### Methods

| Name | Return Type | Description |
|------|-------------|-------------|
| `RefreshRemoteRepositories()` | `void` | Refreshes the provider's view of the remote repositories, authenticating first if a credential is available. |
| `TryGetCredential(out Credential?)` | `bool` | Attempts to resolve a credential for this provider from the credential cache. |

### `GitHubProvider`

`GitProvider` implementation backed by Octokit. Authenticates the underlying `GitHubClient` from a
`CredentialWithUsernamePassword` resolved via `TryGetCredential`.

### `ServiceCollectionExtensions`

| Name | Return Type | Description |
|------|-------------|-------------|
| `AddGitIntegration(IServiceCollection)` | `IServiceCollection` | Registers git integration with default options, invoking the `git` found on `PATH`. |
| `AddGitIntegration(IServiceCollection, Action<GitOptions>)` | `IServiceCollection` | Registers git integration with configured options. Idempotent per service. |

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
| `AzureDevOpsProjectName` | Azure DevOps project name (reserved for planned Azure DevOps support) |

## Contributing

Contributions are welcome! Feel free to open issues or submit pull requests.

## License

This project is licensed under the MIT License. See the [LICENSE.md](LICENSE.md) file for details.
